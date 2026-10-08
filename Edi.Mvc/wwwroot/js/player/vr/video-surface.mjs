import * as THREE from '../../../lib/vr/three.module.min.js';
import { eyeAspect, eyeUv } from './format.mjs';

export function videoGeometry(video, settings, eye) {
    const aspect = eyeAspect(video.videoWidth, video.videoHeight, settings);
    const curvature = Math.max(0, Math.min(1.6, Number(settings.curvature) || 0));
    const geometry = new THREE.PlaneGeometry(settings.width, settings.width / aspect, curvature ? 32 : 1, 1);
    if (curvature) {
        const position = geometry.attributes.position;
        // Keep the requested visible width as the chord instead of treating it
        // as arc length; otherwise the screen appears to shrink while curving.
        const radius = settings.width / (2 * Math.sin(curvature / 2));
        for (let index = 0; index < position.count; index++) {
            const angle = position.getX(index) / settings.width * curvature;
            position.setX(index, Math.sin(angle) * radius);
            // Positive local Z is toward the viewer, so the edges wrap forward.
            position.setZ(index, radius * (1 - Math.cos(angle)));
        }
        position.needsUpdate = true;
        geometry.computeVertexNormals();
    }
    const uv = geometry.attributes.uv;
    const side = settings.swapEyes ? 1 - eye : eye;
    const insetU = video.videoWidth > 0 ? .5 / video.videoWidth : 0;
    const insetV = video.videoHeight > 0 ? .5 / video.videoHeight : 0;
    const minU = settings.stereo === 'sbs' ? side / 2 : 0;
    const maxU = settings.stereo === 'sbs' ? (side + 1) / 2 : 1;
    for (let index = 0; index < uv.count; index++) {
        const [u, v] = eyeUv(uv.getX(index), uv.getY(index), eye, settings);
        uv.setXY(index, Math.max(minU + insetU, Math.min(maxU - insetU, u)),
            Math.max(insetV, Math.min(1 - insetV, v)));
    }
    return geometry;
}

export function createVideoSurface(video, settings) {
    const group = new THREE.Group();
    const surface = new THREE.Group();
    group.add(surface);
    let texture, material;
    function clear() {
        surface.children.forEach(mesh => mesh.geometry.dispose());
        surface.clear(); material?.dispose(); texture?.dispose();
    }
    function rebuild() {
        clear();
        // Native media and its events remain the single playback/sync owner.
        texture = new THREE.VideoTexture(video);
        texture.colorSpace = THREE.SRGBColorSpace;
        material = new THREE.MeshBasicMaterial({ map: texture, side: THREE.DoubleSide, toneMapped: false });
        for (let eye = 0; eye < 2; eye++) {
            const mesh = new THREE.Mesh(videoGeometry(video, settings, eye), material);
            mesh.layers.set(eye + 1); // Three's WebXR left/right camera layers.
            surface.add(mesh);
        }
    }
    function dimensions() {
        const width = Number(settings.width) || 2.4;
        return { width, height: width / eyeAspect(video.videoWidth, video.videoHeight, settings) };
    }
    function useNativeLayer(enabled) {
        if (material) material.visible = !enabled;
    }
    rebuild();
    return { group, rebuild, dimensions, useNativeLayer, meshes: () => surface.children, dispose: clear };
}
