document.addEventListener('DOMContentLoaded', async () => {
    const {
        autoVariantName, changeDeviceVariantPair, generatedFunscriptName,
        getDoubleSpeedScript, getHalfSpeedScript, initializeDeviceVariantPair,
        listOriginalVariants, parseAutoVariant, parseFunscriptName
    } = await import('/js/funscript-tools.mjs');

    const { deviceRouting } = await import('/js/player/device-routing.mjs');
    const { assets: assetManager } = await import('/js/player/assets.mjs');
    const grid = document.getElementById('devicesGrid');
    const video = document.getElementById('videoPlayer');
    const toggle = document.getElementById('variantToggle');
    const panel = document.getElementById('variantTogglePanel');
    const closePanel = document.getElementById('closeVariantPanel');
    const primarySelect = document.getElementById('primaryVariant');
    const secondarySelect = document.getElementById('secondaryVariant');
    const toggleStatus = document.getElementById('variantToggleStatus');
    const globalSettingsKey = 'edi-player-variant-toggle';
    const devicePairsKey = 'edi-player-device-variant-pairs-v2';
    const generationLocks = new Map();
    const generatedBaseCache = new Map();
    let globalSettings = readObject(globalSettingsKey, {
        primary: '', secondary: '', activeSide: 'primary', enabled: false
    });
    let storedPairs = readObject(devicePairsKey);
    let devices = [];
    let editingCount = 0;
    let mutationRevision = 0;
    let refreshRevision = 0;
    let generationQueue = Promise.resolve();
    let switchQueue = Promise.resolve();
    let panelHideTimer = null;
    let assetsReloadPending = false;

    function readObject(key, fallback = {}) {
        try {
            const parsed = JSON.parse(localStorage.getItem(key) || '{}');
            return parsed && typeof parsed === 'object' && !Array.isArray(parsed)
                ? { ...fallback, ...parsed }
                : { ...fallback };
        } catch {
            return { ...fallback };
        }
    }

    const saveGlobalSettings = () => localStorage.setItem(
        globalSettingsKey,
        JSON.stringify(globalSettings));
    const savePairs = () => localStorage.setItem(
        devicePairsKey,
        JSON.stringify(storedPairs));
    const post = async (path, options = {}) => {
        mutationRevision++;
        const response = await fetch(path, { method: 'POST', ...options });
        if (!response.ok) throw new Error(await response.text() || response.statusText);
        return response;
    };

    const deviceVariants = device => [...new Set(device?.variants || [])].filter(Boolean);
    const findDeviceVariant = (device, variant) => deviceVariants(device)
        .find(candidate => candidate.toLowerCase() === variant?.toLowerCase()) || null;
    const physicalVariants = () => [...new Set(devices.flatMap(deviceVariants))];
    const realSelection = value => value?.startsWith('real:') ? value.slice(5) : null;
    const autoSelection = value => value?.startsWith('auto:') ? value.slice(5) : null;
    const physicalSelection = value => realSelection(value)
        || (autoSelection(value) ? autoVariantName(autoSelection(value)) : null);
    const activeSide = () => globalSettings.activeSide === 'secondary' ? 'secondary' : 'primary';
    const variantOptionLabel = variant => {
        if (variant === 'None') return 'Stopped';
        const automatic = parseAutoVariant(variant);
        if (!automatic) return variant;
        return automatic.kind === 'double' ? 'Auto Double' : 'Auto Halve';
    };

    function setStatus(message, error = false) {
        toggleStatus.textContent = message;
        toggleStatus.classList.toggle('text-danger', error);
        toggleStatus.classList.toggle('text-muted', !error);
    }

    function pairFor(device) {
        const current = storedPairs[device.name];
        const initialized = initializeDeviceVariantPair(
            current,
            deviceVariants(device),
            deviceRouting.visibleVariant(device),
            globalSettings);
        if (!current || current.primary !== initialized.primary
            || current.secondary !== initialized.secondary) {
            storedPairs[device.name] = initialized;
            savePairs();
        }
        return storedPairs[device.name];
    }

    function deviceSide(device) {
        if (deviceRouting.participates(device, 'variant')) return activeSide();
        const pair = pairFor(device);
        const variant = deviceRouting.visibleVariant(device);
        return variant === physicalSelection(pair.primary) ? 'primary'
            : variant === physicalSelection(pair.secondary) ? 'secondary' : null;
    }

    function appendVariantOptions(select, variants, selectedValue) {
        select.replaceChildren(new Option('Select…', ''));
        const available = document.createElement('optgroup');
        available.label = 'Available variants';
        variants.forEach(variant => available.append(
            new Option(variantOptionLabel(variant), `real:${variant}`)));
        select.append(available);

        const selectedPhysical = realSelection(selectedValue);
        if (selectedPhysical && !variants.includes(selectedPhysical)) {
            const unavailable = document.createElement('optgroup');
            unavailable.label = 'Unavailable (preference kept)';
            unavailable.append(new Option(selectedPhysical, selectedValue));
            select.append(unavailable);
        }

        const generated = document.createElement('optgroup');
        generated.label = 'Generate variant';
        generated.append(
            new Option('Auto Double', 'auto:double'),
            new Option('Auto Halve', 'auto:halve'));
        select.append(generated);
        select.value = [...select.options].some(option => option.value === selectedValue)
            ? selectedValue
            : '';
    }

    function refreshGlobalOptions() {
        const variants = physicalVariants();
        if (!globalSettings.primary) {
            const first = variants.find(variant => variant !== 'None') || variants[0];
            globalSettings.primary = first ? `real:${first}` : '';
        }
        if (!globalSettings.secondary) {
            const alternate = variants.find(variant => `real:${variant}` !== globalSettings.primary
                && variant !== 'None');
            const first = alternate || variants.find(variant => variant !== 'None') || variants[0];
            globalSettings.secondary = first ? `real:${first}` : '';
        }
        appendVariantOptions(primarySelect, variants, globalSettings.primary);
        appendVariantOptions(secondarySelect, variants, globalSettings.secondary);
        saveGlobalSettings();
    }

    function setPanelOpen(open) {
        panel.hidden = !open;
        toggle.setAttribute('aria-expanded', String(open));
        document.dispatchEvent(new CustomEvent('edi-variant-panel', { detail: { open } }));
        if (panelHideTimer) clearTimeout(panelHideTimer);
        panelHideTimer = open ? setTimeout(() => setPanelOpen(false), 8000) : null;
    }

    async function fetchAssets() {
        return assetManager.fetchForVariants();
    }

    async function generateVariant(kind, baseVariant) {
        const physicalVariant = autoVariantName(kind);
        if (generationLocks.has(physicalVariant)) return generationLocks.get(physicalVariant);
        const generation = generationQueue.then(async () => {
            setStatus(`Generating ${kind === 'double' ? 'Auto Double' : 'Auto Halve'}…`);
            const assets = await fetchAssets();
            const sourceScripts = assets.filter(file => file.name.toLowerCase().endsWith('.funscript')
                && parseFunscriptName(file.name).variant.toLowerCase() === baseVariant.toLowerCase());
            if (!sourceScripts.length) throw new Error(`No funscript assets were found for ${baseVariant}.`);
            const transform = kind === 'double' ? getDoubleSpeedScript : getHalfSpeedScript;
            const generated = await Promise.all(sourceScripts.map(async source => {
                const script = JSON.parse(await source.text());
                if (!Array.isArray(script.actions)) throw new Error(`${source.name} has no actions array.`);
                const output = transform(script, {});
                output.metadata = output.metadata || {};
                output.metadata.ediAutoVariant = { kind, baseVariant };
                return new File([JSON.stringify(output)], generatedFunscriptName(source.name, physicalVariant), {
                    type: 'application/json'
                });
            }));
            const retained = assets.filter(file => !file.name.toLowerCase().endsWith('.funscript')
                || parseAutoVariant(parseFunscriptName(file.name).variant)?.kind !== kind);
            const merged = new Map(retained.map(file => [file.name.toLowerCase(), file]));
            generated.forEach(file => merged.set(file.name.toLowerCase(), file));
            await assetManager.uploadGenerated(generated, [...merged.values()]);
            generatedBaseCache.set(kind, baseVariant);
            assetsReloadPending = true;
            await refreshDeviceState({ render: false, force: true });
            return physicalVariant;
        }).finally(() => generationLocks.delete(physicalVariant));
        generationLocks.set(physicalVariant, generation);
        generationQueue = generation.catch(() => {});
        return generation;
    }

    async function getGeneratedBase(kind) {
        if (generatedBaseCache.has(kind)) return generatedBaseCache.get(kind);
        const files = await fetchAssets();
        const file = files.find(value => {
            if (!value?.name?.toLowerCase().endsWith('.funscript')) return false;
            return parseAutoVariant(parseFunscriptName(value.name).variant)?.kind === kind;
        });
        if (!file) return null;
        const base = JSON.parse(await file.text()).metadata?.ediAutoVariant?.baseVariant || null;
        generatedBaseCache.set(kind, base);
        return base;
    }

    async function resolveSelection(selection, otherSelection, device, allowGeneration) {
        const physical = realSelection(selection);
        if (physical) return findDeviceVariant(device, physical);
        const kind = autoSelection(selection);
        if (!kind) return null;
        const otherPhysical = findDeviceVariant(device, realSelection(otherSelection));
        const base = otherPhysical && otherPhysical !== 'None'
            ? otherPhysical
            : listOriginalVariants(deviceVariants(device)).find(variant => variant !== 'None');
        if (!base) return null;
        const generated = autoVariantName(kind);
        if (!allowGeneration) return deviceVariants(device).includes(generated) ? generated : null;
        const compatibleBase = physicalVariants().includes(generated) ? await getGeneratedBase(kind) : null;
        if (compatibleBase !== base) await generateVariant(kind, base);
        const refreshed = devices.find(candidate => candidate.name === device.name) || device;
        return deviceVariants(refreshed).includes(generated) ? generated : null;
    }

    function iconButton(label, tooltip, path) {
        const button = document.createElement('button');
        button.type = 'button';
        button.className = 'btn btn-outline-secondary icon-button';
        button.setAttribute('aria-label', label);
        button.dataset.tooltip = tooltip;
        button.innerHTML = `<svg class="player-option-icon utility-icon" viewBox="0 0 24 24" aria-hidden="true"><path d="${path}" /></svg>`;
        return button;
    }

    function variantField(select, label) {
        const field = document.createElement('div');
        field.className = 'device-variant-field';
        field.dataset.tooltip = label;
        field.append(select);
        return field;
    }

    async function changeSelection(deviceName, side, value) {
        const device = devices.find(candidate => candidate.name === deviceName);
        if (!device) return;
        const previous = { ...pairFor(device) };
        let next;
        try {
            next = changeDeviceVariantPair(previous, side, value);
        } catch (error) {
            setStatus(error.message, true);
            renderDevices();
            return;
        }

        storedPairs[deviceName] = next;
        savePairs();
        renderDevices();
        editingCount++;
        try {
            const current = devices.find(candidate => candidate.name === deviceName);
            if (current && deviceRouting.participates(current, 'variant')) {
                const otherSide = side === 'primary' ? 'secondary' : 'primary';
                const selected = await resolveSelection(next[side], next[otherSide], current, true);
                if (!selected) throw new Error('The saved variant is not currently available on this device.');
                if (deviceSide(current) === side) {
                    mutationRevision++;
                    await deviceRouting.setVariants({ [deviceName]: selected });
                }
            }
            setStatus(`${deviceName}: ${side === 'primary' ? 'Primary' : 'Secondary'} saved.`);
        } catch (error) {
            setStatus(`${deviceName}: preference saved; activation failed: ${error.message}`, true);
        } finally {
            editingCount--;
            renderDevices();
            publishVariantState();
            publishAssetsReload();
        }
    }

    function renderDevice(device) {
        const settings = pairFor(device);
        const card = document.createElement('div');
        card.className = 'device-card';

        const heading = document.createElement('div');
        heading.className = 'device-meta-line';
        const dot = document.createElement('span');
        dot.className = `device-status-dot${device.isReady ? ' connected' : ''}`;
        dot.title = device.isReady ? 'Connected' : 'Unavailable';
        const name = document.createElement('strong');
        name.className = 'device-meta-name';
        name.textContent = device.name;
        name.title = `${device.name} · Current: ${variantOptionLabel(deviceRouting.visibleVariant(device) || 'None')}`;
        heading.append(dot, name);
        if (devices.length > 1) {
            const controls = document.createElement('div');
            controls.className = 'btn-group device-control-buttons';
            controls.setAttribute('role', 'group');
            controls.setAttribute('aria-label', `Controls for ${device.name}`);
            [['pause', 'strokerToggle', 'Pause / resume'], ['intensity', 'intensityToggle', 'Intensity'],
                ['variant', 'variantToggle', 'Variant']].forEach(([control, source, label]) => {
                const button = document.createElement('button');
                button.type = 'button';
                const enabled = deviceRouting.participates(device, control);
                const stopped = enabled && (control === 'pause' && deviceRouting.isPaused(device)
                    || control === 'intensity' && device.min === device.max
                    || control === 'variant' && deviceRouting.visibleVariant(device) === 'None');
                button.className = `btn icon-button ${stopped ? 'btn-danger' : enabled ? 'btn-primary' : 'btn-outline-secondary'}`;
                button.classList.toggle('stroker-paused', control === 'pause' && stopped);
                button.innerHTML = document.getElementById(source).querySelector('svg').outerHTML;
                button.setAttribute('aria-label', `${label} for ${device.name}`);
                button.setAttribute('aria-pressed', String(enabled));
                button.dataset.control = control;
                button.dataset.tooltip = `${label}: ${enabled ? 'on' : 'off'}`;
                button.addEventListener('click', async () => {
                    editingCount++;
                    mutationRevision++;
                    try {
                        await deviceRouting.toggle(device, control);
                        if (control === 'variant' && deviceRouting.participates(device, control)) {
                            const pair = pairFor(device);
                            const side = activeSide();
                            const selected = await resolveSelection(pair[side],
                                pair[side === 'primary' ? 'secondary' : 'primary'], device, true);
                            if (selected) await deviceRouting.setVariants({ [device.name]: selected });
                        }
                    }
                    catch (error) { setStatus(error.message, true); }
                    finally { editingCount--; renderDevices(); }
                });
                controls.append(button);
            });
            heading.append(controls);
        }

        const variants = document.createElement('div');
        variants.className = 'device-variants';
        const primary = document.createElement('select');
        primary.className = 'form-select form-select-sm';
        primary.setAttribute('aria-label', `Primary variant for ${device.name}`);
        appendVariantOptions(primary, deviceVariants(device), settings.primary);
        const secondary = document.createElement('select');
        secondary.className = 'form-select form-select-sm';
        secondary.setAttribute('aria-label', `Secondary variant for ${device.name}`);
        appendVariantOptions(secondary, deviceVariants(device), settings.secondary);
        const primaryActive = deviceSide(device) === 'primary';
        const secondaryActive = deviceSide(device) === 'secondary';
        primary.classList.toggle('device-variant-active', primaryActive);
        secondary.classList.toggle('device-variant-active', secondaryActive);
        primary.classList.toggle('device-variant-stopped', primaryActive && deviceRouting.visibleVariant(device) === 'None');
        secondary.classList.toggle('device-variant-stopped', secondaryActive && deviceRouting.visibleVariant(device) === 'None');
        primary.addEventListener('change', () => changeSelection(device.name, 'primary', primary.value));
        secondary.addEventListener('change', () => changeSelection(device.name, 'secondary', secondary.value));
        const switcher = iconButton(
            `Switch all devices to ${activeSide() === 'primary' ? 'Secondary' : 'Primary'}`,
            `Switch to ${activeSide() === 'primary' ? 'Secondary' : 'Primary'}`,
            'M7 7h10M14 4l3 3-3 3M17 17H7M10 14l-3 3 3 3');
        switcher.classList.add('device-variant-switch');
        switcher.addEventListener('click', () => switchVariantSide(
            activeSide() === 'primary' ? 'secondary' : 'primary'));
        variants.append(
            variantField(primary, 'Primary'),
            switcher,
            variantField(secondary, 'Secondary'));

        let low = Math.max(0, Math.min(100, Number(device.baseMin ?? device.min ?? 0)));
        let high = Math.max(low, Math.min(100, Number(device.baseMax ?? device.max ?? 100)));
        const rangeField = document.createElement('div');
        rangeField.className = 'device-range-field';
        const tooltip = document.createElement('span');
        tooltip.className = 'device-range-tooltip';
        const range = document.createElement('div');
        range.className = 'device-range';
        const track = document.createElement('div');
        track.className = 'device-range-track';
        const min = document.createElement('input');
        const max = document.createElement('input');
        [min, max].forEach(input => {
            input.className = 'device-range-input';
            input.type = 'range';
            input.min = 0;
            input.max = 100;
            input.step = 1;
        });
        min.value = low;
        max.value = high;
        min.setAttribute('aria-label', `Minimum range for ${device.name}`);
        max.setAttribute('aria-label', `Maximum range for ${device.name}`);
        const paintRange = changed => {
            low = Number(min.value);
            high = Number(max.value);
            if (changed === min && low > high) low = high;
            if (changed === max && high < low) high = low;
            min.value = low;
            max.value = high;
            range.style.setProperty('--range-low', `${low}%`);
            range.style.setProperty('--range-high', `${high}%`);
            min.style.zIndex = changed === min ? 3 : 2;
            max.style.zIndex = changed === max ? 3 : 2;
            tooltip.textContent = `Range ${low}–${high}%`;
        };
        const applyRange = async () => {
            editingCount++;
            try {
                await post(`/Devices/${encodeURIComponent(device.name)}/Range/${low}-${high}`);
                device.min = device.baseMin = low;
                device.max = device.baseMax = high;
            } catch (error) {
                setStatus(`Could not change ${device.name} range: ${error.message}`, true);
            } finally {
                editingCount--;
                renderDevices();
            }
        };
        min.addEventListener('input', () => paintRange(min));
        max.addEventListener('input', () => paintRange(max));
        min.addEventListener('change', applyRange);
        max.addEventListener('change', applyRange);
        range.append(track, min, max);
        rangeField.append(tooltip, range);
        paintRange();
        card.append(heading, variants, rangeField);
        return card;
    }

    function renderDevices() {
        grid.replaceChildren();
        if (!devices.length) {
            const empty = document.createElement('div');
            empty.className = 'alert alert-secondary mb-0';
            empty.textContent = 'No devices detected.';
            grid.append(empty);
        } else devices.forEach(device => grid.append(renderDevice(device)));
        renderFeatureState();
        publishVariantState();
    }

    function variantSelectFocused() {
        const focused = document.activeElement;
        return focused === primarySelect || focused === secondarySelect
            || grid.contains(focused)
                && focused?.matches?.('.device-variants select, .device-range-input');
    }

    async function refreshDeviceState({ render = true, force = false } = {}) {
        if (editingCount && !force) return;
        const requestRevision = ++refreshRevision;
        const mutationAtStart = mutationRevision;
        const response = await fetch('/Devices');
        if (!response.ok) throw new Error(await response.text() || response.statusText);
        const refreshed = await response.json();
        if (requestRevision !== refreshRevision || mutationAtStart !== mutationRevision) return;
        devices = refreshed;
        await deviceRouting.setDevices(devices);
        devices.forEach(pairFor);
        const preserveEditor = variantSelectFocused();
        if (!preserveEditor) refreshGlobalOptions();
        if (render && !preserveEditor) renderDevices();
        else renderFeatureState();
    }

    function currentVariantStates() {
        return devices.map(device => {
            const settings = pairFor(device);
            const primary = physicalSelection(settings.primary);
            const secondary = physicalSelection(settings.secondary);
            return {
                name: device.name,
                side: deviceRouting.visibleVariant(device) === primary ? 'P'
                    : deviceRouting.visibleVariant(device) === secondary ? 'S' : null,
                variant: variantOptionLabel(deviceRouting.visibleVariant(device) || 'None')
            };
        });
    }

    function publishVariantState() {
        document.dispatchEvent(new CustomEvent('edi-variant-state', {
            detail: { devices: currentVariantStates() }
        }));
    }

    function renderFeatureState() {
        const active = globalSettings.enabled === true;
        const connected = devices.filter(device => device.isReady !== false);
        const stoppedCount = connected.filter(device => deviceRouting.visibleVariant(device) === 'None').length;
        const allStopped = active && connected.length > 0 && stoppedCount === connected.length;
        const someStopped = active && stoppedCount > 0 && !allStopped;
        toggle.setAttribute('aria-pressed', String(active));
        toggle.classList.toggle('btn-primary', active && !allStopped);
        toggle.classList.toggle('btn-danger', allStopped);
        toggle.classList.toggle('btn-outline-secondary', !active);
        toggle.classList.toggle('variant-toggle-partially-stopped', someStopped);
        if (!toggleStatus.classList.contains('text-danger')) {
            setStatus(active
                ? `${devices.length} device${devices.length === 1 ? '' : 's'} configured individually.`
                : 'Right-click variant switching is off.');
        }
    }

    function publishAssetsReload() {
        if (!assetsReloadPending) return;
        assetsReloadPending = false;
        document.dispatchEvent(new CustomEvent('edi-assets-reloaded'));
    }

    toggle.addEventListener('click', () => {
        if (!globalSettings.enabled) {
            globalSettings.enabled = true;
            setPanelOpen(true);
        } else if (panel.hidden) {
            setPanelOpen(true);
        } else {
            globalSettings.enabled = false;
            setPanelOpen(false);
        }
        saveGlobalSettings();
        renderFeatureState();
    });
    closePanel.addEventListener('click', event => {
        event.stopPropagation();
        setPanelOpen(false);
    });
    panel.addEventListener('pointerdown', () => setPanelOpen(true));
    panel.addEventListener('focusin', () => setPanelOpen(true));
    document.addEventListener('pointerdown', event => {
        if (!panel.hidden && !panel.contains(event.target) && !toggle.contains(event.target))
            setPanelOpen(false);
    }, true);
    document.addEventListener('keydown', event => {
        if (event.key === 'Escape' && !panel.hidden) setPanelOpen(false);
    });

    function changeGlobalSelection(side, value) {
        try {
            globalSettings = changeDeviceVariantPair(globalSettings, side, value);
            saveGlobalSettings();
            refreshGlobalOptions();
            setStatus('Defaults saved. Existing device pairs are unchanged.');
        } catch (error) {
            setStatus(error.message, true);
            refreshGlobalOptions();
        }
    }
    primarySelect.addEventListener('change', () => changeGlobalSelection('primary', primarySelect.value));
    secondarySelect.addEventListener('change', () => changeGlobalSelection('secondary', secondarySelect.value));
    document.addEventListener('focusout', event => {
        if (!(event.target instanceof HTMLSelectElement)
            && !event.target?.matches?.('.device-range-input')) return;
        setTimeout(() => {
            if (!editingCount && !variantSelectFocused()) renderDevices();
        }, 0);
    });

    function switchVariantSide(targetSide) {
        switchQueue = switchQueue.then(async () => {
            editingCount++;
            const failures = [];
            let changed = 0;
            try {
                const snapshots = devices.filter(device => device.isReady !== false
                    && deviceRouting.participates(device, 'variant'));
                const prepared = await Promise.all(snapshots.map(async snapshot => {
                    const pair = pairFor(snapshot);
                    const otherSide = targetSide === 'primary' ? 'secondary' : 'primary';
                    try {
                        const selected = await resolveSelection(
                            pair[targetSide], pair[otherSide], snapshot, true);
                        return { snapshot, selected };
                    } catch (error) {
                        failures.push(`${snapshot.name}: ${error.message}`);
                        return { snapshot, selected: null };
                    }
                }));
                const selections = {};
                prepared.forEach(({ snapshot, selected }) => {
                    if (selected) selections[snapshot.name] = selected;
                    else if (!failures.some(failure => failure.startsWith(`${snapshot.name}:`)))
                        failures.push(`${snapshot.name}: selected variant could not be prepared.`);
                });
                if (Object.keys(selections).length) {
                    mutationRevision++;
                    await deviceRouting.setVariants(selections);
                    changed = Object.keys(selections).length;
                }
                if (!changed && !failures.length)
                    failures.push('No connected device has an available variant for this mode.');
                globalSettings.activeSide = targetSide;
                saveGlobalSettings();
                renderDevices();
                document.dispatchEvent(new CustomEvent('edi-variant-switched', {
                    detail: { devices: currentVariantStates() }
                }));
                setStatus(failures.length
                    ? `${changed} switched; ${failures.length} failed: ${failures.join(' | ')}`
                    : `${changed} device${changed === 1 ? '' : 's'} switched.`,
                failures.length > 0);
            } finally {
                editingCount--;
            }
        }).catch(error => setStatus(`Could not switch variants: ${error.message}`, true));
    }

    document.addEventListener('edi-device-controls-state', () => {
        mutationRevision++;
        if (!variantSelectFocused()) renderDevices();
    });

    video?.addEventListener('mousedown', event => {
        if (event.button !== 2 || !globalSettings.enabled) return;
        event.preventDefault();
        switchVariantSide(activeSide() === 'primary' ? 'secondary' : 'primary');
    });
    video?.addEventListener('contextmenu', event => event.preventDefault());
    document.addEventListener('edi-assets-cached', () => generatedBaseCache.clear());
    document.addEventListener('edi-devices-refresh-requested', () => {
        void refreshDeviceState().catch(error => setStatus(`Could not refresh devices: ${error.message}`, true));
    });

    try {
        await refreshDeviceState();
    } catch (error) {
        setStatus(`Could not load devices: ${error.message}`, true);
        const message = document.createElement('div');
        message.className = 'alert alert-danger mb-0';
        message.textContent = `Could not load devices: ${error.message}`;
        grid.replaceChildren(message);
    }
    setInterval(() => {
        void refreshDeviceState().catch(error => setStatus(`Could not refresh devices: ${error.message}`, true));
    }, 10000);
});
