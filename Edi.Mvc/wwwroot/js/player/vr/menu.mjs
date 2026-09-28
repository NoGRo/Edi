export function createVrMenu({ elements, preferences, onChange, onRecenter, onExit }) {
    const root = document.createElement('section');
    root.id = 'vrSettings';
    root.className = 'vr-settings';
    root.hidden = true;
    root.innerHTML = `
        <div class="workspace-panel-header"><strong>VR screen</strong>
            <button type="button" class="btn btn-sm btn-outline-secondary" data-vr-close>Close</button></div>
        <p id="vrStatus" class="small text-muted" role="status"></p>
        <div class="vr-settings-grid">
            <label>Eye packing<select data-vr-setting="stereo" class="form-select">
                <option value="mono">Mono</option><option value="sbs">Side by side (LR / SBS)</option></select></label>
            <label class="vr-check"><input type="checkbox" data-vr-setting="swapEyes"> Swap eyes (RL)</label>
            <label class="vr-check"><input type="checkbox" data-vr-setting="halfResolution"> Half size packing (HSBS)</label>
            <span class="small text-muted">Restores the screen aspect of compressed stereo frames.</span>
            <label>Width <output data-vr-value="width"></output><input type="range" min="0.4" max="6" step="0.05" data-vr-setting="width"></label>
            <label>Horizontal curve <output data-vr-value="curvature"></output><input type="range" min="0" max="1.6" step="0.05" data-vr-setting="curvature"></label>
            <label>Distance <output data-vr-value="distance"></output><input type="range" min="0.45" max="8" step="0.05" data-vr-setting="distance"></label>
            <label>Horizontal offset <output data-vr-value="offsetX"></output><input type="range" min="-3" max="3" step="0.05" data-vr-setting="offsetX"></label>
            <label>Vertical offset <output data-vr-value="offsetY"></output><input type="range" min="-3" max="3" step="0.05" data-vr-setting="offsetY"></label>
            <label class="vr-check"><input type="checkbox" data-vr-setting="follow"> Delayed head follow</label>
            <span class="small text-muted">Keeps your chosen view position after you stop moving.</span>
            <label>Wait <output data-vr-value="followDelay"></output><input type="range" min="0.5" max="5" step="0.1" data-vr-setting="followDelay"></label>
            <label>Smoothing <output data-vr-value="followEase"></output><input type="range" min="0.15" max="2" step="0.05" data-vr-setting="followEase"></label>
        </div>
        <p class="small text-muted vr-help">Grip: point at the video and hold to move it; the screen stays facing you.<br>
            Trigger: click a control; hold the video to move it; tap the video or empty space to show/hide every panel.<br>
            The blue dot is the exact HTML click point.<br>
            A/X: player mouse action · B/Y or stick sideways: variant · Stick vertical: intensity.<br>
            Grip + stick: vertical changes size, horizontal changes curve; click the stick to recenter.</p>
        <div class="d-flex gap-2"><button type="button" class="btn btn-primary" data-vr-recenter>Recenter video</button>
            <button type="button" class="btn btn-outline-secondary" data-vr-auto>Detect filename</button>
            <button type="button" class="btn btn-outline-danger" data-vr-exit>Exit VR</button></div>`;
    elements.videoStage.append(root);
    const fields = [...root.querySelectorAll('[data-vr-setting]')];
    const formatFields = new Set(['stereo', 'swapEyes', 'halfResolution']);
    const units = { width: ' m', curvature: ' rad', distance: ' m', offsetX: ' m', offsetY: ' m', followDelay: ' s', followEase: ' s' };
    const status = root.querySelector('#vrStatus');
    function render() {
        for (const field of fields) {
            const key = field.dataset.vrSetting, value = preferences[key];
            if (field.type === 'checkbox') field.checked = value;
            else field.value = value;
            const output = root.querySelector(`[data-vr-value="${key}"]`);
            if (output) output.textContent = `${Number(value).toFixed(['width','distance','offsetX','offsetY'].includes(key) ? 2 : 1)}${units[key] || ''}`;
        }
        elements.vrFollowToggle.setAttribute('aria-pressed', String(preferences.follow));
        elements.vrFollowToggle.classList.toggle('btn-primary', preferences.follow);
        elements.vrFollowToggle.classList.toggle('btn-outline-secondary', !preferences.follow);
        elements.vrFollowToggle.setAttribute('aria-label', preferences.follow ? 'Unlock screen from gaze' : 'Lock screen to gaze');
        elements.vrFollowToggle.setAttribute('title', preferences.follow ? 'Unlock screen from gaze' : 'Lock screen to gaze');
    }
    for (const field of fields) field.addEventListener('input', () => {
        const key = field.dataset.vrSetting;
        preferences[key] = field.type === 'checkbox' ? field.checked
            : field.type === 'range' ? Number(field.value) : field.value;
        render();
        onChange(key, formatFields.has(key));
    });
    function setOpen(open) {
        root.hidden = !open;
        elements.vrSettingsToggle.setAttribute('aria-expanded', String(open));
    }
    elements.vrSettingsToggle.addEventListener('click', () => setOpen(root.hidden));
    elements.vrFollowToggle.addEventListener('click', () => {
        preferences.follow = !preferences.follow;
        render(); onChange('follow', false);
    });
    root.querySelector('[data-vr-close]').addEventListener('click', () => setOpen(false));
    root.querySelector('[data-vr-recenter]').addEventListener('click', onRecenter);
    root.querySelector('[data-vr-auto]').addEventListener('click', () => onChange('auto', true));
    root.querySelector('[data-vr-exit]').addEventListener('click', onExit);
    render();
    return { root, render, setOpen, setStatus(message) { status.textContent = message; } };
}
