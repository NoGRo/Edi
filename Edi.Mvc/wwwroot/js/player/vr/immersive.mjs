import { detectVideoFormat } from './format.mjs';
import { createVrMenu } from './menu.mjs';
import { report } from '../edi-api.mjs';

const preferenceKey = 'edi-player-vr';
const defaults = { width: 2.4, distance: 2, offsetX: 0, offsetY: 0,
    curvature: 0, follow: false, followDelay: 2, followEase: .6 };

export function createImmersive({ state, elements, currentItem, setIntensity }) {
    let stored;
    try { stored = JSON.parse(localStorage.getItem(preferenceKey) || '{}'); } catch { stored = {}; }
    const preferences = { ...defaults, ...stored?.view, ...detectVideoFormat() };
    const formats = stored?.formats && typeof stored.formats === 'object' && !Array.isArray(stored.formats) ? { ...stored.formats } : {};
    // Validate saved numbers before they reach geometry or motion calculations.
    for (const [key, min, max] of [['width', .4, 6], ['distance', .45, 8], ['offsetX', -3, 3],
        ['curvature', 0, 1.6],
        ['offsetY', -3, 3], ['followDelay', .5, 5], ['followEase', .15, 2]]) {
        preferences[key] = Number.isFinite(Number(preferences[key]))
            ? Math.max(min, Math.min(max, Number(preferences[key]))) : defaults[key];
    }
    preferences.follow = preferences.follow === true;
    let runtime = null, starting = false, session = null, activeName = null;
    let menu;
    const formatKeys = ['stereo', 'swapEyes', 'halfResolution'];
    function persist() {
        try { localStorage.setItem(preferenceKey, JSON.stringify({
            view: Object.fromEntries(Object.keys(defaults).map(key => [key, preferences[key]])), formats
        })); } catch { /* Storage may be unavailable in private browsing. */ }
    }
    function loadFormat(force = false) {
        const name = currentItem()?.name || '';
        if (!force && name === activeName) return;
        activeName = name;
        const detected = detectVideoFormat(name);
        const saved = formats[name.toLowerCase()];
        Object.assign(preferences, detected);
        if (saved && ['mono', 'sbs'].includes(saved.stereo)) {
            Object.assign(preferences, Object.fromEntries(formatKeys.map(key => [key, saved[key] ?? detected[key]])));
            preferences.swapEyes = preferences.swapEyes === true;
            preferences.halfResolution = preferences.halfResolution === true;
        }
        menu?.render();
        runtime?.rebuildVideo();
        runtime?.recenterVideo();
    }
    async function exit() {
        if (session) {
            try { await session.end(); } catch (error) { report(`Could not exit VR: ${error.message}`, true); }
        }
    }
    function cleanup() {
        runtime?.dispose(); runtime = null; session = null;
        elements.enterVr.setAttribute('aria-pressed', 'false');
        elements.enterVr.disabled = false;
        menu.setStatus('VR ended. Your video and player settings are retained.');
    }
    async function enter() {
        if (session) { await exit(); return; }
        if (starting) return;
        if (!window.isSecureContext || !navigator.xr) {
            menu.setOpen(true);
            menu.setStatus('VR needs WebXR and a trusted HTTPS address in the headset browser.');
            return;
        }
        starting = true;
        elements.enterVr.disabled = true;
        try {
            // Request directly from the button event, before imports or fullscreen
            // exit can consume the browser's transient user activation.
            session = await navigator.xr.requestSession('immersive-vr', { optionalFeatures: ['local-floor'] });
            session.addEventListener('end', cleanup, { once: true });
            if (document.fullscreenElement) await document.exitFullscreen();
            const { createVrRuntime } = await import('./runtime.mjs');
            if (!session) return;
            loadFormat(true);
            runtime = await createVrRuntime({ session, elements, preferences, state, setIntensity,
                exit, onPlacement: () => { persist(); menu.render(); }, onFormat: loadFormat,
                onError: error => {
                    void exit().then(() => {
                        menu.setOpen(true); menu.setStatus(`Could not render VR controls: ${error.message}`);
                        report(`Could not render VR controls: ${error.message}`, true);
                    });
                },
                onPrimary: () => {
                    // Exactly the existing left-mouse sequence, including the
                    // click suppression flags and the configured stroker option.
                    for (const type of ['mousedown', 'mouseup', 'click'])
                        elements.video.dispatchEvent(new MouseEvent(type, { button: 0, cancelable: true }));
                },
                onVariant: () => elements.video.dispatchEvent(new MouseEvent('mousedown', { button: 2, cancelable: true }))
            });
            if (!session) { runtime?.dispose(); runtime = null; return; }
            elements.enterVr.setAttribute('aria-pressed', 'true');
            menu.setStatus('Grip moves the video. While gripping, the stick changes size/curve and clicking it recenters.');
        } catch (error) {
            const failedSession = session;
            if (failedSession) { try { await failedSession.end(); } catch { cleanup(); } }
            else cleanup();
            menu.setOpen(true);
            menu.setStatus(`Could not enter VR: ${error.message}`);
            report(`Could not enter VR: ${error.message}`, true);
        } finally {
            starting = false;
            elements.enterVr.disabled = false;
        }
    }
    function mount() {
        menu = createVrMenu({ elements, preferences,
            onChange(key, formatChanged) {
                if (key === 'auto') {
                    delete formats[(currentItem()?.name || '').toLowerCase()];
                    loadFormat(true);
                } else {
                    if (formatChanged && activeName) formats[activeName.toLowerCase()] = Object.fromEntries(formatKeys.map(name => [name, preferences[name]]));
                    runtime?.settingsChanged(key);
                }
                persist();
            }, onRecenter: () => runtime?.recenterVideo(), onExit: exit
        });
        elements.enterVr.addEventListener('click', enter);
        elements.video.addEventListener('loadedmetadata', () => loadFormat(true));
        elements.video.addEventListener('emptied', () => loadFormat());
        window.addEventListener('pagehide', () => { void exit(); runtime?.dispose(); });
        document.addEventListener('keydown', event => {
            if (session && event.key === 'Escape') void exit();
        });
        if (window.isSecureContext && navigator.xr?.isSessionSupported) {
            void navigator.xr.isSessionSupported('immersive-vr')
                .then(supported => {
                    elements.enterVr.hidden = !supported;
                    elements.vrFollowToggle.hidden = !supported;
                })
                .catch(() => {
                    elements.enterVr.hidden = true;
                    elements.vrFollowToggle.hidden = true;
                });
        } else {
            elements.enterVr.hidden = true;
            elements.vrFollowToggle.hidden = true;
        }
        loadFormat(true);
    }
    return { mount };
}
