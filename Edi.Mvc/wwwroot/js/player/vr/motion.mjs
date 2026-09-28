export const clamp = (value, min, max) => Math.max(min, Math.min(max, value));
export const easingAlpha = (seconds, ease) => 1 - Math.exp(-Math.max(0, seconds) / Math.max(.05, ease));
const distance = (a, b) => Math.hypot(...a.map((value, index) => value - b[index]));
export const rotationDistance = (a, b) => 2 * Math.acos(clamp(Math.abs(a.reduce((sum, value, index) => sum + value * b[index], 0)), 0, 1));

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

export function controllerButtons(source, previous = []) {
    const gamepad = source?.gamepad;
    const known = gamepad?.mapping === 'xr-standard';
    const pressed = index => Boolean(known && gamepad.buttons[index]?.pressed);
    // A/B (right), X/Y (left), then the stick button on Touch controllers.
    const buttons = [pressed(4), pressed(5), pressed(3)];
    return { buttons, primary: buttons[0] && !previous[0], variant: buttons[1] && !previous[1],
        menu: buttons[2] && !previous[2] };
}
