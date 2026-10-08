import * as THREE from '../../../lib/vr/three.module.min.js';
import { createVideoSurface } from './video-surface.mjs';
import { createMediaLayer } from './media-layer.mjs';
import { clamp, constrainPlacement, controllerButtons, createStillnessGate, enforcementStep, insideFollowContour,
    gripStickAction, horizontalStick, intensityStickVelocity, stickAction } from './motion.mjs';

function createExitControl(video) {
    const canvas = document.createElement('canvas'); canvas.width = 512; canvas.height = 160;
    const context = canvas.getContext('2d');
    context.fillStyle = 'rgba(15, 20, 28, .94)'; context.roundRect(4, 4, 504, 152, 34); context.fill();
    context.strokeStyle = '#58a6ff'; context.lineWidth = 8; context.stroke();
    context.fillStyle = '#f0f6fc'; context.font = '600 54px system-ui'; context.textAlign = 'center';
    context.textBaseline = 'middle'; context.fillText('Exit VR', 256, 82);
    const texture = new THREE.CanvasTexture(canvas); texture.colorSpace = THREE.SRGBColorSpace;
    const material = new THREE.MeshBasicMaterial({ map: texture, transparent: true, depthTest: false, toneMapped: false });
    const button = new THREE.Mesh(new THREE.PlaneGeometry(.48, .15), material);
    const hitMaterial = new THREE.MeshBasicMaterial({ transparent: true, opacity: 0, depthWrite: false, colorWrite: false });
    const hit = new THREE.Mesh(new THREE.PlaneGeometry(.82, .38), hitMaterial);
    const group = new THREE.Group(); group.add(hit, button); button.position.z = .006; button.visible = false;
    button.renderOrder = 20; hit.userData.exitControl = true; button.userData.exitControlButton = true;
    function layout() {
        const { width, height } = video.dimensions();
        group.position.set(width / 2 - .24, -height / 2 - .13, .012);
    }
    layout();
    return { group, hit, layout, hover(value) { button.visible = value; },
        dispose() { button.geometry.dispose(); material.dispose(); texture.dispose(); hit.geometry.dispose(); hitMaterial.dispose(); } };
}

