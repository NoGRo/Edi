import test from 'node:test';
import assert from 'node:assert/strict';
import { createAssetManager } from '../wwwroot/js/player/assets.mjs';
import { createSync } from '../wwwroot/js/player/synchronization.mjs';
import { createPlaybackEvents } from '../wwwroot/js/player/playback-events.mjs';

const file = name => new File(['{}'], name);
const deferred = () => {
    let resolve;
    const promise = new Promise(done => { resolve = done; });
    return { promise, resolve };
};

function assetRig(saved = []) {
    const calls = [], published = [];
    let records = saved.map(file => ({ file }));
    const manager = createAssetManager({
        request: async (path, options = {}) => {
            calls.push({ path, ...options });
            return new Response(JSON.stringify([]), { status: 200 });
        },
        repository: {
            getStoredItems: async () => records,
            saveAssets: async files => { records = files.map(file => ({ file })); },
            clearStoredVideos: async () => {}
        },
        publish: files => published.push(files)
    });
    return { manager, calls, published };
}

test('asset library merges by case-insensitive filename and excludes generated definitions', async () => {
    const old = file('Scene.funscript'), replacement = file('SCENE.funscript');
    const { manager } = assetRig([old, file('other.funscript')]);
    const merged = await manager.merge([replacement, file('Definitions_auto.csv')]);
    assert.equal(merged.length, 2);
    assert.equal(merged[0], replacement);
    assert.deepEqual(manager.forPlaylist(merged, [{ name: 'scene.mp4' }]), [replacement]);
    assert.deepEqual(manager.forPlaylist(merged, []), []);
});

test('playlist assets retain definitions, bundles and matching variants and axes', () => {
    const { manager } = assetRig();
    const files = ['Definitions.csv','BundleDefinition1.txt','scene.funscript','scene.fast.L0.funscript',
        'scene.mp3','scenery.funscript','other.funscript'].map(file);
    assert.deepEqual(manager.forPlaylist(files, [{ name: 'SCENE.mp4' }]), files.slice(0, 5));
});

test('successful uploads remember the complete server set without deleting the persistent library', async () => {
    const scene = file('scene.funscript'), other = file('other.funscript');
    const { manager, calls } = assetRig([scene, other]);
    await manager.restore();
    assert.deepEqual(manager.getUploaded(), []);
    await manager.upload([scene]);
    assert.deepEqual(manager.getUploaded(), [scene]);
    assert.deepEqual(await manager.fetchForVariants(), [scene]);
    assert.deepEqual(await manager.merge([]), [scene, other]);
    assert.equal(calls.length, 1);
    assert.equal(calls[0].method, 'POST');
    assert.equal(calls[0].body.getAll('files')[0].name, scene.name);
    await manager.clear();
    assert.deepEqual(manager.getUploaded(), []);
    assert.deepEqual(await manager.merge([]), []);
    assert.equal(calls.at(-1).method, 'DELETE');
});

test('failed upload leaves the last confirmed set intact', async () => {
    let fail = false;
    const manager = createAssetManager({
        request: async () => { if (fail) throw new Error('offline'); return new Response('[]'); },
        publish: () => {}
    });
    const original = file('scene.funscript');
    await manager.upload([original]);
    fail = true;
    await assert.rejects(manager.upload([file('replacement.funscript')]), /offline/);
    assert.deepEqual(manager.getUploaded(), [original]);
});

test('generated variants upload only new files and share the complete merged cache', async () => {
    const calls = [], notifications = [];
    const manager = createAssetManager({
        request: async (path, options) => { calls.push({ path, ...options }); return new Response('[]'); },
        publish: () => {}, notifyPersistence: files => notifications.push(files)
    });
    const source = file('scene.funscript'), generated = file('scene.AutoDouble.funscript');
    await manager.upload([source]);
    await manager.uploadGenerated([generated], [source, generated]);
    assert.equal(calls.at(-1).method, 'PUT');
    assert.deepEqual(calls.at(-1).body.getAll('files').map(file => file.name), [generated.name]);
    assert.deepEqual(await manager.fetchForVariants(), [source, generated]);
    assert.deepEqual(manager.getUploaded(), [source, generated]);
    assert.deepEqual(notifications, [[source, generated]]);
});

test('concurrent variant fetches share downloads and an older download cannot replace a new upload', async () => {
    const listing = deferred();
    let reads = 0;
    const manager = createAssetManager({
        request: async (path, options = {}) => {
            if (options.method === 'POST') return new Response('[]');
            reads++;
            return listing.promise;
        },
        publish: () => {}
    });
    const first = manager.fetchForVariants(), second = manager.fetchForVariants();
    const uploaded = file('new.funscript');
    await manager.upload([uploaded]);
    listing.resolve(new Response('[]'));
    assert.deepEqual(await first, [uploaded]);
    assert.deepEqual(await second, [uploaded]);
    assert.equal(reads, 1);
    await manager.prepareVariant({ videoName: 'new.mp4', selection: 'real:default' });
    assert.equal(reads, 1);
});

