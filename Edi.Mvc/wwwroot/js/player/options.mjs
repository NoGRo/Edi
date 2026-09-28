import { routedPlaybackCommand } from './device-routing.mjs';
import { playbackOptionsKey } from './preferences.mjs';
import { report } from './edi-api.mjs';

export function createOptions({ state, elements, showIntensityOverlay, saveCurrentPosition, resetPositions, toggleStrokerPlayback }) {
    const loopModeIndicator = elements.loopModeIndicator;

    const optionButtons = elements.optionButtons;

    async function setIntensity(value) {
        const clamped = Math.max(0, Math.min(100, Math.round(value)));
        state.currentIntensity = clamped;
        localStorage.setItem('edi-player-intensity', String(clamped));
        renderPlaybackOptions();
        showIntensityOverlay(clamped);
        try {
            await routedPlaybackCommand(`/Edi/Intensity/${clamped}`);
            state.intensityNeedsResync = false;
        } catch (error) {
            state.intensityNeedsResync = true;
            report(`Could not set intensity: ${error.message}`, true);
        }
    }

    function renderPlaybackOptions() {
        Object.entries(optionButtons).forEach(([name, button]) => {
            const enabled = name === 'loop' ? state.playbackOptions.loopMode !== 'none' : state.playbackOptions[name] === true;
            const paused = name === 'stroker' && state.strokerPaused;
            const intensityAtZero = name === 'intensity' && state.currentIntensity === 0;
            const zeroIntensity = intensityAtZero && state.intensityEnabled;
            button.setAttribute('aria-pressed', String(enabled));
            button.classList.toggle('btn-primary', enabled && !paused && !zeroIntensity);
            button.classList.toggle('btn-danger', paused || zeroIntensity);
            button.classList.remove('btn-outline-danger');
            button.classList.toggle('btn-outline-secondary', !enabled && !paused && !zeroIntensity);
            button.classList.toggle('intensity-zero-disabled', intensityAtZero && !state.intensityEnabled);
            button.classList.toggle('stroker-paused', paused);
            if (name === 'loop') {
                const mode = state.playbackOptions.loopMode;
                const label = mode === 'video' ? 'Repeat video' : mode === 'playlist' ? 'Repeat playlist' : 'Repeat off';
                button.dataset.tooltip = label;
                button.removeAttribute('title');
                button.setAttribute('aria-label', label);
                loopModeIndicator.textContent = mode === 'video' ? '1' : mode === 'playlist' ? '≡' : '';
            }
            if (name === 'intensity') {
                // intensity button reflects enabled flag
                const ie = state.intensityEnabled === true;
                button.setAttribute('aria-pressed', String(ie));
                button.classList.toggle('btn-primary', ie && !zeroIntensity);
                button.classList.toggle('btn-danger', zeroIntensity);
                button.classList.remove('btn-outline-danger');
                button.classList.toggle('btn-outline-secondary', !ie && !zeroIntensity);
            }
            if (name === 'stroker') {
                const label = paused
                    ? 'Click or Space: resume devices'
                    : enabled
                        ? 'Click or Space: pause devices'
                        : 'Click or Space: pause playback';
                button.dataset.tooltip = label;
                button.removeAttribute('title');
                button.setAttribute('aria-label', label);
            }
        });
    }

    function togglePlaybackOption(name) {
        if (name === 'loop') {
            state.playbackOptions.loopMode = state.playbackOptions.loopMode === 'none'
                ? 'video'
                : state.playbackOptions.loopMode === 'video' ? 'playlist' : 'none';
            state.playbackOptions.loop = state.playbackOptions.loopMode === 'video';
            localStorage.setItem(playbackOptionsKey, JSON.stringify(state.playbackOptions));
            renderPlaybackOptions();
            return;
        }
        state.playbackOptions[name] = state.playbackOptions[name] !== true;
        localStorage.setItem(playbackOptionsKey, JSON.stringify(state.playbackOptions));
        if (name === 'resume') {
            if (state.playbackOptions.resume) saveCurrentPosition();
            else {
                resetPositions();
            }
        }
        if (name === 'intensity') {
            state.intensityEnabled = !state.intensityEnabled;
        }
        if (name === 'stroker' && !state.playbackOptions.stroker && state.strokerPaused) {
            toggleStrokerPlayback();
        }
        renderPlaybackOptions();
    }

    function mount() {

    }
    return { setIntensity, renderPlaybackOptions, togglePlaybackOption, mount };
}

