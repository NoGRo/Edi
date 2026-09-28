import { api, confirmedPlaybackCommand } from './edi-api.mjs';

// Playback controls are transient; saved variant pairs and ranges remain the source of truth.
export function createDeviceRouting({ request = api, command = confirmedPlaybackCommand,
    preferences = {}, save = () => {}, changed = () => {}, initialIntensity = 100 } = {}) {
    let devices = [], paused = false, pauseMode = null, intensity = initialIntensity;
    let intensityApplied = false;
    let variantsQueue = Promise.resolve(), intensityQueue = null, pendingIntensity = null;
    const desired = new Map();
    const connected = () => devices.filter(device => device.isReady !== false);
    const participates = (device, control) => devices.length <= 1 || preferences[device.name]?.[control] !== false;
    const allPause = () => devices.every(device => participates(device, 'pause'));
    const hidden = device => paused && pauseMode === 'selective' && participates(device, 'pause');
    const enqueue = action => {
        const next = variantsQueue.then(action);
        variantsQueue = next.catch(() => {});
        return next;
    };

    async function sendVariants(selections) {
        const changes = Object.fromEntries(devices.filter(device => device.name in selections
            && selections[device.name] !== device.selectedVariant)
            .map(device => [device.name, selections[device.name]]));
        if (!Object.keys(changes).length) return;
        await request('/Devices/Variants?persist=false', {
            method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(changes)
        });
        devices.forEach(device => {
            if (device.name in changes) device.selectedVariant = changes[device.name];
        });
        changed();
    }

    const effectiveVariants = () => Object.fromEntries(connected().map(device =>
        [device.name, hidden(device) ? 'None' : desired.get(device.name) || device.selectedVariant]));

    async function applyPause(value) {
        const wasPaused = paused;
        paused = value;
        const previous = pauseMode;
        pauseMode = value ? (allPause() ? 'global' : 'selective') : null;
        try {
            if (pauseMode === 'global') {
                if (previous !== 'global') await command('/Edi/Pause?untilResume=false');
            } else {
                // Set None before lifting a global pause so only excluded devices resume.
                await sendVariants(effectiveVariants());
                if (previous === 'global') await command('/Edi/Resume?AtCurrentTime=true');
            }
            changed();
        } catch (error) {
            paused = wasPaused;
            pauseMode = previous;
            changed();
            throw error;
        }
    }

    async function applyIntensity(value) {
        intensity = value;
        intensityApplied = true;
        if (devices.every(device => participates(device, 'intensity'))) {
            await command(`/Edi/Intensity/${value}`);
            devices.forEach(device => {
                device.min = device.baseMin ?? device.min ?? 0;
                device.max = device.min + Math.floor(((device.baseMax ?? 100) - device.min) * value / 100);
            });
        } else {
            const results = await Promise.allSettled(connected().map(async device => {
                const min = device.baseMin ?? device.min ?? 0;
                const baseMax = device.baseMax ?? device.max ?? 100;
                const max = min + Math.floor((baseMax - min) * (participates(device, 'intensity') ? value : 100) / 100);
                if (device.min === min && device.max === max) return;
                await request(`/Devices/${encodeURIComponent(device.name)}/Range/${min}-${max}?persist=false`, { method: 'POST' });
                device.min = min;
                device.max = max;
            }));
            const failure = results.find(result => result.status === 'rejected');
            if (failure) throw failure.reason;
        }
        changed();
    }

    function setIntensity(value) {
        pendingIntensity = value;
        if (!intensityQueue) intensityQueue = (async () => {
            try {
                while (pendingIntensity !== null) {
                    const next = pendingIntensity;
                    pendingIntensity = null;
                    try { await applyIntensity(next); }
                    catch (error) { if (pendingIntensity === null) throw error; }
                }
            } finally { intensityQueue = null; }
        })();
        return intensityQueue;
    }

    return {
        participates, allPause,
        visibleVariant: device => desired.get(device.name) || device.selectedVariant,
        isPaused: device => paused && participates(device, 'pause'),
        setDevices(values) {
            const topologyChanged = devices.length !== values.length || values.some(device =>
                !devices.some(previous => previous.name === device.name && previous.isReady === device.isReady));
            devices = values;
            devices.forEach(device => {
                if (!(paused && desired.has(device.name) && device.selectedVariant === 'None'))
                    desired.set(device.name, device.selectedVariant);
            });
            if (topologyChanged) return Promise.all([
                paused ? enqueue(() => applyPause(true)) : Promise.resolve(),
                intensityApplied ? setIntensity(intensity) : Promise.resolve()
            ]);
        },
        setVariants(selections) {
            return enqueue(async () => {
                Object.entries(selections).forEach(([name, variant]) => desired.set(name, variant));
                await sendVariants(Object.fromEntries(Object.entries(selections).map(([name, variant]) => {
                    const device = devices.find(device => device.name === name);
                    return [name, device && hidden(device) ? 'None' : variant];
                })));
                changed();
            });
        },
        async toggle(device, control) {
            preferences[device.name] = { ...preferences[device.name], [control]: !participates(device, control) };
            save(preferences);
            changed();
            if (control === 'pause' && paused) await enqueue(() => applyPause(true));
            if (control === 'intensity') await setIntensity(intensity);
        },
        setIntensity,
        setPaused: value => enqueue(() => applyPause(value)),
        async play(path) {
            // Play itself releases a normal global pause when a new gallery is needed.
            if (paused && pauseMode === 'global') await enqueue(async () => {
                paused = false;
                pauseMode = null;
                await sendVariants(effectiveVariants());
                changed();
            });
            await command(path);
        },
        async stop(path) {
            await command(path);
            await enqueue(async () => {
                paused = false;
                pauseMode = null;
                await sendVariants(effectiveVariants());
                changed();
            });
        }
    };
}

let stored = {};
try { stored = JSON.parse(globalThis.localStorage?.getItem('edi-player-device-controls') || '{}') || {}; } catch {}
export const deviceRouting = createDeviceRouting({
    preferences: stored,
    initialIntensity: Number(globalThis.localStorage?.getItem('edi-player-intensity') ?? 100),
    save: values => localStorage.setItem('edi-player-device-controls', JSON.stringify(values)),
    changed: () => globalThis.document?.dispatchEvent(new CustomEvent('edi-device-controls-state'))
});

export function routedPlaybackCommand(path) {
    if (path.startsWith('/Edi/Intensity/')) return deviceRouting.setIntensity(Number(path.split('/').pop()));
    if (path.startsWith('/Edi/Pause')) return deviceRouting.setPaused(true);
    if (path.startsWith('/Edi/Resume')) return deviceRouting.setPaused(false);
    if (path === '/Edi/Stop') return deviceRouting.stop(path);
    if (path.startsWith('/Edi/Play/')) return deviceRouting.play(path);
    return confirmedPlaybackCommand(path);
}
