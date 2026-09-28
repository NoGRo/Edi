// VR is deliberately a movable flat screen. Filename conventions only select
// mono or side-by-side eye packing; 180/360 markers do not change geometry.
export function detectVideoFormat(name = '') {
    const stem = name.replace(/^.*[\\/]/, '').replace(/\.[^.]+$/, '').toLowerCase();
    const tokens = stem.replace(/(180|360)(lr|rl|sbs)/g, '$1 $2').split(/[^a-z0-9]+/);
    const has = (...values) => values.some(value => tokens.includes(value));
    const stereo = has('lr', 'rl', 'sbs', 'hsbs', 'halfsbs', '3dh', '3dph', '3dsbs', '3d', 'sidebyside') ? 'sbs' : 'mono';
    return { stereo, swapEyes: has('rl'), halfResolution: has('hsbs', 'halfsbs') };
}

export function eyeUv(u, v, eye, { stereo, swapEyes }) {
    // Sphere pole UVs can extend slightly beyond the seam. Clamp BEFORE eye
    // packing so filtering never addresses the neighbouring eye's image.
    u = Math.max(0, Math.min(1, u)); v = Math.max(0, Math.min(1, v));
    const side = swapEyes ? 1 - eye : eye;
    if (stereo === 'sbs') return [(u + side) / 2, v];
    return [u, v];
}

export function eyeAspect(width, height, { stereo, halfResolution }) {
    if (!(width > 0 && height > 0)) return 16 / 9;
    if (halfResolution && stereo !== 'mono') return width / height;
    return (width / (stereo === 'sbs' ? 2 : 1)) / height;
}
