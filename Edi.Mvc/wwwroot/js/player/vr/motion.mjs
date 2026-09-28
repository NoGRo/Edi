export const clamp = (value, min, max) => Math.max(min, Math.min(max, value));
export const easingAlpha = (seconds, ease) => 1 - Math.exp(-Math.max(0, seconds) / Math.max(.05, ease));
const distance = (a, b) => Math.hypot(...a.map((value, index) => value - b[index]));
export const rotationDistance = (a, b) => 2 * Math.acos(clamp(Math.abs(a.reduce((sum, value, index) => sum + value * b[index], 0)), 0, 1));

// Keep the screen in the useful forward viewing cone. The limits are relative
// to distance, so a nearby screen cannot be dragged out beside the viewer while
// a distant screen still has enough room for comfortable placement.
export function constrainPlacement(position) {
    const distance = clamp(-position.z, .45, 8);
    return {
        x: clamp(position.x, -distance * .85, distance * .85),
        y: clamp(position.y, -distance * .65, distance * .65),
        z: -distance
    };
}

// Compare with a stable sample so small movements accumulate instead of silently
// resetting the stillness timer on every frame. Time is supplied by the XR loop.
export function createStillnessGate() {
    let sample = null, since = 0;
    return {
        reset() { sample = null; },
        ready(now, pose, delay) {
            if (!sample || distance(sample.position, pose.position) > .02
                || rotationDistance(sample.rotation, pose.rotation) > Math.PI / 120) {
                sample = { position: [...pose.position], rotation: [...pose.rotation] };
                since = now;
                return false;
            }
            return now - since >= delay;
        }
    };
}

export function stickAction(axes, target = 'intensity') {
    const x = axes?.[2] || 0, y = -(axes?.[3] || 0);
    // Seeking requires explicitly pointing at the video control surface.
    const value = target === 'seek' && Math.abs(x) > Math.abs(y) ? x : y;
    const deadzone = .22;
    return { kind: target, value: Math.abs(value) <= deadzone ? 0
        : Math.sign(value) * (Math.abs(value) - deadzone) / (1 - deadzone) };
}

// Quest thumbsticks reach their full reported range with little physical travel.
// A power curve keeps the center precise while preserving deliberate full-range changes.
export function intensityStickVelocity(value) {
    return Math.sign(value) * Math.pow(Math.abs(value), 1.7) * 18;
}

export function gripStickAction(axes) {
    const x = axes?.[2] || 0, y = -(axes?.[3] || 0), deadzone = .22;
    const normalize = value => Math.abs(value) <= deadzone ? 0
        : Math.sign(value) * (Math.abs(value) - deadzone) / (1 - deadzone);
    return { curve: normalize(x), scale: normalize(y) };
}

export function horizontalStick(axes, mapping = '') {
    if (!axes?.length) return 0;
    const candidates = mapping === 'xr-standard' ? [axes[2] || 0] : [axes[0] || 0, axes[2] || 0];
    const value = candidates.reduce((strongest, candidate) => Math.abs(candidate) > Math.abs(strongest) ? candidate : strongest, 0);
    return Math.abs(value) >= .65 ? Math.sign(value) : 0;
}

export function controllerButtons(source, previous = []) {
    const gamepad = source?.gamepad;
    const known = gamepad?.mapping === 'xr-standard';
    const pressed = index => Boolean(known && gamepad.buttons[index]?.pressed);
    const buttons = [pressed(4), pressed(5), pressed(3)]; // A/B + thumbstick on Touch.
    return { buttons, primary: buttons[0] && !previous[0], variant: buttons[1] && !previous[1],
        stick: buttons[2] && !previous[2] };
}
