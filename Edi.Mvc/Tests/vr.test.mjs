import test from 'node:test';
import assert from 'node:assert/strict';
import { detectVideoFormat, eyeUv, eyeAspect } from '../wwwroot/js/player/vr/format.mjs';
import { controllerButtons, createStillnessGate, easingAlpha, stickAction } from '../wwwroot/js/player/vr/motion.mjs';
import { videoGeometry } from '../wwwroot/js/player/vr/video-surface.mjs';

test('VR filename conventions detect only flat mono or side-by-side screens', () => {
    for (const [name, stereo, swapEyes] of [
        ['scene.mp4', 'mono', false], ['Scene_3DH.MP4', 'sbs', false],
        ['scene_HSBS.mkv', 'sbs', false], ['scene_halfsbs.mp4', 'sbs', false],
        ['scene_180_LR.mp4', 'sbs', false], ['scene_VR180_RL.mp4', 'sbs', true],
        ['scene_360_SBS.mp4', 'sbs', false], ['360days_2.mp4', 'mono', false]
    ]) {
        const format = detectVideoFormat(name);
        assert.deepEqual([format.stereo, format.swapEyes], [stereo, swapEyes], name);
        assert.equal('projection' in format, false);
    }
});

test('each eye receives its own reversible SBS region', () => {
    assert.deepEqual(eyeUv(.5, .5, 0, { stereo: 'sbs' }), [.25, .5]);
    assert.deepEqual(eyeUv(.5, .5, 1, { stereo: 'sbs' }), [.75, .5]);
    assert.deepEqual(eyeUv(.5, .5, 0, { stereo: 'sbs', swapEyes: true }), [.75, .5]);
    assert.equal(eyeAspect(3840, 1080, { stereo: 'sbs' }), 16 / 9);
    assert.equal(eyeAspect(1920, 1080, detectVideoFormat('movie_HSBS.mp4')), 16 / 9);
});

test('video geometry stays a simple flat plane with separate SBS eye UVs', () => {
    const settings = { ...detectVideoFormat('scene_180_LR.mp4'), width: 2.4 };
    const geometries = [0, 1].map(eye => videoGeometry({ videoWidth: 3840, videoHeight: 1080 }, settings, eye));
    geometries.forEach((geometry, eye) => {
        assert.equal(geometry.attributes.position.count, 4);
        const uv = geometry.attributes.uv;
        for (let index = 0; index < uv.count; index++) {
            assert.ok(uv.getX(index) >= eye / 2 && uv.getX(index) <= (eye + 1) / 2);
            assert.ok(uv.getY(index) >= 0 && uv.getY(index) <= 1);
        }
        geometry.dispose();
    });
});

test('head follow waits for stillness, tolerates micro motion and resets after cumulative turns', () => {
    const gate = createStillnessGate();
    const pose = yaw => ({ position: [0, 1.6, 0], rotation: [0, Math.sin(yaw / 2), 0, Math.cos(yaw / 2)] });
    assert.equal(gate.ready(0, pose(0), 2), false);
    assert.equal(gate.ready(1.9, pose(.005), 2), false);
    assert.equal(gate.ready(2, pose(.008), 2), true);
    assert.equal(gate.ready(2.1, pose(.04), 2), false);
    assert.equal(gate.ready(4, pose(.04), 2), false);
    assert.equal(gate.ready(4.11, pose(.04), 2), true);
    gate.reset();
    assert.equal(gate.ready(20, pose(.04), 2), false);
});

test('smoothing is frame-rate independent and cannot overshoot after a suspended frame', () => {
    assert.ok(Math.abs(1 - Math.pow(1 - easingAlpha(1 / 90, .6), 90) - easingAlpha(1, .6)) < 1e-12);
    assert.equal(easingAlpha(-1, .6), 0);
    assert.ok(easingAlpha(10, .6) <= 1);
});

test('stick intensity is the default; horizontal seek requires the explicit video target', () => {
    assert.deepEqual(stickAction([0, 0, 1, 0]), { kind: 'intensity', value: 0 });
    assert.deepEqual(stickAction([0, 0, 1, 0], 'seek'), { kind: 'seek', value: 1 });
    assert.deepEqual(stickAction([0, 0, 0, -1], 'volume'), { kind: 'volume', value: 1 });
    assert.deepEqual(stickAction([0, 0, 0, -.15]), { kind: 'intensity', value: 0 });
    assert.deepEqual(stickAction([0, 0, 0, 1]), { kind: 'intensity', value: -1 });
});

test('A/B are rising-edge actions and unsupported gamepad layouts are ignored', () => {
    const source = { gamepad: { mapping: 'xr-standard', buttons: Array.from({ length: 6 }, () => ({ pressed: false })) } };
    source.gamepad.buttons[4].pressed = true;
    assert.equal(controllerButtons(source).primary, true);
    assert.equal(controllerButtons(source, [true, false]).primary, false);
    source.gamepad.buttons[5].pressed = true;
    assert.equal(controllerButtons(source, [true, false]).variant, true);
    source.gamepad.buttons[3].pressed = true;
    assert.equal(controllerButtons(source, [true, true, false]).menu, true);
    assert.equal(controllerButtons(source, [true, true, true]).menu, false);
    source.gamepad.mapping = '';
    assert.equal(controllerButtons(source).primary, false);
    assert.equal(controllerButtons(source).variant, false);
});
