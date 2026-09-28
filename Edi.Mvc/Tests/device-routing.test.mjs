import test from 'node:test';
import assert from 'node:assert/strict';
import { createDeviceRouting } from '../wwwroot/js/player/device-routing.mjs';
import { createSync } from '../wwwroot/js/player/synchronization.mjs';

function rig(preferences = {}, requestOverride) {
    const calls = [];
    const devices = ['First', 'Second', 'Third'].map(name => ({
        name, isReady: true, selectedVariant: 'primary', min: 0, max: 100, baseMin: 0, baseMax: 100
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
    assert.equal(devices[1].max, 100);
    await routing.setIntensity(50);
    assert.deepEqual(calls.map(call => call.path), ['/Devices/First/Range/0-50?persist=false']);
    await routing.toggle(devices[0], 'intensity');
    assert.equal(calls.at(-1).path, '/Devices/First/Range/0-100?persist=false');
});

test('changing from global intensity to selective restores the excluded range', async () => {
    const { routing, devices, calls } = rig();
    await routing.setIntensity(0);
    await routing.toggle(devices[1], 'intensity');
    assert.equal(devices[0].max, 0);
    assert.equal(devices[1].max, 100);
    assert.equal(calls.at(-1).path, '/Devices/Second/Range/0-100?persist=false');
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
        '/Devices/First/Range/0-20?persist=false', '/Devices/Second/Range/0-20?persist=false',
        '/Devices/First/Range/0-70?persist=false', '/Devices/Second/Range/0-70?persist=false'
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
        '/Devices/First/Range/0-50?persist=false', '/Devices/Variants?persist=false'
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

test('both configured bounds collapse proportionally to the player-only center', async () => {
    const { routing, devices, calls } = rig();
    await routing.setRangeCenter(devices[0], 80);
    await routing.setIntensity(50);
    assert.deepEqual([devices[0].min, devices[0].max], [40, 90]);
    assert.deepEqual(routing.effectiveRange(devices[0]), { min: 40, max: 90 });
    await routing.setIntensity(0);
    assert.deepEqual([devices[0].min, devices[0].max], [80, 80]);
    await routing.setIntensity(100);
    assert.deepEqual([devices[0].min, devices[0].max], [0, 100]);
    assert.ok(calls.every(call => call.path.includes('/Range/') && call.path.endsWith('?persist=false')));
    assert.deepEqual([devices[0].baseMin, devices[0].baseMax], [0, 100]);
});

test('default zero also collapses a nonzero configured minimum to zero', async () => {
    const { routing, devices } = rig();
    Object.assign(devices[0], { baseMin: 20, baseMax: 80, min: 20, max: 80 });
    await routing.setIntensity(50);
    assert.deepEqual([devices[0].min, devices[0].max], [10, 40]);
    await routing.setIntensity(0);
    assert.deepEqual([devices[0].min, devices[0].max], [0, 0]);
});

test('resetting center restores the lower bound before returning to global intensity', async () => {
    const { routing, devices, calls } = rig();
    await routing.setRangeCenter(devices[0], 80);
    await routing.setIntensity(50);
    await routing.setRangeCenter(devices[0], 0);
    assert.equal(calls.at(-1).path, '/Devices/First/Range/0-50?persist=false');
    await routing.setIntensity(25);
    assert.equal(calls.at(-1).path, '/Edi/Intensity/25');
});

test('center respects intensity participation and follows edited ranges', async () => {
    const { routing, devices } = rig({ Second: { intensity: false, rangeCenter: 50 } });
    await routing.setRangeCenter(devices[0], 50);
    await routing.setIntensity(0);
    assert.deepEqual([devices[0].min, devices[0].max], [50, 50]);
    assert.deepEqual([devices[1].min, devices[1].max], [0, 100]);
    Object.assign(devices[0], { baseMin: 30, baseMax: 90 });
    await routing.setIntensity(50);
    assert.deepEqual([devices[0].min, devices[0].max], [40, 70]);
    await routing.setRangeCenter(devices[0], 80);
    assert.deepEqual([devices[0].min, devices[0].max], [55, 85]);
});

test('saved player center survives replacement device objects and clamps to the output scale', async () => {
    const preferences = { First: { rangeCenter: 80 } };
    const { routing, devices } = rig(preferences);
    await routing.setIntensity(0);
    const reconnected = devices.map(device => ({ ...device, isReady: false }));
    await routing.setDevices(reconnected);
    assert.equal(routing.rangeCenter(reconnected[0]), 80);
    await routing.setRangeCenter(reconnected[0], 150);
    assert.equal(preferences.First.rangeCenter, 100);
});

test('new gallery preserves global pause until explicit resume', async () => {
    const { routing, calls } = rig();
    await routing.setPaused(true);
    await routing.play('/Edi/Play/next?seek=200');
    assert.deepEqual(calls.map(call => call.path), ['/Edi/Pause?untilResume=false']);
    await routing.setPaused(false);
    await routing.play('/Edi/Play/next?seek=200');
    assert.equal(calls.at(-1).path, '/Edi/Play/next?seek=200');
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

test('stop and replacement playback preserve selective pause, None and output ranges', async () => {
    const { routing, devices, calls } = rig({ Second: { pause: false }, Third: { pause: false } });
    await routing.setVariants({ Third: 'None' });
    await routing.setIntensity(0);
    await routing.setPaused(true);
    calls.length = 0;
    await routing.stop('/Edi/Stop');
    await routing.setVideoContext({ name: 'next.mp4' });
    await routing.play('/Edi/Play/next?seek=0');
    assert.deepEqual(calls, [{ path: '/Edi/Stop' }, { path: '/Edi/Play/next?seek=0' }]);
    assert.equal(routing.isPaused(devices[0]), true);
    assert.equal(devices[0].selectedVariant, 'None');
    assert.equal(routing.visibleVariant(devices[0]), 'primary');
    assert.equal(devices[2].selectedVariant, 'None');
    assert.deepEqual(devices.map(device => [device.min, device.max]), [[0, 0], [0, 0], [0, 0]]);
});

test('stop preserves global pause and replacement playback sends no play command', async () => {
    const { routing, devices, calls } = rig();
    await routing.setPaused(true);
    calls.length = 0;
    await routing.stop('/Edi/Stop');
    await routing.play('/Edi/Play/next');
    assert.deepEqual(calls, [{ path: '/Edi/Stop' }]);
    assert.ok(devices.every(device => routing.isPaused(device)));
});

test('explicit activation starts the pending gallery with seek without Resume, then pause cycles normally', async () => {
    const { routing, devices, calls } = rig();
    await routing.setPaused(true);
    calls.length = 0;
    await routing.play('/Edi/Play/next?seek=1500', { resumePaused: true });
    assert.deepEqual(calls, [{ path: '/Edi/Play/next?seek=1500' }]);
    assert.ok(devices.every(device => !routing.isPaused(device)));
    await routing.setPaused(true);
    await routing.setPaused(false);
    assert.deepEqual(calls.slice(1).map(call => call.path),
        ['/Edi/Pause?untilResume=false', '/Edi/Resume?AtCurrentTime=true']);
});