function playbackRig() {
    const commands = [];
    const media = Object.assign(new EventTarget(), {
        currentTime: 3, duration: 20, paused: false, ended: false, seeking: false, readyState: 4,
        play: async () => { media.paused = false; }, pause: () => { media.paused = true; }
    });
    const state = { playlist: [{ id: 'one', name: 'scene.mp4' }], currentId: 'one',
        definitions: [{ fileName: 'scene.funscript', name: 'gallery', startTime: 1000, endTime: 20000 }],
        playbackOptions: { loopMode: 'none', autoplay: false },
        ediStopped: true, lastGallery: null, currentIntensity: 50, strokerPaused: false,
        strokerNeedsResync: false, intensityNeedsResync: false };
    const sync = createSync({ state, media, currentItem: () => state.playlist[0],
        renderPlaybackOptions: () => {}, showStrokerStateOverlay: () => {},
        command: async path => { commands.push(path); }
    });
    return { state, media, sync, commands };
}

test('synchronization works with a non-DOM player and preserves seek, pause and resync commands', async () => {
    const { state, media, sync, commands } = playbackRig();
    await sync.startEdi();
    await sync.startEdi();
    assert.deepEqual(commands, ['/Edi/Play/gallery?seek=2000']);
    await sync.handleStrokerInput('mouse');
    assert.equal(state.strokerPaused, true);
    assert.equal(commands.at(-1), '/Edi/Pause?untilResume=false');
    media.currentTime = 5;
    state.strokerNeedsResync = true;
    await sync.handleStrokerInput('mouse');
    assert.equal(commands.at(-1), '/Edi/Play/gallery?seek=4000');
    await sync.stopEdi();
    assert.equal(commands.at(-1), '/Edi/Stop');
    assert.equal(state.ediStopped, true);
});

test('asset reload keeps stroker paused and restores intensity on the next synchronization', async () => {
    const { state, sync, commands } = playbackRig();
    await sync.startEdi();
    await sync.handleStrokerInput('space');
    const count = commands.length;
    await sync.resyncAfterAssetsReload();
    assert.equal(commands.length, count);
    assert.equal(state.strokerNeedsResync, true);
    await sync.handleStrokerInput('space');
    assert.deepEqual(commands.slice(-2), ['/Edi/Play/gallery?seek=2000', '/Edi/Intensity/50']);
});

test('media events preserve buffering, seeking, loop and autoplay behavior without a DOM', async () => {
    const { state, media } = playbackRig();
    const calls = [];
    const events = createPlaybackEvents({ state, media,
        renderCustomControls: () => {}, renderPlaybackOptions: () => {},
        startEdi: () => calls.push('sync'), stopEdi: async () => calls.push('stop'),
        startPlaybackTimer: () => calls.push('timer'), stopPlaybackTimer: () => {},
        saveCurrentPosition: () => {}, clearSavedPosition: () => {},
        selectVideo: async (id, autoplay) => calls.push([id, autoplay])
    });
    events.mount();
    media.dispatchEvent(new Event('waiting'));
    assert.equal(calls.at(-1), 'stop');
    state.strokerPaused = true;
    media.dispatchEvent(new Event('seeking'));
    assert.equal(state.strokerNeedsResync, true);
    state.playlist.push({ id: 'two' });
    state.playbackOptions.loopMode = 'playlist';
    media.dispatchEvent(new Event('ended'));
    await Promise.resolve();
    assert.deepEqual(calls.at(-1), ['two', true]);
    state.playbackOptions.loopMode = 'video';
    media.dispatchEvent(new Event('ended'));
    await Promise.resolve();
    assert.equal(media.currentTime, 0);
});

test('video transition keeps device pause until the user resumes at the new video time', async () => {
    const { state, media, sync, commands } = playbackRig();
    await sync.startEdi();
    await sync.handleStrokerInput('space');
    await sync.stopEdi('Previous video stopped.');
    assert.equal(state.strokerPaused, true);
    assert.equal(state.strokerPauseMethod, 'space');
    assert.equal(state.strokerNeedsResync, true);
    state.definitions = [{ fileName: 'scene.funscript', name: 'next', startTime: 0, endTime: 20000 }];
    media.currentTime = 1;
    commands.length = 0;
    await sync.startEdi();
    assert.deepEqual(commands, []);
    await sync.toggleStrokerPlayback();
    assert.equal(state.strokerPaused, false);
    assert.deepEqual(commands, ['/Edi/Play/next?seek=1000']);
});

test('explicit combined resume releases device pause when the video is also paused', async () => {
    const { state, media, sync, commands } = playbackRig();
    await sync.startEdi();
    await sync.handleStrokerInput('space');
    await sync.stopEdi();
    media.paused = true;
    commands.length = 0;
    await sync.handleStrokerInput('space');
    assert.equal(state.strokerPaused, false);
    assert.equal(media.paused, false);
    assert.deepEqual(commands, []);
    await sync.startEdi();
    assert.deepEqual(commands, ['/Edi/Play/gallery?seek=2000']);
});
