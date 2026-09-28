import test from 'node:test';
import assert from 'node:assert/strict';
import { createPlaylist } from '../wwwroot/js/player/playlist.mjs';
import { createSync } from '../wwwroot/js/player/synchronization.mjs';
import { createPlaybackEvents } from '../wwwroot/js/player/playback-events.mjs';
import { deviceRouting } from '../wwwroot/js/player/device-routing.mjs';

test('autoplay replaces the video with Play only and retains paused device controls', async () => {
    const node = () => ({ classList: { toggle() {} }, append() {}, replaceChildren() {},
        setAttribute() {}, addEventListener() {}, dataset: {} });
    globalThis.document = { createElement: node, createElementNS: node };
    globalThis.localStorage = { setItem() {} };
    try {
        const calls = [];
        const state = { playlist: [{ id: 'one', name: 'one.mp4' }, { id: 'two', name: 'two.mp4' }],
            currentId: 'one', definitions: [{ fileName: 'two.funscript', name: 'two', startTime: 0, endTime: 10000 }],
            ediStopped: false, lastGallery: 'one', strokerPaused: false, playbackOptions: {} };
        const media = Object.assign(new EventTarget(), { paused: false, currentTime: 0,
            pause() { this.paused = true; },
            setSource() {},
            async play() { this.paused = false; this.dispatchEvent(new Event('play')); } });
        const sync = createSync({ state, media, currentItem: () => state.playlist.find(item => item.id === state.currentId),
            renderPlaybackOptions() {}, showStrokerStateOverlay() {},
            allPause: () => true, command: async path => { calls.push(path); } });
        createPlaybackEvents({ state, media, renderCustomControls() {}, startEdi: sync.startEdi,
            startPlaybackTimer() {}, stopPlaybackTimer() {}, stopEdi: sync.stopEdi,
            saveCurrentPosition() {}, renderPlaybackOptions() {} }).mount();
        deviceRouting.setVideoHandler(async () => {
            // Source replacement delivers the old video's pause/abort asynchronously.
            media.dispatchEvent(new Event('pause'));
            media.dispatchEvent(new Event('abort'));
        });
        const playlist = createPlaylist({ state, media,
            elements: { playlistElement: node(), playlistCount: node(), playlistDurationTotal: node() },
            saveCurrentPosition() {}, stopEdi: sync.stopEdi, restorePosition() {}, renderDiscreteProgress() {} });
        await playlist.selectVideo('two', true);
        await sync.startEdi();
        assert.deepEqual(calls, ['/Edi/Play/two?seek=0']);
        assert.equal(state.suppressPause, false);

        await sync.toggleStrokerPlayback(false, 'space');
        calls.length = 0;
        await playlist.selectVideo('one', true);
        await sync.startEdi();
        assert.deepEqual(calls, []);
        assert.equal(state.strokerPaused, true);
        assert.equal(state.strokerPauseMethod, 'space');
        assert.equal(state.strokerNeedsResync, true);
        state.definitions.push({ fileName: 'one.funscript', name: 'one', startTime: 0, endTime: 10000 });
        media.currentTime = 2;
        await sync.toggleStrokerPlayback();
        assert.deepEqual(calls, ['/Edi/Play/one?seek=2000']);
        assert.equal(state.strokerNeedsResync, false);
        await sync.toggleStrokerPlayback();
        await sync.toggleStrokerPlayback();
        assert.deepEqual(calls.slice(1), ['/Edi/Pause?untilResume=false', '/Edi/Resume?AtCurrentTime=true']);
        await sync.toggleStrokerPlayback();
        calls.length = 0;

        await playlist.selectVideo('two', false);
        assert.deepEqual(calls, ['/Edi/Stop']);
        assert.equal(state.strokerPaused, true);
    } finally {
        delete globalThis.document;
        delete globalThis.localStorage;
    }
});
