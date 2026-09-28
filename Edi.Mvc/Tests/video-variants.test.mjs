import test from 'node:test';
import assert from 'node:assert/strict';
import { variantsForVideo, videoVariantPair } from '../wwwroot/js/player/video-variants.mjs';
import { createAssetManager } from '../wwwroot/js/player/assets.mjs';
import { autoVariantName } from '../wwwroot/js/funscript-tools.mjs';
import { createDeviceRouting } from '../wwwroot/js/player/device-routing.mjs';

const script = name => new File([JSON.stringify({ actions: [{ at: 0, pos: 0 }, { at: 1000, pos: 100 }] })], name);
const auto = autoVariantName('double');

test('video options come from matching assets, including axes and audio, and device variants', () => {
    const files = ['Scene.fast.Sway.funscript', 'scene.slow.funscript', 'scene.audio.mp3',
        'other.unrelated.funscript', `scene.${auto}.funscript`].map(script);
    assert.deepEqual(variantsForVideo({ variants: ['fast', 'slow', 'audio', auto, 'unrelated', 'None'] },
        { name: 'SCENE.mp4', definitions: [] }, files), ['fast', 'slow', 'audio', auto, 'None']);
    assert.deepEqual(variantsForVideo({ variants: ['fast', 'None'] }, { name: 'empty.mp4' }, files), ['None']);
    assert.deepEqual(variantsForVideo({ variants: ['fast', 'None'] }, null, files), ['None']);
});

test('history wins, shared selections continue, missing choices fall back and Stopped survives history', () => {
    const input = { variants: ['default', 'fast', 'None'], selected: 'fast',
        previous: { primary: 'real:fast', secondary: 'real:old' }, defaults: { defaultVariant: 'default' } };
    assert.equal(videoVariantPair(input).primary, 'real:fast');
    assert.equal(videoVariantPair(input).secondary, 'real:default');
    assert.equal(videoVariantPair({ ...input, history: { primary: 'real:default' } }).primary, 'real:default');
    assert.equal(videoVariantPair({ ...input, selected: 'None', history: { primary: 'real:fast' } }).primary, 'real:None');
    const pair = videoVariantPair({ ...input, history: { primary: 'auto:double', bases: { primary: 'fast' } } });
    assert.equal(pair.primary, 'auto:double');
    assert.equal(pair.bases.primary, 'fast');
});

function assetRig(files) {
    const calls = [];
    const manager = createAssetManager({
        repository: { getStoredItems: async () => files.map(file => ({ file })), saveAssets: async () => {} },
        publish: () => {}, notifyPersistence: () => {},
        request: async (path, options = {}) => {
            calls.push({ path, ...options });
            return new Response(JSON.stringify([]));
        }
    });
    return { manager, calls };
}

test('restored video originals upload once; repeated preparation shares confirmation without queries', async () => {
    const { manager, calls } = assetRig([script('one.fast.funscript'), script('two.fast.funscript')]);
    await manager.restore();
    await manager.prepareVariant({ videoName: 'one.mp4' });
    assert.deepEqual(calls.map(call => call.method || 'GET'), ['GET', 'PUT']);
    assert.deepEqual(calls[1].body.getAll('files').map(file => file.name), ['one.fast.funscript']);
    await manager.prepareVariant({ videoName: 'one.mp4' });
    await manager.prepareVariant({ videoName: 'one.mp4', selection: 'real:fast' });
    assert.equal(calls.length, 2);
    await manager.prepareVariant({ videoName: 'two.mp4' });
    assert.equal(calls.length, 3);
});

test('automatic preparation generates current video axes, uploads once and reuses concurrent requests', async () => {
    const files = [script('one.fast.Sway.funscript'), script('one.fast.Surge.funscript'), script('two.fast.funscript')];
    const { manager, calls } = assetRig(files);
    await manager.upload(files);
    calls.length = 0;
    const request = { videoName: 'one.mp4', selection: 'auto:double', baseVariant: 'fast' };
    const results = await Promise.all([manager.prepareVariant(request), manager.prepareVariant(request)]);
    assert.deepEqual(results.map(result => result.changed), [true, false]);
    assert.equal(calls.length, 1);
    const generatedName = autoVariantName('double', 'fast');
    assert.deepEqual(calls[0].body.getAll('files').map(file => file.name), [`one.${generatedName}.Sway.funscript`, `one.${generatedName}.Surge.funscript`]);
    assert.deepEqual(await manager.generationBases('one.mp4'), ['fast']);
    assert.equal(calls.length, 1);
    await assert.rejects(manager.prepareVariant({ ...request, baseVariant: auto }), /original base/);
    assert.equal(calls.length, 1);
});

