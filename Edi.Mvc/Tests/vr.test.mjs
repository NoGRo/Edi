import test from 'node:test';
import assert from 'node:assert/strict';
import { detectVideoFormat, eyeUv, eyeAspect } from '../wwwroot/js/player/vr/format.mjs';
import { constrainPlacement, controllerButtons, createStillnessGate, easingAlpha, enforcementStep, insideFollowContour, gripStickAction,
    horizontalStick, intensityStickVelocity, stickAction } from '../wwwroot/js/player/vr/motion.mjs';
import { videoGeometry } from '../wwwroot/js/player/vr/video-surface.mjs';
import { createMediaLayer } from '../wwwroot/js/player/vr/media-layer.mjs';

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

test('video curvature bends only the horizontal plane and remains bounded', () => {
    const flat = videoGeometry({ videoWidth: 1920, videoHeight: 1080 }, { stereo: 'mono', width: 2.4, curvature: 0 }, 0);
    const curved = videoGeometry({ videoWidth: 1920, videoHeight: 1080 }, { stereo: 'mono', width: 2.4, curvature: 1.6 }, 0);
    assert.equal(flat.attributes.position.count, 4);
    assert.ok(curved.attributes.position.count > flat.attributes.position.count);
    const position = curved.attributes.position;
    assert.ok(position.getZ(0) > 0, 'curved edges wrap toward the viewer');
    assert.ok(Math.abs(Math.abs(position.getX(0)) - 1.2) < 1e-6, 'curvature preserves visible width');
    assert.equal(new Set(Array.from({ length: position.count }, (_, index) => position.getY(index))).size, 2);
    flat.dispose(); curved.dispose();
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

test('follow contour scales independently with the current video width and height', () => {
    assert.equal(insideFollowContour({ x: .9, y: 0 }, 2.4, 1.35), true);
    assert.equal(insideFollowContour({ x: 0, y: .6 }, 2.4, 1.35), false);
    assert.equal(insideFollowContour({ x: 1.1, y: 0 }, 2.4, 1.35), false);
    assert.equal(insideFollowContour({ x: 1.1, y: 0 }, 6, 3.375), true);
});

test('enforcement starts timidly, is confident in the middle and slows for final precision', () => {
    const timid = enforcementStep(1, 1, 0, 1 / 90, .6);
    const confident = enforcementStep(.6, 1, .6, 1 / 90, .6);
    const precise = enforcementStep(.01, 1, 1.2, 1 / 90, .6);
    assert.ok(timid < confident);
    assert.ok(precise < confident);
    assert.ok(precise > 0 && precise <= .01);
});

test('native media and WebGL controls are composed as separate ordered streams', () => {
    const previousBinding = globalThis.XRMediaBinding, previousTransform = globalThis.XRRigidTransform;
    class Transform { constructor(position = {}, orientation = {}) { this.position = position; this.orientation = orientation; } }
    class Binding { createQuadLayer(video, options) { return { video, ...options }; } }
    globalThis.XRMediaBinding = Binding; globalThis.XRRigidTransform = Transform;
    try {
        const projectionLayer = { kind: 'controls' };
        const session = { renderState: { layers: [projectionLayer] },
            updateRenderState(update) { this.renderState = { ...this.renderState, ...update }; } };
        const media = createMediaLayer({ session, referenceSpace: {}, projectionLayer,
            video: { videoWidth: 3840, videoHeight: 1080 },
            settings: { width: 2.4, curvature: 0, stereo: 'sbs', swapEyes: false } });
        assert.equal(media.rebuild(), true);
        assert.equal(session.renderState.layers.length, 2);
        assert.equal(session.renderState.layers[0].layout, 'stereo-left-right');
        assert.equal(session.renderState.layers[1], projectionLayer);
        media.update({ x: 1, y: 2, z: -3 }, { x: 0, y: 0, z: 0, w: 1 });
        assert.deepEqual(session.renderState.layers[0].transform.position, { x: 1, y: 2, z: -3 });
        media.remove();
        assert.deepEqual(session.renderState.layers, [projectionLayer]);
    } finally {
        globalThis.XRMediaBinding = previousBinding; globalThis.XRRigidTransform = previousTransform;
    }
});

test('VR screen placement stays in front of the viewer and inside a useful viewing cone', () => {
    assert.deepEqual(constrainPlacement({ x: 20, y: -20, z: 2 }), { x: .3825, y: -.29250000000000004, z: -.45 });
    assert.deepEqual(constrainPlacement({ x: 5, y: 5, z: -2 }), { x: 1.7, y: 1.3, z: -2 });
    assert.deepEqual(constrainPlacement({ x: .5, y: -.5, z: -20 }), { x: .5, y: -.5, z: -8 });
});

test('stick intensity is the default; horizontal seek requires the explicit video target', () => {
    assert.deepEqual(stickAction([0, 0, 1, 0]), { kind: 'intensity', value: 0 });
    assert.deepEqual(stickAction([0, 0, 1, 0], 'seek'), { kind: 'seek', value: 1 });
    assert.deepEqual(stickAction([0, 0, 0, -1], 'volume'), { kind: 'volume', value: 1 });
    assert.deepEqual(stickAction([0, 0, 0, -.15]), { kind: 'intensity', value: 0 });
    assert.deepEqual(stickAction([0, 0, 0, 1]), { kind: 'intensity', value: -1 });
});

test('Quest intensity uses a fine center curve and a refresh-independent full-stick speed', () => {
    assert.equal(intensityStickVelocity(0), 0);
    assert.ok(intensityStickVelocity(.25) < 2);
    assert.ok(intensityStickVelocity(-.25) > -2);
    assert.equal(intensityStickVelocity(1), 8);
    assert.equal(intensityStickVelocity(-1), -8);
});

test('grip stick separates size and curve while a lateral stick has a deliberate threshold', () => {
    assert.deepEqual(gripStickAction([0, 0, .61, -.61]), { curve: .5, scale: .5 });
    assert.equal(horizontalStick([.64, 0, 0, 0], 'standard'), 0);
    assert.equal(horizontalStick([-.8, 0, .7, 0], 'standard'), -1);
    assert.equal(horizontalStick([0, 0, .8, 0], 'xr-standard'), 1);
});

test('A/B are rising-edge actions and unsupported gamepad layouts are ignored', () => {
    const source = { gamepad: { mapping: 'xr-standard', buttons: Array.from({ length: 6 }, () => ({ pressed: false })) } };
    source.gamepad.buttons[4].pressed = true;
    assert.equal(controllerButtons(source).primary, true);
    assert.equal(controllerButtons(source, [true, false]).primary, false);
    source.gamepad.buttons[5].pressed = true;
    assert.equal(controllerButtons(source, [true, false]).variant, true);
    source.gamepad.buttons[3].pressed = true;
    assert.equal(controllerButtons(source, [true, true, false]).stick, true);
    source.gamepad.mapping = '';
    assert.equal(controllerButtons(source).primary, false);
    assert.equal(controllerButtons(source).variant, false);
});
