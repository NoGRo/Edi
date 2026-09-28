import test from 'node:test';
import assert from 'node:assert/strict';
import { createDeviceRouting } from '../wwwroot/js/player/device-routing.mjs';
import { createSync } from '../wwwroot/js/player/synchronization.mjs';

function rig(preferences = {}, requestOverride) {
    const calls = [];
    const devices = ['First', 'Second', 'Third'].map(name => ({
        name, isReady: true, selectedVariant: 'primary', min: 10, max: 90, baseMin: 10, baseMax: 90
    }));
    const routing = createDeviceRouting({ preferences,
        command: async path => calls.push({ path }),
        request: async (path, options) => {
            calls.push({ path, body: options.body && JSON.parse(options.body) });
            await requestOverride?.(path);
        }
    });
    routing.setDevices(devices);
    return { routing, devices, calls };
}

test('all participants use only global pause, resume and intensity', async () => {
    const { routing, calls } = rig();
    await routing.setPaused(true);
    await routing.setPaused(false);
    await routing.setIntensity(50);
    assert.deepEqual(calls.map(call => call.path), [
        '/Edi/Pause?untilResume=false', '/Edi/Resume?AtCurrentTime=true', '/Edi/Intensity/50'
    ]);
});

test('selective pause hides None and resumes the latest variant without play or asset queries', async () => {
    const { routing, devices, calls } = rig({ Second: { pause: false }, Third: { pause: false } });
    await routing.setPaused(true);
    assert.equal(devices[0].selectedVariant, 'None');
    assert.equal(routing.visibleVariant(devices[0]), 'primary');
    routing.setDevices(devices.map(device => ({ ...device })));
    await routing.setVariants({ First: 'secondary', Second: 'secondary' });
    await routing.setPaused(false);
    assert.deepEqual(calls.map(call => call.body), [
        { First: 'None' }, { Second: 'secondary' }, { First: 'secondary' }
    ]);
    assert.ok(calls.every(call => call.path === '/Devices/Variants?persist=false'));
});

test('selective intensity scales configured ranges, restores exclusions and skips unchanged values', async () => {
    const { routing, devices, calls } = rig({ Second: { intensity: false }, Third: { intensity: false } });
    await routing.setIntensity(50);
    assert.equal(devices[0].max, 50);
    assert.equal(devices[1].max, 90);
    await routing.setIntensity(50);
    assert.deepEqual(calls.map(call => call.path), ['/Devices/First/Range/10-50?persist=false']);
    await routing.toggle(devices[0], 'intensity');
    assert.equal(calls.at(-1).path, '/Devices/First/Range/10-90?persist=false');
});

test('changing from global intensity to selective restores the excluded range', async () => {
    const { routing, devices, calls } = rig();
    await routing.setIntensity(0);
    await routing.toggle(devices[1], 'intensity');
    assert.equal(devices[0].max, 10);
    assert.equal(devices[1].max, 90);
    assert.equal(calls.at(-1).path, '/Devices/Second/Range/10-90?persist=false');
});

test('selective range requests dispatch in parallel and newer wheel values replace pending values', async () => {
    let release;
    const blocked = new Promise(resolve => { release = resolve; });
    const { routing, calls } = rig({ Third: { intensity: false } }, () => blocked);
    const first = routing.setIntensity(20);
    routing.setIntensity(30);
    routing.setIntensity(70);
    assert.equal(calls.length, 2);
    release();
    await first;
    assert.deepEqual(calls.map(call => call.path), [
        '/Devices/First/Range/10-26?persist=false', '/Devices/Second/Range/10-26?persist=false',
        '/Devices/First/Range/10-66?persist=false', '/Devices/Second/Range/10-66?persist=false'
    ]);
});

test('pause participation can change while paused without resuming included devices', async () => {
    const { routing, devices, calls } = rig();
    await routing.setPaused(true);
    await routing.toggle(devices[1], 'pause');
    assert.deepEqual(calls.map(call => call.path), [
        '/Edi/Pause?untilResume=false', '/Devices/Variants?persist=false', '/Edi/Resume?AtCurrentTime=true'
    ]);
    assert.deepEqual(calls[1].body, { First: 'None', Third: 'None' });
    await routing.toggle(devices[1], 'pause');
    routing.setDevices(devices.map(device => ({ ...device })));
    await routing.setPaused(false);
    assert.deepEqual(calls.at(-2).body, { First: 'primary', Third: 'primary' });
    assert.equal(calls.at(-1).path, '/Edi/Resume?AtCurrentTime=true');
});

test('no pause participants produces no device commands and one device keeps the original controls', async () => {
    const { routing, devices, calls } = rig(Object.fromEntries(['First', 'Second', 'Third']
        .map(name => [name, { pause: false, intensity: false }])));
    await routing.setPaused(true);
    await routing.setPaused(false);
    assert.equal(calls.length, 0);
    routing.setDevices([devices[0]]);
    await routing.setPaused(true);
    assert.equal(calls.at(-1).path, '/Edi/Pause?untilResume=false');
});

test('failed pause rolls back state and can be retried', async () => {
    const { routing, devices } = rig({ Second: { pause: false } }, () => { throw new Error('offline'); });
    await assert.rejects(routing.setPaused(true), /offline/);
    assert.equal(routing.isPaused(devices[0]), false);
});

test('a reconnect inherits selective pause and intensity without extra asset or playback requests', async () => {
    const { routing, devices, calls } = rig({ Second: { pause: false, intensity: false },
        Third: { pause: false, intensity: false } });
    devices[0].isReady = false;
    await routing.setDevices(devices);
    await routing.setPaused(true);
    await routing.setIntensity(50);
    calls.length = 0;
    await routing.setDevices(devices.map(device => ({ ...device, isReady: true })));
    assert.deepEqual(calls.map(call => call.path).sort(), [
        '/Devices/First/Range/10-50?persist=false', '/Devices/Variants?persist=false'
    ]);
    assert.equal(routing.isPaused(devices[0]), true);
});

test('intensity dispatch is independent of a blocked variant request', async () => {
    let release;
    const blocked = new Promise(resolve => { release = resolve; });
    const { routing, calls } = rig({}, path => path.startsWith('/Devices/Variants') ? blocked : undefined);
    const variants = routing.setVariants({ First: 'secondary' });
    await Promise.resolve();
    await routing.setIntensity(50);
    assert.deepEqual(calls.map(call => call.path), ['/Devices/Variants?persist=false', '/Edi/Intensity/50']);
    release();
    await variants;
});

test('new gallery can release a global pause without an extra resume', async () => {
    const { routing, calls } = rig();
    await routing.setPaused(true);
    await routing.play('/Edi/Play/next?seek=200');
    assert.deepEqual(calls.map(call => call.path), ['/Edi/Pause?untilResume=false', '/Edi/Play/next?seek=200']);
});

test('selective pause keeps the EDI timeline updating for devices that continue playing', async () => {
    const calls = [];
    const state = { definitions: [
        { name: 'next', fileName: 'scene.funscript', startTime: 1000, endTime: 3000 }
    ], strokerPaused: true, lastGallery: 'previous', ediStopped: false };
    const sync = createSync({ state, media: { paused: false, currentTime: 1.5 },
        currentItem: () => ({ name: 'scene.mp4' }), renderPlaybackOptions: () => {},
        showStrokerStateOverlay: () => {}, allPause: () => false,
        command: async path => calls.push(path)
    });
    await sync.startEdi();
    await sync.toggleStrokerPlayback();
    assert.deepEqual(calls, ['/Edi/Play/next?seek=500', '/Edi/Resume?AtCurrentTime=true']);
});