test('replacing original content regenerates its automatic derivative once', async () => {
    const original = script('one.fast.funscript');
    const { manager, calls } = assetRig([original]);
    await manager.upload([original]);
    const request = { videoName: 'one.mp4', selection: 'auto:double', baseVariant: 'fast' };
    await manager.prepareVariant(request);
    const generated = (await manager.fetchForVariants()).find(file => file.name.includes(auto));
    const replacement = new File([JSON.stringify({ actions: [{ at: 0, pos: 30 }, { at: 1000, pos: 80 }] })], original.name);
    await manager.upload([replacement, generated]);
    calls.length = 0;
    await manager.prepareVariant(request);
    await manager.prepareVariant(request);
    assert.equal(calls.length, 1);
});

test('different original bases keep independent automatic files on the same video', async () => {
    const files = [script('one.fast.funscript'), script('one.slow.funscript')];
    const { manager } = assetRig(files);
    await manager.upload(files);
    const fast = await manager.prepareVariant({ videoName: 'one.mp4', selection: 'auto:double', baseVariant: 'fast' });
    const slow = await manager.prepareVariant({ videoName: 'one.mp4', selection: 'auto:double', baseVariant: 'slow' });
    assert.notEqual(fast.variant, slow.variant);
    const names = (await manager.fetchForVariants()).map(file => file.name);
    assert.ok(names.includes(`one.${fast.variant}.funscript`));
    assert.ok(names.includes(`one.${slow.variant}.funscript`));
});

test('video setup waits for the device editor to register its preparation handler', async () => {
    const routing = createDeviceRouting({ waitForVideoHandler: true });
    let prepared = false, settled = false;
    const context = { name: 'one.mp4', definitions: [] };
    const preparing = routing.setVideoContext(context).then(() => { settled = true; });
    await Promise.resolve();
    assert.equal(settled, false);
    routing.setVideoHandler(async value => { assert.equal(value, context); prepared = true; });
    await preparing;
    assert.equal(prepared, true);
});

test('uncached variant fetch downloads only the active video scripts and audio', async () => {
    const paths = ['/Edi/Assets/other-game.funscript', '/Edi/Assets/one.fast.Sway.funscript',
        '/Edi/Assets/one.mp3', '/Edi/Assets/Definitions.csv'];
    const calls = [];
    const manager = createAssetManager({ publish: () => {},
        request: async path => {
            calls.push(path);
            return new Response(path === '/Edi/Assets' ? JSON.stringify(paths) : '{}');
        }
    });
    const files = await manager.fetchForVariants('one.webm');
    assert.deepEqual(files.map(file => file.name), ['one.fast.Sway.funscript', 'one.mp3']);
    assert.deepEqual(calls, ['/Edi/Assets', ...paths.slice(1, 3)]);
    const next = await manager.fetchForVariants('other-game.mp4');
    assert.deepEqual(next.map(file => file.name), ['one.fast.Sway.funscript', 'one.mp3', 'other-game.funscript']);
    assert.deepEqual(calls.slice(3), ['/Edi/Assets', paths[0]]);
    const count = calls.length;
    await manager.fetchForVariants('one.webm');
    await manager.fetchForVariants('other-game.mp4');
    assert.equal(calls.length, count);
});

test('device playback waits for pending video variants even when media already plays', async () => {
    let release;
    const ready = new Promise(resolve => { release = resolve; });
    const calls = [];
    const routing = createDeviceRouting({ command: async path => calls.push(path) });
    routing.setVideoHandler(() => ready);
    const preparing = routing.setVideoContext({ name: 'one.webm', definitions: [] });
    const playing = routing.play('/Edi/Play/one');
    await Promise.resolve();
    assert.deepEqual(calls, []);
    release();
    await Promise.all([preparing, playing]);
    assert.deepEqual(calls, ['/Edi/Play/one']);
});
