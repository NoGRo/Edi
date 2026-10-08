import { eyeAspect } from './format.mjs';

// Use the compositor's media path when WebXR Layers is available. The decoder
// can then feed the headset without first copying every frame through a WebGL
// VideoTexture. The WebGL projection layer remains a separate transparent
// stream for the live HTML controls, pointers and interaction proxy.
export function createMediaLayer({ session, referenceSpace, projectionLayer, video, settings }) {
    let layer = null;
    const supported = typeof XRMediaBinding !== 'undefined'
        && typeof XRRigidTransform !== 'undefined'
        && Array.isArray(session.renderState.layers)
        && projectionLayer;

    function remove() {
        if (!layer) return;
        layer = null;
        try { session.updateRenderState({ layers: [projectionLayer] }); } catch { /* The session may already have ended. */ }
    }
    function rebuild() {
        remove();
        // Native quad layers cannot reproduce the custom curved geometry or
        // reverse stereo eyes. Those formats retain the proven texture path.
        if (!supported || settings.curvature > 0 || settings.swapEyes) return false;
        const width = Number(settings.width) || 2.4;
        try {
            const binding = new XRMediaBinding(session);
            layer = binding.createQuadLayer(video, {
                space: referenceSpace,
                layout: settings.stereo === 'sbs' ? 'stereo-left-right' : 'mono',
                width,
                height: width / eyeAspect(video.videoWidth, video.videoHeight, settings),
                transform: new XRRigidTransform()
            });
            session.updateRenderState({ layers: [layer, projectionLayer] });
            return true;
        } catch {
            layer = null;
            try { session.updateRenderState({ layers: [projectionLayer] }); } catch { /* Keep the texture fallback. */ }
            return false;
        }
    }
    function update(position, orientation) {
        if (!layer) return;
        layer.transform = new XRRigidTransform(
            { x: position.x, y: position.y, z: position.z },
            { x: orientation.x, y: orientation.y, z: orientation.z, w: orientation.w });
    }
    return { rebuild, update, remove, active: () => Boolean(layer) };
}
