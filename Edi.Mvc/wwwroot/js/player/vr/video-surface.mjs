import * as THREE from '../../../lib/vr/three.module.min.js';
import { eyeAspect, eyeUv } from './format.mjs';

export function videoGeometry(video, settings, eye) {
    const aspect = eyeAspect(video.videoWidth, video.videoHeight, settings);
    const curvature = Math.max(0, Math.min(1.6, Number(settings.curvature) || 0));
    const geometry = new THREE.PlaneGeometry(settings.width, settings.width / aspect, curvature ? 32 : 1, 1);
    if (curvature) {
        const position = geometry.attributes.position;
        const radius = settings.width / curvature;
        for (let index = 0; index < position.count; index++) {
            const angle = position.getX(index) / radius;
            position.setX(index, Math.sin(angle) * radius);
            position.setZ(index, radius * (Math.cos(angle) - 1));
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
    rebuild();
    return { group, rebuild, meshes: () => surface.children, dispose: clear };
}