export async function createVrRuntime({ session, elements, preferences, state, setIntensity,
    exit, onPlacement, onFormat, onPrimary, onVariant, onError }) {
    // Quest is fill-rate constrained. WebXR supplies its own compositor, so
    // disabling MSAA and using its foveated framebuffer saves substantially
    // more work than it costs in edge quality.
    const renderer = new THREE.WebGLRenderer({ alpha: true, antialias: false, powerPreference: 'high-performance', precision: 'mediump' });
    renderer.domElement.className = 'vr-canvas';
    document.body.append(renderer.domElement);
    renderer.setSize(window.innerWidth, window.innerHeight);
    renderer.xr.enabled = true;
    renderer.xr.setReferenceSpaceType('local');
    renderer.xr.setFramebufferScaleFactor(.8);
    const scene = new THREE.Scene();
    scene.background = null;
    const camera = new THREE.PerspectiveCamera(70, 1, .03, 100);
    const video = createVideoSurface(elements.video, preferences);
    scene.add(video.group);
    // Video and UI remain separate scene branches. Only this lightweight
    // Three.js exit affordance is rendered over VR; desktop HTML stays in place.
    const uiRig = new THREE.Group(); scene.add(uiRig);
    const exitControl = createExitControl(video); uiRig.add(exitControl.group);
    const raycaster = new THREE.Raycaster();
    const head = { position: new THREE.Vector3(), rotation: new THREE.Quaternion(), valid: false };
    const relativePosition = new THREE.Vector3(), desiredPosition = new THREE.Vector3();
    const dragMatrix = new THREE.Matrix4(), dragPosition = new THREE.Vector3();
    const dragRotation = new THREE.Quaternion(), dragScale = new THREE.Vector3();
    const placementRotation = new THREE.Quaternion(), faceMatrix = new THREE.Matrix4();
    const gazeOrigin = new THREE.Vector3(), gazeDirection = new THREE.Vector3(), gazePoint = new THREE.Vector3();
    const inverseVideo = new THREE.Matrix4(), followDelta = new THREE.Vector3();
    const gate = createStillnessGate();
    const controllers = [];
    let mediaLayer, disposed = false, placed = false, catchingUp = false, grab = null;
    let outsideSince = null, followStarted = 0, followJourney = 0;
    let lastTime = null, lastStickTime = 0, stickAccumulator = 0, lastStickKind = null;
    let lastSurfaceUpdate = 0;
    let frameErrorReported = false;
    let diagnosticStarted = performance.now(), diagnosticFrames = 0;
    const diagnostics = { active: true, frames: 0, fps: 0, lastFrameMs: 0, maxFrameMs: 0,
        videoReadyState: elements.video.readyState, videoStream: 'webgl-texture', controlsStream: 'three-exit-button',
        follow: null, lastError: null };
    window.ediVrDiagnostics = diagnostics;

    function captureRelative() {
        if (!head.valid) return;
        const inverse = head.rotation.clone().invert();
        relativePosition.copy(video.group.position).sub(head.position).applyQuaternion(inverse);
        gate.reset(); catchingUp = false; outsideSince = null;
    }
    function recenterVideo() {
        relativePosition.set(preferences.offsetX, preferences.offsetY, -preferences.distance);
        video.group.position.copy(relativePosition).applyQuaternion(head.rotation).add(head.position);
        video.group.quaternion.copy(head.rotation);
        gate.reset(); catchingUp = false; outsideSince = null;
    }
    function faceViewer() {
        if (!head.valid || video.group.position.distanceToSquared(head.position) < .0001) return;
        // A flat screen has no useful free rotation in VR: keep its local +Z
        // normal aimed at the viewer and its vertical axis upright.
        faceMatrix.lookAt(head.position, video.group.position, camera.up);
        video.group.quaternion.setFromRotationMatrix(faceMatrix);
    }
    function syncSurfaces() {
        uiRig.position.copy(video.group.position);
        uiRig.quaternion.copy(video.group.quaternion);
        mediaLayer?.update(video.group.position, video.group.quaternion);
    }
    function rebuildVideo() {
        video.rebuild();
        exitControl.layout();
        video.useNativeLayer(mediaLayer?.rebuild());
        diagnostics.videoStream = mediaLayer?.active() ? 'native-media-layer' : 'webgl-texture';
    }
    function gazeInContour() {
        video.group.updateMatrixWorld(true);
        gazeOrigin.copy(head.position); video.group.worldToLocal(gazeOrigin);
        inverseVideo.copy(video.group.matrixWorld).invert();
        gazeDirection.set(0, 0, -1).applyQuaternion(head.rotation).transformDirection(inverseVideo);
        if (Math.abs(gazeDirection.z) < .0001) return false;
        const distance = -gazeOrigin.z / gazeDirection.z;
        if (distance <= 0) return false;
        gazePoint.copy(gazeDirection).multiplyScalar(distance).add(gazeOrigin);
        const dimensions = video.dimensions();
        return insideFollowContour(gazePoint, dimensions.width, dimensions.height);
    }
    function intersection(controller) {
        scene.updateMatrixWorld(true);
        raycaster.ray.origin.setFromMatrixPosition(controller.object.matrixWorld);
        raycaster.ray.direction.set(0, 0, -1).transformDirection(controller.object.matrixWorld);
        raycaster.layers.set(0);
        const ui = raycaster.intersectObject(exitControl.hit, false)[0];
        if (ui) return { type: 'exit', distance: ui.distance, point: ui.point };
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
        gate.reset(); catchingUp = false; outsideSince = null;
        return true;
    }
    function moveGrab() {
        if (!grab) return;
        dragMatrix.multiplyMatrices(grab.space.matrixWorld, grab.offset);
        dragMatrix.decompose(dragPosition, dragRotation, dragScale);
        if (dragPosition.distanceTo(grab.position) > .025 || dragRotation.angleTo(grab.rotation) > .03) grab.moved = true;
        if (grab.moved) {
            const local = dragPosition.clone().sub(head.position).applyQuaternion(placementRotation.copy(head.rotation).invert());
            const constrained = constrainPlacement(local);
            video.group.position.set(constrained.x, constrained.y, constrained.z)
                .applyQuaternion(head.rotation).add(head.position);
            faceViewer();
        }
    }
    function finishGrab(controller, kind) {
        if (grab?.controller !== controller || grab.kind !== kind) return;
        scene.updateMatrixWorld(true); moveGrab();
        const moved = grab.moved || grab.adjusted;
        grab = null;
        captureRelative();
        if (moved) {
            preferences.offsetX = clamp(relativePosition.x, -3, 3);
            preferences.offsetY = clamp(relativePosition.y, -3, 3);
            preferences.distance = clamp(-relativePosition.z, .45, 8);
            onPlacement();
        }
    }
    function gripStart(controller) {
        if (disposed || !head.valid) return;
        if (videoIntersection()) beginGrab(controller, 'grip');
    }
    function cancel(controller) {
        if (grab?.controller === controller) { grab = null; captureRelative(); }
        controller.press = null;
    }
    function start(controller) {
        if (disposed || !head.valid) return;
        const target = intersection(controller);
        if (target?.type === 'exit') controller.press = { type: 'exit' };
        else if (target?.type === 'video' && !grab) {
            beginGrab(controller, 'trigger');
            controller.press = { type: 'video' };
        } else controller.press = { type: 'empty' };
    }
    function finish(controller) {
        if (disposed) return;
        const target = intersection(controller);
        if (controller.press?.type === 'exit' && target?.type === 'exit') void exit();
        else if (controller.press?.type === 'video') finishGrab(controller, 'trigger');
        controller.press = null;
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
            stickAccumulator += intensityStickVelocity(action.value) * dt;
            if (now - lastStickTime >= .08 && Math.abs(stickAccumulator) >= 1) {
                const step = Math.trunc(stickAccumulator);
                const value = clamp(state.currentIntensity + step, 0, 100);
                stickAccumulator -= step; lastStickTime = now;
                if (value !== state.currentIntensity) void setIntensity(value);
            }
        }
    }
    function applyGripStick(controller, dt, now) {
        if (grab?.controller !== controller) return;
        const action = gripStickAction(controller.source.gamepad?.axes);
        if ((!action.scale && !action.curve) || now - lastSurfaceUpdate < 1 / 20) return;
        const elapsed = lastSurfaceUpdate ? Math.min(.1, now - lastSurfaceUpdate) : dt;
        const width = clamp(preferences.width + action.scale * elapsed * 1.5, .4, 6);
        const curvature = clamp(preferences.curvature + action.curve * elapsed * .9, 0, 1.6);
        if (width === preferences.width && curvature === preferences.curvature) return;
        preferences.width = width; preferences.curvature = curvature;
        grab.adjusted = true;
        lastSurfaceUpdate = now;
        rebuildVideo();
    }
    function follow(now, dt) {
        const still = gate.ready(now, { position: head.position.toArray(), rotation: head.rotation.toArray() }, .18);
        const inside = gazeInContour();
        diagnostics.follow = { enabled: preferences.follow, inside, still, catchingUp, now, delay: preferences.followDelay };
        if (!preferences.follow || grab) { catchingUp = false; outsideSince = null; return; }
        // The last grip placement is the contract: enforcement follows the
        // current head while preserving that exact relative x/y/z offset.
        desiredPosition.copy(relativePosition).applyQuaternion(head.rotation).add(head.position);
        const distance = video.group.position.distanceTo(desiredPosition);
        diagnostics.follow.distance = distance;
        if (!catchingUp) {
            outsideSince = inside ? null : outsideSince ?? now;
            if (outsideSince !== null && now - outsideSince >= preferences.followDelay) {
                catchingUp = true; followStarted = now; followJourney = Math.max(distance, .1);
            }
        }
        if (!catchingUp) return;
        followJourney = Math.max(followJourney, distance);
        const step = enforcementStep(distance, followJourney, now - followStarted, dt, preferences.followEase);
        if (distance > 0) video.group.position.addScaledVector(followDelta.copy(desiredPosition).sub(video.group.position), step / distance);
        // Head movement never cancels enforcement. It only prevents completion;
        // the target is recomputed every frame until both screen and head settle.
        if (distance < .005 && still) { catchingUp = false; outsideSince = null; captureRelative(); }
    }
    function animate(milliseconds, frame) {
        if (disposed || !frame) return;
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
        faceViewer();
        syncSurfaces();
        let exitHovered = false, activeStick = null;
        for (const controller of controllers) {
            if (!controller.source || !controller.object.visible) continue;
            controller.object.getWorldPosition(controller.posePosition);
            controller.object.getWorldQuaternion(controller.poseRotation);
            if (!controller.poseReady || controller.posePosition.distanceTo(controller.previousPosition) > .004
                || controller.poseRotation.angleTo(controller.previousRotation) > .01) {
                controller.poseReady = true;
                controller.previousPosition.copy(controller.posePosition);
                controller.previousRotation.copy(controller.poseRotation);
                controller.lastActivity = now;
            }
            const target = intersection(controller);
            controller.line.scale.z = target ? Math.min(20, target.distance) : 4;
            if (target) {
                // Use the actual raycast collision, rather than assuming the
                // panel is perpendicular to the controller ray.
                controller.hitPoint.copy(target.point);
                controller.object.worldToLocal(controller.hitPoint);
                controller.dot.position.copy(controller.hitPoint);
                controller.dot.material.color.set(target.type === 'exit' ? 0x2f81f7 : 0xffffff);
            }
            const active = Boolean(controller.source.gamepad && target);
            const opacity = controller.press || grab?.controller === controller ? 1
                : active ? 1 - clamp((now - controller.lastActivity - .65) / .45, 0, 1) : 0;
            controller.line.material.opacity = controller.dot.material.opacity = opacity;
            controller.line.visible = opacity > .01;
            controller.dot.visible = Boolean(target) && opacity > .01;
            if (target?.type === 'exit') exitHovered = true;
            const buttons = controllerButtons(controller.source, controller.previousButtons);
            controller.previousButtons = buttons.buttons;
            if (buttons.primary) onPrimary();
            if (buttons.variant) onVariant();
            if (controller.source.gamepad?.mapping === 'xr-standard') {
                if (buttons.stick && grab?.controller === controller) {
                    grab = null; recenterVideo(); onPlacement();
                }
                if (grab?.controller === controller) {
                    applyGripStick(controller, dt, now);
                    controller.variantStick = 0;
                } else {
                    const sideways = horizontalStick(controller.source.gamepad.axes, 'xr-standard');
                    if (sideways && !controller.variantStick) onVariant();
                    controller.variantStick = sideways;
                    if (!sideways) {
                        const action = stickAction(controller.source.gamepad.axes);
                        if (action.value && (!activeStick || controller.source.handedness === 'right')) activeStick = action;
                    }
                }
            }
        }
        exitControl.hover(exitHovered);
        applyStick(activeStick || { value: 0 }, dt, now);
        if (grab && !grab.space.visible) cancel(grab.controller);
        moveGrab();
        syncSurfaces();
        renderer.render(scene, camera);
    }
    function animationLoop(milliseconds, frame) {
        const started = performance.now();
        try {
            animate(milliseconds, frame);
            const elapsed = performance.now() - started;
            diagnostics.frames++;
            diagnostics.lastFrameMs = elapsed;
            diagnostics.maxFrameMs = Math.max(diagnostics.maxFrameMs, elapsed);
            diagnostics.videoReadyState = elements.video.readyState;
            diagnosticFrames++;
            const diagnosticElapsed = performance.now() - diagnosticStarted;
            if (diagnosticElapsed >= 1000) {
                diagnostics.fps = diagnosticFrames * 1000 / diagnosticElapsed;
                diagnosticFrames = 0; diagnosticStarted = performance.now();
            }
        } catch (error) {
            diagnostics.lastError = { message: error.message, stack: error.stack, at: new Date().toISOString() };
            console.error('VR frame failed.', error);
            if (!frameErrorReported) { frameErrorReported = true; onError(error); }
        }
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
        diagnostics.active = false;
        renderer.setAnimationLoop(null);
        session.removeEventListener('end', dispose);
        session.removeEventListener('visibilitychange', visibilityChanged);
        controllers.forEach(controller => {
            cancel(controller);
            for (const [type, listener] of controller.listeners) controller.object.removeEventListener(type, listener);
            controller.line.geometry.dispose(); controller.line.material.dispose();
            controller.dot.geometry.dispose(); controller.dot.material.dispose();
        });
        mediaLayer?.remove(); exitControl.dispose(); video.dispose(); renderer.dispose();
        renderer.domElement.remove();
    }
    session.addEventListener('end', dispose, { once: true });
    session.addEventListener('visibilitychange', visibilityChanged);
    try {
        for (let index = 0; index < 2; index++) {
            const object = renderer.xr.getController(index);
            const grip = renderer.xr.getControllerGrip(index); scene.add(grip);
            const line = new THREE.Line(new THREE.BufferGeometry().setFromPoints([
                new THREE.Vector3(), new THREE.Vector3(0, 0, -1)]), new THREE.LineBasicMaterial({ color: '#58a6ff', depthTest: false, transparent: true }));
            const dot = new THREE.Mesh(new THREE.SphereGeometry(.012, 10, 8),
                new THREE.MeshBasicMaterial({ color: '#ffffff', depthTest: false, transparent: true }));
            line.renderOrder = dot.renderOrder = 30;
            object.add(line, dot); scene.add(object);
            line.visible = dot.visible = false;
            const controller = { object, grip, line, dot, hitPoint: new THREE.Vector3(), source: null, press: null,
                previousButtons: [], variantStick: 0, listeners: [], posePosition: new THREE.Vector3(), previousPosition: new THREE.Vector3(),
                poseRotation: new THREE.Quaternion(), previousRotation: new THREE.Quaternion(), poseReady: false, lastActivity: 0 };
            const listen = (type, listener) => { object.addEventListener(type, listener); controller.listeners.push([type, listener]); };
            listen('connected', event => {
                controller.source = event.data; controller.previousButtons = []; controller.poseReady = false;
            });
            listen('disconnected', () => {
                cancel(controller); controller.source = null; controller.poseReady = false;
                controller.line.visible = controller.dot.visible = false;
            });
            listen('selectstart', () => start(controller));
            listen('selectend', () => finish(controller));
            listen('squeezestart', () => gripStart(controller));
            listen('squeezeend', () => finishGrab(controller, 'grip'));
            controllers.push(controller);
        }
        await renderer.xr.setSession(session);
        renderer.xr.setFoveation(1);
        if (disposed) return { dispose };
        mediaLayer = createMediaLayer({ session, referenceSpace: renderer.xr.getReferenceSpace(),
            projectionLayer: renderer.xr.getBaseLayer(), video: elements.video, settings: preferences });
        video.useNativeLayer(mediaLayer.rebuild());
        diagnostics.videoStream = mediaLayer.active() ? 'native-media-layer' : 'webgl-texture';
        if (disposed) return { dispose };
        renderer.setAnimationLoop(animationLoop);
        return { dispose, recenterVideo, rebuildVideo,
            settingsChanged(key) {
                if (key === 'follow') captureRelative();
                else if (['width', 'curvature', 'stereo', 'swapEyes', 'halfResolution'].includes(key)) rebuildVideo();
                if (['distance', 'offsetX', 'offsetY'].includes(key)) recenterVideo();
                gate.reset();
            }
        };
    } catch (error) { dispose(); throw error; }
}
