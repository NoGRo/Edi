import * as THREE from '../../../lib/vr/three.module.min.js';
import { createVideoSurface } from './video-surface.mjs';
import { createHtmlPanels } from './html-panels.mjs';
import { clamp, controllerButtons, createStillnessGate, easingAlpha, stickAction } from './motion.mjs';

export async function createVrRuntime({ session, elements, preferences, state, setIntensity,
    exit, onPlacement, onFormat, onPrimary, onVariant, onError }) {
    // Quest is fill-rate constrained. WebXR supplies its own compositor, so
    // disabling MSAA and using its foveated framebuffer saves substantially
    // more work than it costs in edge quality.
    const renderer = new THREE.WebGLRenderer({ antialias: false, powerPreference: 'high-performance', precision: 'mediump' });
    renderer.domElement.className = 'vr-canvas';
    document.body.append(renderer.domElement);
    renderer.setSize(window.innerWidth, window.innerHeight);
    renderer.xr.enabled = true;
    renderer.xr.setReferenceSpaceType('local');
    renderer.xr.setFramebufferScaleFactor(.8);
    const scene = new THREE.Scene();
    scene.background = new THREE.Color('#05080d');
    const camera = new THREE.PerspectiveCamera(70, 1, .03, 100);
    const video = createVideoSurface(elements.video, preferences);
    scene.add(video.group);
    const menuRig = new THREE.Group(); video.group.add(menuRig);
    const raycaster = new THREE.Raycaster();
    const head = { position: new THREE.Vector3(), rotation: new THREE.Quaternion(), valid: false };
    const relativePosition = new THREE.Vector3(), relativeRotation = new THREE.Quaternion();
    const desiredPosition = new THREE.Vector3(), desiredRotation = new THREE.Quaternion();
    const dragMatrix = new THREE.Matrix4(), dragPosition = new THREE.Vector3();
    const dragRotation = new THREE.Quaternion(), dragScale = new THREE.Vector3();
    const gate = createStillnessGate();
    const controllers = [];
    let panels, disposed = false, placed = false, catchingUp = false, grab = null;
    let lastTime = null, lastStickTime = 0, stickAccumulator = 0, lastStickKind = null;
    let snapshotErrorReported = false;

    function captureRelative() {
        if (!head.valid) return;
        const inverse = head.rotation.clone().invert();
        relativePosition.copy(video.group.position).sub(head.position).applyQuaternion(inverse);
        relativeRotation.copy(inverse).multiply(video.group.quaternion);
        gate.reset(); catchingUp = false;
    }
    function recenterVideo() {
        relativePosition.set(preferences.offsetX, preferences.offsetY, -preferences.distance);
        relativeRotation.identity();
        video.group.position.copy(relativePosition).applyQuaternion(head.rotation).add(head.position);
        video.group.quaternion.copy(head.rotation);
        gate.reset(); catchingUp = false;
    }
    function toggleMenus() {
        const visible = panels.toggle();
        elements.vrMenusToggle?.setAttribute('aria-pressed', String(visible));
    }
    function intersection(controller) {
        scene.updateMatrixWorld(true);
        raycaster.ray.origin.setFromMatrixPosition(controller.object.matrixWorld);
        raycaster.ray.direction.set(0, 0, -1).transformDirection(controller.object.matrixWorld);
        raycaster.layers.set(0);
        const ui = raycaster.intersectObjects(panels.meshes(), false)[0];
        if (ui) {
            const hit = panels.hitAt(ui.object.userData.panel, ui.uv);
            if (hit) return { type: 'panel', distance: ui.distance, point: ui.point, hit };
        }
        return videoIntersection();
    }
    function videoIntersection() {
        if (!video.group.visible) return null;
        raycaster.layers.set(1);
        const surface = raycaster.intersectObjects(video.meshes(), false)[0];
        return surface ? { type: 'video', distance: surface.distance, point: surface.point } : null;
    }
    function beginGrab(controller, kind) {
        if (grab) return false;
        const space = kind === 'grip' && controller.grip.visible ? controller.grip : controller.object;
        video.group.updateMatrixWorld(true);
        grab = { controller, kind, space, offset: space.matrixWorld.clone().invert().multiply(video.group.matrixWorld),
            position: video.group.position.clone(), rotation: video.group.quaternion.clone(), moved: false };
        gate.reset(); catchingUp = false;
        return true;
    }
    function moveGrab() {
        if (!grab) return;
        dragMatrix.multiplyMatrices(grab.space.matrixWorld, grab.offset);
        dragMatrix.decompose(dragPosition, dragRotation, dragScale);
        if (dragPosition.distanceTo(grab.position) > .025 || dragRotation.angleTo(grab.rotation) > .03) grab.moved = true;
        if (grab.moved) { video.group.position.copy(dragPosition); video.group.quaternion.copy(dragRotation); }
    }
    function finishGrab(controller, kind) {
        if (grab?.controller !== controller || grab.kind !== kind) return;
        scene.updateMatrixWorld(true); moveGrab();
        const moved = grab.moved;
        grab = null;
        captureRelative();
        if (moved) {
            preferences.offsetX = clamp(relativePosition.x, -3, 3);
            preferences.offsetY = clamp(relativePosition.y, -3, 3);
            preferences.distance = clamp(-relativePosition.z, .45, 8);
            onPlacement();
        } else if (kind === 'trigger') toggleMenus();
    }
    function gripStart(controller) {
        if (disposed || !panels || !head.valid) return;
        // Grip grabs the video even when a menu is in front of it. Selection
        // keeps its normal UI priority, so clicking and grabbing stay distinct.
        intersection(controller);
        if (videoIntersection()) beginGrab(controller, 'grip');
    }
    function cancel(controller) {
        if (controller.press?.type === 'panel') panels?.release(controller.press.action, null, true);
        if (grab?.controller === controller) { grab = null; captureRelative(); }
        controller.press = null;
    }
    function start(controller) {
        if (disposed || !panels || !head.valid) return;
        const target = intersection(controller);
        if (target?.type === 'panel') controller.press = { type: 'panel', action: panels.press(target.hit) };
        else if (target?.type === 'video' && !grab) {
            beginGrab(controller, 'trigger');
            controller.press = { type: 'video' };
        } else controller.press = { type: 'empty' };
    }
    function finish(controller) {
        if (disposed || !panels) return;
        const target = intersection(controller);
        if (controller.press?.type === 'panel') panels.release(controller.press.action, target?.hit);
        else if (controller.press?.type === 'video') finishGrab(controller, 'trigger');
        else if (controller.press?.type === 'empty') toggleMenus();
        controller.press = null;
    }
    function targetKind(target) {
        const element = target?.hit?.element;
        if (element?.closest('.custom-volume-control')) return 'volume';
        if (element?.closest('#customVideoControls')) return 'seek';
        return 'intensity';
    }
    function applyStick(action, dt, now) {
        if (!action.value || grab) { lastStickKind = null; stickAccumulator = 0; return; }
        if (action.kind !== lastStickKind) { lastStickKind = action.kind; stickAccumulator = 0; }
        if (action.kind === 'volume') {
            elements.video.volume = clamp(elements.video.volume + action.value * dt * .35, 0, 1);
            elements.video.muted = elements.video.volume === 0;
        } else if (action.kind === 'seek') {
            if (!Number.isFinite(elements.video.duration)) return;
            stickAccumulator += action.value * dt * 20;
            // Commit seeks at 5Hz, allowing the existing buffering/resync events
            // to settle instead of restarting the decoder on every XR frame.
            if (now - lastStickTime >= .2) {
                elements.video.currentTime = clamp(elements.video.currentTime + stickAccumulator, 0, elements.video.duration);
                stickAccumulator = 0; lastStickTime = now;
            }
        } else if (state.intensityEnabled) {
            stickAccumulator += action.value * dt * 30;
            if (now - lastStickTime >= .08 && Math.abs(stickAccumulator) >= 1) {
                const step = Math.trunc(stickAccumulator);
                const value = clamp(state.currentIntensity + step, 0, 100);
                stickAccumulator -= step; lastStickTime = now;
                if (value !== state.currentIntensity) void setIntensity(value);
            }
        }
    }
    function follow(now, dt) {
        const ready = gate.ready(now, { position: head.position.toArray(), rotation: head.rotation.toArray() }, preferences.followDelay);
        if (!preferences.follow || grab || !ready) { catchingUp = false; return; }
        desiredPosition.copy(relativePosition).applyQuaternion(head.rotation).add(head.position);
        desiredRotation.copy(head.rotation).multiply(relativeRotation);
        const distance = video.group.position.distanceTo(desiredPosition);
        const angle = video.group.quaternion.angleTo(desiredRotation);
        if (!catchingUp && (distance > .1 || angle > Math.PI / 22.5)) catchingUp = true;
        if (!catchingUp) return;
        const alpha = easingAlpha(dt, preferences.followEase);
        video.group.position.lerp(desiredPosition, alpha);
        video.group.quaternion.slerp(desiredRotation, alpha);
        if (distance < .005 && angle < .005) catchingUp = false;
    }
    function animate(milliseconds, frame) {
        if (disposed || !panels || !frame) return;
        const now = milliseconds / 1000;
        const dt = lastTime === null ? 0 : clamp(now - lastTime, 0, .05); lastTime = now;
        const pose = frame.getViewerPose(renderer.xr.getReferenceSpace());
        if (!pose) return;
        head.position.copy(pose.transform.position);
        head.rotation.copy(pose.transform.orientation); head.valid = true;
        if (!placed) { placed = true; recenterVideo(); }
        onFormat();
        video.group.visible = elements.video.readyState >= 2;
        follow(now, dt);
        panels.update(now);
        let hovered = null, activeStick = null;
        for (const controller of controllers) {
            if (!controller.source || !controller.object.visible) continue;
            const target = intersection(controller);
            controller.line.scale.z = target ? Math.min(20, target.distance) : 4;
            if (target) {
                // Use the actual raycast collision, rather than assuming the
                // panel is perpendicular to the controller ray.
                controller.hitPoint.copy(target.point);
                controller.object.worldToLocal(controller.hitPoint);
                controller.dot.position.copy(controller.hitPoint);
                controller.dot.material.color.set(target.type === 'panel' ? 0x2f81f7 : 0xffffff);
            }
            controller.dot.visible = Boolean(target);
            if (target?.hit) hovered = target.hit;
            if (controller.press?.type === 'panel') panels.move(controller.press.action, target?.hit);
            const buttons = controllerButtons(controller.source, controller.previousButtons);
            controller.previousButtons = buttons.buttons;
            if (buttons.primary) onPrimary();
            if (buttons.variant) onVariant();
            if (buttons.menu) toggleMenus();
            if (controller.source.gamepad?.mapping === 'xr-standard') {
                const action = stickAction(controller.source.gamepad.axes, targetKind(target));
                if (action.value && (!activeStick || controller.source.handedness === 'right')) activeStick = action;
            }
        }
        panels.hover(hovered);
        applyStick(activeStick || { value: 0 }, dt, now);
        if (grab && !grab.space.visible) cancel(grab.controller);
        moveGrab();
        renderer.render(scene, camera);
    }
    function visibilityChanged() {
        if (session.visibilityState !== 'visible') {
            elements.video.pause();
            controllers.forEach(cancel);
            lastTime = null; gate.reset();
        }
    }
    function dispose() {
        if (disposed) return;
        disposed = true;
        renderer.setAnimationLoop(null);
        session.removeEventListener('end', dispose);
        session.removeEventListener('visibilitychange', visibilityChanged);
        controllers.forEach(controller => {
            cancel(controller);
            for (const [type, listener] of controller.listeners) controller.object.removeEventListener(type, listener);
            controller.line.geometry.dispose(); controller.line.material.dispose();
            controller.dot.geometry.dispose(); controller.dot.material.dispose();
        });
        panels?.dispose(); video.dispose(); renderer.dispose();
        renderer.domElement.remove();
    }
    session.addEventListener('end', dispose, { once: true });
    session.addEventListener('visibilitychange', visibilityChanged);
    try {
        for (let index = 0; index < 2; index++) {
            const object = renderer.xr.getController(index);
            const grip = renderer.xr.getControllerGrip(index); scene.add(grip);
            const line = new THREE.Line(new THREE.BufferGeometry().setFromPoints([
                new THREE.Vector3(), new THREE.Vector3(0, 0, -1)]), new THREE.LineBasicMaterial({ color: '#58a6ff', depthTest: false }));
            const dot = new THREE.Mesh(new THREE.SphereGeometry(.012, 10, 8),
                new THREE.MeshBasicMaterial({ color: '#ffffff', depthTest: false }));
            line.renderOrder = dot.renderOrder = 30;
            object.add(line, dot); scene.add(object);
            const controller = { object, grip, line, dot, hitPoint: new THREE.Vector3(), source: null, press: null, previousButtons: [], listeners: [] };
            const listen = (type, listener) => { object.addEventListener(type, listener); controller.listeners.push([type, listener]); };
            listen('connected', event => { controller.source = event.data; controller.previousButtons = []; });
            listen('disconnected', () => { cancel(controller); controller.source = null; });
            listen('selectstart', () => start(controller));
            listen('selectend', () => finish(controller));
            listen('squeezestart', () => gripStart(controller));
            listen('squeezeend', () => finishGrab(controller, 'grip'));
            controllers.push(controller);
        }
        await renderer.xr.setSession(session);
        renderer.xr.setFoveation(1);
        if (disposed) return { dispose };
        panels = await createHtmlPanels({ rig: menuRig, elements, preferences, exit, onError(error) {
            if (!snapshotErrorReported) { snapshotErrorReported = true; onError(error); }
        } });
        if (disposed) { panels.dispose(); return { dispose }; }
        renderer.setAnimationLoop(animate);
        elements.vrMenusToggle?.setAttribute('aria-pressed', 'true');
        return { dispose, recenterVideo, rebuildVideo: video.rebuild, toggleMenus,
            settingsChanged(key) {
                if (key === 'follow') captureRelative();
                else if (['width', 'stereo', 'swapEyes', 'halfResolution'].includes(key)) video.rebuild();
                if (['distance', 'offsetX', 'offsetY'].includes(key)) recenterVideo();
                gate.reset();
            }
        };
    } catch (error) { dispose(); throw error; }
}
