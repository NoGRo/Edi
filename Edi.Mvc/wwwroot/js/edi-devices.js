document.addEventListener('DOMContentLoaded', async () => {
    const {
        autoVariantName, generatedFunscriptName, getDoubleSpeedScript,
        getHalfSpeedScript, listOriginalVariants, parseAutoVariant,
        parseFunscriptName, planDeviceRange, reorderValues, swapPairValues
    } = await import('/js/funscript-tools.mjs');

    const grids = [
        document.getElementById('devicesGrid'),
        document.getElementById('fullscreenDevicesGrid')
    ].filter(Boolean);
    const video = document.getElementById('videoPlayer');
    const toggle = document.getElementById('variantToggle');
    const panel = document.getElementById('variantTogglePanel');
    const closePanel = document.getElementById('closeVariantPanel');
    const primarySelect = document.getElementById('primaryVariant');
    const secondarySelect = document.getElementById('secondaryVariant');
    const toggleStatus = document.getElementById('variantToggleStatus');
    const globalSettingsKey = 'edi-player-variant-toggle';
    const deviceSettingsKey = 'edi-player-device-controls';
    const deviceOrderKey = 'edi-player-device-order';
    const generationLocks = new Map();
    const generatedBaseCache = new Map();
    let generationQueue = Promise.resolve();
    let switchQueue = Promise.resolve();
    let devices = [];
    let editingCount = 0;
    let panelHideTimer = null;
    let assetsReloadRevision = 0;
    let draggedDeviceName = null;
    let deviceMutationRevision = 0;

    const readObject = (key, fallback = {}) => {
        try { return { ...fallback, ...JSON.parse(localStorage.getItem(key) || '{}') }; }
        catch { return { ...fallback }; }
    };
    let globalSettings = readObject(globalSettingsKey, {
        primary: '', secondary: '', enabled: false
    });
    let storedDevices = readObject(deviceSettingsKey);
    let deviceOrder;
    try {
        const storedOrder = JSON.parse(localStorage.getItem(deviceOrderKey) || '[]');
        deviceOrder = Array.isArray(storedOrder) ? storedOrder.filter(value => typeof value === 'string') : [];
    } catch { deviceOrder = []; }

    const saveGlobalSettings = () => localStorage.setItem(
        globalSettingsKey,
        JSON.stringify(globalSettings));
    const saveDeviceSettings = () => localStorage.setItem(
        deviceSettingsKey,
        JSON.stringify(storedDevices));
    const saveDeviceOrder = () => localStorage.setItem(
        deviceOrderKey,
        JSON.stringify(deviceOrder));
    const post = async (path, options = {}) => {
        const response = await fetch(path, { method: 'POST', ...options });
        if (!response.ok) throw new Error(await response.text() || response.statusText);
        return response;
    };

    const deviceVariants = device => [...new Set(device.variants || [])]
        .filter(Boolean);
    const physicalVariants = () => [...new Set(
        devices.flatMap(deviceVariants))];
    const realSelection = value => value?.startsWith('real:') ? value.slice(5) : null;
    const autoSelection = value => value?.startsWith('auto:') ? value.slice(5) : null;
    const physicalSelection = value => realSelection(value)
        || (autoSelection(value) ? autoVariantName(autoSelection(value)) : null);
    const selectionAvailable = (value, variants = physicalVariants()) => {
        const physical = realSelection(value);
        return physical ? variants.includes(physical) : ['double', 'halve'].includes(autoSelection(value));
    };
    const variantOptionLabel = variant => {
        if (variant === 'None') return 'Stopped';
        const automatic = parseAutoVariant(variant);
        if (!automatic) return variant;
        return `${automatic.kind === 'double' ? 'Auto Double' : 'Auto Halve'} (generated)`;
    };

    function defaultPrimary(device) {
        const variants = deviceVariants(device);
        return variants.find(variant => variant.toLowerCase() === 'default')
            || (device.selectedVariant !== 'None' && variants.includes(device.selectedVariant) ? device.selectedVariant : null)
            || variants[0]
            || null;
    }

    function settingsFor(device) {
        const variants = deviceVariants(device);
        const existing = storedDevices[device.name] || {};
        const defaultVariant = defaultPrimary(device);
        const primaryFallback = selectionAvailable(globalSettings.primary, variants)
            ? globalSettings.primary
            : defaultVariant ? `real:${defaultVariant}` : '';
        const primary = selectionAvailable(existing.primary, variants)
            ? existing.primary
            : primaryFallback;
        const alternate = variants.find(variant => `real:${variant}` !== primary);
        const secondaryFallback = selectionAvailable(globalSettings.secondary, variants)
            && globalSettings.secondary !== primary
                ? globalSettings.secondary
                : alternate ? `real:${alternate}` : 'auto:double';
        const secondary = selectionAvailable(existing.secondary, variants)
            && existing.secondary !== primary
                ? existing.secondary
                : secondaryFallback;
        const numericMin = Number(existing.rangeMin);
        const numericMax = Number(existing.rangeMax);
        const legacyResumeVariant = existing.resumeVariant || null;
        const numericResumeMin = existing.resumeRangeMin == null ? NaN : Number(existing.resumeRangeMin);
        const numericResumeMax = existing.resumeRangeMax == null ? NaN : Number(existing.resumeRangeMax);
        const settings = Object.assign(existing, {
            pause: existing.pause !== false,
            range: existing.range !== false,
            switch: existing.switch !== false,
            customVariants: existing.customVariants === true,
            primary,
            secondary,
            rangeMin: Number.isFinite(numericMin) ? numericMin : Number(device.min ?? 0),
            rangeMax: Number.isFinite(numericMax) ? numericMax : Number(device.max ?? 100),
            paused: existing.paused === true || legacyResumeVariant != null,
            resumeRangeMin: Number.isFinite(numericResumeMin) ? numericResumeMin : null,
            resumeRangeMax: Number.isFinite(numericResumeMax) ? numericResumeMax : null,
            resumeVariant: legacyResumeVariant
        });
        storedDevices[device.name] = settings;
        return settings;
    }

    function appendVariantOptions(select, variants, selectedValue) {
        select.replaceChildren(new Option('Select…', ''));
        const available = document.createElement('optgroup');
        available.label = 'Available variants';
        variants.forEach(variant => available.append(
            new Option(variantOptionLabel(variant), `real:${variant}`)));
        select.append(available);
        const generated = document.createElement('optgroup');
        generated.label = 'Generate variant';
        generated.append(
            new Option('Generate Auto Double', 'auto:double'),
            new Option('Generate Auto Halve', 'auto:halve'));
        select.append(generated);
        select.value = [...select.options].some(option => option.value === selectedValue)
            ? selectedValue
            : '';
    }

    function variantField(select, label) {
        const field = document.createElement('div');
        field.className = 'device-variant-field';
        field.dataset.tooltip = label;
        field.append(select);
        return field;
    }

    function variantSelectFocused() {
        const focused = document.activeElement;
        return focused === primarySelect || focused === secondarySelect
            || grids.some(grid => grid.contains(focused) && focused?.matches?.('.device-variants select'));
    }

    function inferGlobalSettings() {
        const variants = physicalVariants();
        const namedDefault = variants.find(variant => variant.toLowerCase() === 'default');
        if (!selectionAvailable(globalSettings.primary, variants)) {
            globalSettings.primary = namedDefault ? `real:${namedDefault}`
                : variants[0] ? `real:${variants[0]}` : '';
        }
        if (!selectionAvailable(globalSettings.secondary, variants)
            || globalSettings.secondary === globalSettings.primary) {
            const alternate = variants.find(variant => `real:${variant}` !== globalSettings.primary);
            globalSettings.secondary = alternate ? `real:${alternate}` : 'auto:double';
        }
    }

    function refreshGlobalOptions() {
        inferGlobalSettings();
        const variants = physicalVariants();
        appendVariantOptions(primarySelect, variants, globalSettings.primary);
        appendVariantOptions(secondarySelect, variants, globalSettings.secondary);
        saveGlobalSettings();
    }

    function setStatus(message, error = false) {
        toggleStatus.textContent = message;
        toggleStatus.classList.toggle('text-danger', error);
        toggleStatus.classList.toggle('text-muted', !error);
    }

    function renderMasterState() {
        const targets = devices.filter(device => settingsFor(device).switch && device.isReady !== false);
        const active = globalSettings.enabled === true && targets.length > 0;
        toggle.setAttribute('aria-pressed', String(active));
        toggle.classList.toggle('btn-primary', active);
        toggle.classList.toggle('btn-outline-secondary', !active);
        setStatus(globalSettings.enabled
            ? `${targets.length} switch-enabled device${targets.length === 1 ? '' : 's'}. Each device can override the defaults below.`
            : 'Right-click variant switching is off.');
    }

    function setPanelOpen(open) {
        panel.hidden = !open;
        toggle.setAttribute('aria-expanded', String(open));
        document.dispatchEvent(new CustomEvent('edi-variant-panel', { detail: { open } }));
        if (panelHideTimer) clearTimeout(panelHideTimer);
        panelHideTimer = open ? setTimeout(() => setPanelOpen(false), 8000) : null;
    }

    async function fetchAssets() {
        const response = await fetch('/Edi/Assets');
        if (!response.ok) throw new Error(await response.text() || response.statusText);
        const paths = await response.json();
        const recognized = /(?:\.funscript|\.mp3|\.csv|\.txt)$/i;
        return Promise.all(paths.filter(path => typeof path === 'string'
            && recognized.test(path) && !/definitions_auto\.csv$/i.test(path)).map(async path => {
            const assetResponse = await fetch(path);
            if (!assetResponse.ok) throw new Error(`Could not download ${path}`);
            return new File([await assetResponse.blob()], decodeURIComponent(path.split('/').pop()), {
                type: assetResponse.headers.get('content-type') || ''
            });
        }));
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
            const form = new FormData();
            [...merged.values()].forEach(file => form.append('files', file, file.name));
            await post('/Edi/Assets', { body: form });
            generatedBaseCache.set(kind, baseVariant);
            assetsReloadRevision++;
            await refreshDeviceState(false, true);
            return physicalVariant;
        }).finally(() => generationLocks.delete(physicalVariant));
        generationLocks.set(physicalVariant, generation);
        generationQueue = generation.catch(() => {});
        return generation;
    }

    async function getGeneratedBase(kind) {
        if (generatedBaseCache.has(kind)) return generatedBaseCache.get(kind);
        const paths = await (await fetch('/Edi/Assets')).json();
        const path = paths.find(value => {
            if (typeof value !== 'string' || !value.toLowerCase().endsWith('.funscript')) return false;
            const name = decodeURIComponent(value.split('/').pop() || '');
            return parseAutoVariant(parseFunscriptName(name).variant)?.kind === kind;
        });
        if (!path) return null;
        const response = await fetch(path);
        if (!response.ok) return null;
        const base = (await response.json()).metadata?.ediAutoVariant?.baseVariant || null;
        generatedBaseCache.set(kind, base);
        return base;
    }

    async function resolveSelection(selection, otherSelection, device, allowGeneration) {
        const physical = realSelection(selection);
        if (physical) return deviceVariants(device).includes(physical) ? physical : null;
        const kind = autoSelection(selection);
        if (!kind) return null;
        const otherPhysical = realSelection(otherSelection);
        const base = otherPhysical && otherPhysical !== 'None' && !parseAutoVariant(otherPhysical)
            ? otherPhysical
            : listOriginalVariants(deviceVariants(device))
                .find(variant => variant !== 'None');
        if (!base) throw new Error('An original variant is required as the automatic source.');
        const generated = autoVariantName(kind);
        if (!allowGeneration) return deviceVariants(device).includes(generated) ? generated : null;
        const compatibleBase = physicalVariants().includes(generated) ? await getGeneratedBase(kind) : null;
        if (compatibleBase !== base) await generateVariant(kind, base);
        const refreshed = devices.find(candidate => candidate.name === device.name) || device;
        return deviceVariants(refreshed).includes(generated) ? generated : null;
    }

    async function resolvedPair(deviceName, allowGeneration) {
        const device = devices.find(candidate => candidate.name === deviceName);
        if (!device) return null;
        const settings = settingsFor(device);
        const primary = await resolveSelection(settings.primary, settings.secondary, device, allowGeneration);
        const refreshed = devices.find(candidate => candidate.name === deviceName) || device;
        const secondary = await resolveSelection(settings.secondary, settings.primary, refreshed, allowGeneration);
        return primary && secondary && primary !== secondary ? { primary, secondary } : null;
    }

    function actionButton(label, tooltip, path) {
        const button = document.createElement('button');
        button.type = 'button';
        button.className = 'btn btn-outline-secondary device-action icon-button';
        button.setAttribute('aria-label', label);
        button.dataset.tooltip = tooltip;
        button.innerHTML = `<svg class="player-option-icon utility-icon" viewBox="0 0 24 24" aria-hidden="true"><path d="${path}" /></svg>`;
        return button;
    }

    function paintAction(button, enabled, paused = false) {
        button.classList.toggle('btn-primary', enabled && !paused);
        button.classList.toggle('btn-danger', paused);
        button.classList.toggle('btn-outline-secondary', !enabled && !paused);
        button.setAttribute('aria-pressed', String(enabled));
    }

    async function changeDeviceSelection(deviceName, side, value) {
        deviceMutationRevision++;
        editingCount++;
        const device = devices.find(candidate => candidate.name === deviceName);
        const settings = settingsFor(device);
        const otherSide = side === 'primary' ? 'secondary' : 'primary';
        const previousValue = settings[side];
        const previousOtherValue = settings[otherSide];
        let previousPair = null;
        try {
            previousPair = await resolvedPair(deviceName, false);
            settings[side] = value;
            if (value && value === settings[otherSide])
                settings[otherSide] = previousValue;
            settings.customVariants = true;
            saveDeviceSettings();
            const nextPair = await resolvedPair(deviceName, true);
            if (!nextPair) throw new Error('Primary and Secondary must be different and available.');
            const current = devices.find(candidate => candidate.name === deviceName);
            const previousPhysical = previousPair?.[side];
            if (current && (!previousPhysical || current.selectedVariant === previousPhysical)) {
                await post(`/Devices/${encodeURIComponent(deviceName)}/Variant/${encodeURIComponent(nextPair[side])}`);
                current.selectedVariant = nextPair[side];
            }
        } catch (error) {
            settings[side] = previousValue;
            settings[otherSide] = previousOtherValue;
            setStatus(`Could not update ${deviceName}: ${error.message}`, true);
        } finally {
            editingCount--;
            saveDeviceSettings();
            await refreshDeviceState(true);
            if (assetsReloadRevision) {
                assetsReloadRevision = 0;
                document.dispatchEvent(new CustomEvent('edi-assets-reloaded'));
            }
        }
    }

    function swapDeviceSelections(deviceName) {
        const device = devices.find(candidate => candidate.name === deviceName);
        if (!device) return;
        const settings = settingsFor(device);
        Object.assign(settings, swapPairValues(settings.primary, settings.secondary), {
            customVariants: true
        });
        saveDeviceSettings();
        renderDevices();
    }

    function renderDevice(device) {
        const settings = settingsFor(device);
        const card = document.createElement('div');
        card.className = 'device-card';
        card.dataset.deviceName = device.name;
        card.addEventListener('dragstart', event => {
            draggedDeviceName = device.name;
            card.classList.add('dragging');
            event.dataTransfer?.setData('text/plain', device.name);
            if (event.dataTransfer) event.dataTransfer.effectAllowed = 'move';
        });
        card.addEventListener('dragend', () => {
            draggedDeviceName = null;
            grids.forEach(grid => grid.querySelectorAll('.device-card.dragging')
                .forEach(item => item.classList.remove('dragging')));
        });
        card.addEventListener('dragover', event => {
            if (draggedDeviceName && draggedDeviceName !== device.name) {
                event.preventDefault();
                if (event.dataTransfer) event.dataTransfer.dropEffect = 'move';
            }
        });
        card.addEventListener('drop', event => {
            event.preventDefault();
            reorderDevice(draggedDeviceName, device.name,
                event.clientY > card.getBoundingClientRect().top + card.offsetHeight / 2);
        });
        const heading = document.createElement('div');
        heading.className = 'device-meta-line';
        const dragHandle = document.createElement('span');
        dragHandle.className = 'device-drag-handle';
        dragHandle.textContent = '↕';
        dragHandle.draggable = true;
        dragHandle.title = `Drag ${device.name} to reorder`;
        const dot = document.createElement('span');
        dot.className = `device-status-dot${device.isReady ? ' connected' : ''}`;
        dot.title = device.isReady ? 'Connected' : 'Unavailable';
        const name = document.createElement('strong');
        name.className = 'device-meta-name';
        name.textContent = device.name;
        name.title = `${device.name} · Current variant: ${variantOptionLabel(device.selectedVariant || 'None')}`;
        const actions = document.createElement('div');
        actions.className = 'device-actions';
        const pause = actionButton(`Pause control for ${device.name}`, 'React to click/Space pause', 'M8 5v14M16 5v14');
        const rangeAction = actionButton(`Range control for ${device.name}`, 'React to mouse-wheel range', 'M5 18V10M12 18V6M19 18V3');
        const switchAction = actionButton(`Switch control for ${device.name}`, 'React to right-click variant switch', 'M4 12h16M8 8l-4 4 4 4M16 8l4 4-4 4');
        paintAction(pause, settings.pause, settings.paused);
        paintAction(rangeAction, settings.range);
        paintAction(switchAction, settings.switch);
        const toggleSetting = async key => {
            if (key === 'pause' && settings.pause && settings.paused)
                await setPaused(false, [device.name]);
            settings[key] = !settings[key];
            saveDeviceSettings();
            renderDevices();
            renderMasterState();
        };
        pause.addEventListener('click', () => toggleSetting('pause').catch(error => setStatus(error.message, true)));
        rangeAction.addEventListener('click', () => toggleSetting('range'));
        switchAction.addEventListener('click', () => toggleSetting('switch'));
        actions.append(pause, rangeAction, switchAction);
        heading.append(dragHandle, dot, name, actions);

        const variants = document.createElement('div');
        variants.className = 'device-variants';
        const primary = document.createElement('select');
        primary.className = 'form-select form-select-sm';
        primary.title = `Primary variant for ${device.name}`;
        primary.setAttribute('aria-label', primary.title);
        appendVariantOptions(primary, deviceVariants(device), settings.primary);
        const secondary = document.createElement('select');
        secondary.className = 'form-select form-select-sm';
        secondary.title = `Secondary variant for ${device.name}`;
        secondary.setAttribute('aria-label', secondary.title);
        appendVariantOptions(secondary, deviceVariants(device), settings.secondary);
        const activeVariant = device.selectedVariant;
        const primaryActive = physicalSelection(settings.primary) === activeVariant;
        const secondaryActive = physicalSelection(settings.secondary) === activeVariant;
        primary.classList.toggle('device-variant-active', primaryActive);
        secondary.classList.toggle('device-variant-active', secondaryActive);
        if (primaryActive) primary.title += ' · Active';
        if (secondaryActive) secondary.title += ' · Active';
        primary.addEventListener('change', () => changeDeviceSelection(device.name, 'primary', primary.value));
        secondary.addEventListener('change', () => changeDeviceSelection(device.name, 'secondary', secondary.value));
        const swap = actionButton(
            `Swap Primary and Secondary for ${device.name}`,
            'Swap Primary and Secondary',
            'M7 7h10M14 4l3 3-3 3M17 17H7M10 14l-3 3 3 3');
        swap.classList.add('device-variant-swap');
        swap.addEventListener('click', () => swapDeviceSelections(device.name));
        variants.append(
            variantField(primary, 'Primary'),
            swap,
            variantField(secondary, 'Secondary'));

        let low = Math.max(0, Math.min(100, Number(settings.rangeMin)));
        let high = Math.max(low, Math.min(100, Number(settings.rangeMax)));
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
            input.type = 'range'; input.min = 0; input.max = 100; input.step = 1;
        });
        min.value = low; max.value = high;
        min.setAttribute('aria-label', `Minimum range for ${device.name}`);
        max.setAttribute('aria-label', `Maximum range for ${device.name}`);
        const renderRange = changed => {
            low = Number(min.value); high = Number(max.value);
            if (changed === min && low > high) low = high;
            if (changed === max && high < low) high = low;
            min.value = low; max.value = high;
            range.style.setProperty('--range-low', `${low}%`);
            range.style.setProperty('--range-high', `${high}%`);
            min.style.zIndex = changed === min ? 3 : 2;
            max.style.zIndex = changed === max ? 3 : 2;
            const active = `${Number(device.min ?? 0)}–${Number(device.max ?? 100)}%`;
            tooltip.textContent = `Range ${low}–${high}%${active === `${low}–${high}%` ? '' : ` · active ${active}`}`;
        };
        const applyRange = async () => {
            editingCount++;
            try {
                settings.rangeMin = low; settings.rangeMax = high;
                const planned = planDeviceRange(settings.paused, low, high);
                if (planned.deferred) {
                    settings.resumeRangeMin = low;
                    settings.resumeRangeMax = high;
                }
                saveDeviceSettings();
                if (!planned.deferred) {
                    await post(`/Devices/${encodeURIComponent(device.name)}/Range/${planned.min}-${planned.max}`);
                    device.min = planned.min; device.max = planned.max;
                }
            } catch (error) {
                setStatus(`Could not change ${device.name} range: ${error.message}`, true);
            } finally { editingCount--; renderDevices(); }
        };
        min.addEventListener('input', () => renderRange(min));
        max.addEventListener('input', () => renderRange(max));
        min.addEventListener('change', applyRange);
        max.addEventListener('change', applyRange);
        range.append(track, min, max);
        rangeField.append(tooltip, range);
        renderRange();
        card.append(heading, variants, rangeField);
        return card;
    }

    function renderDevices() {
        grids.forEach(grid => {
            grid.replaceChildren();
            if (!devices.length) {
                const empty = document.createElement('div');
                empty.className = 'alert alert-secondary mb-0';
                empty.textContent = 'No devices detected.';
                grid.append(empty);
            } else devices.forEach(device => grid.append(renderDevice(device)));
        });
        publishVariantState();
    }

    function applyDeviceOrder() {
        const positions = new Map(deviceOrder.map((name, index) => [name, index]));
        devices.sort((left, right) =>
            (positions.get(left.name) ?? Number.MAX_SAFE_INTEGER)
            - (positions.get(right.name) ?? Number.MAX_SAFE_INTEGER));
        const known = new Set(deviceOrder);
        devices.forEach(device => {
            if (!known.has(device.name)) {
                deviceOrder.push(device.name);
                known.add(device.name);
            }
        });
        saveDeviceOrder();
    }

    function reorderDevice(sourceName, targetName, placeAfter) {
        if (!sourceName || sourceName === targetName) return;
        const names = devices.map(device => device.name);
        const reordered = reorderValues(names, sourceName, targetName, placeAfter);
        const visibleNames = new Set(reordered);
        deviceOrder = [...reordered, ...deviceOrder.filter(name => !visibleNames.has(name))];
        const byName = new Map(devices.map(device => [device.name, device]));
        devices = reordered.map(name => byName.get(name));
        draggedDeviceName = null;
        saveDeviceOrder();
        renderDevices();
    }

    function currentVariantStates() {
        return devices
            .filter(device => settingsFor(device).switch)
            .map(device => {
                const settings = settingsFor(device);
                const variant = device.selectedVariant ?? 'None';
                const primary = physicalSelection(settings.primary);
                const secondary = physicalSelection(settings.secondary);
                return {
                    name: device.name,
                    side: variant === primary ? 'P' : variant === secondary ? 'S' : null,
                    variant: variantOptionLabel(variant),
                    paused: settings.paused
                };
            });
    }

    function publishVariantState() {
        const states = currentVariantStates();
        document.dispatchEvent(new CustomEvent('edi-variant-state', {
            detail: { devices: states }
        }));
        return states;
    }

    async function migrateLegacyPauseState() {
        for (const device of devices) {
            const settings = settingsFor(device);
            const resumeVariant = settings.resumeVariant;
            if (!resumeVariant) continue;
            const resumeMin = Number(device.min ?? settings.rangeMin);
            const resumeMax = Number(device.max ?? settings.rangeMax);
            await post(`/Devices/${encodeURIComponent(device.name)}/Range/0-0`);
            if (deviceVariants(device).includes(resumeVariant)) {
                await post(`/Devices/${encodeURIComponent(device.name)}/Variant/${encodeURIComponent(resumeVariant)}`);
                device.selectedVariant = resumeVariant;
            }
            device.min = 0; device.max = 0;
            settings.paused = true;
            settings.resumeRangeMin = resumeMin;
            settings.resumeRangeMax = resumeMax;
            settings.resumeVariant = null;
        }
    }

    async function refreshDeviceState(render = true, force = false) {
        if (editingCount && !force) return;
        const mutationRevision = deviceMutationRevision;
        const response = await fetch('/Devices');
        if (!response.ok) throw new Error(await response.text() || response.statusText);
        const refreshedDevices = await response.json();
        if (mutationRevision !== deviceMutationRevision) return;
        devices = refreshedDevices;
        applyDeviceOrder();
        devices.forEach(settingsFor);
        await migrateLegacyPauseState();
        saveDeviceSettings();
        const preserveOpenCombo = variantSelectFocused();
        if (!preserveOpenCombo) refreshGlobalOptions();
        if (render && !preserveOpenCombo) renderDevices();
        renderMasterState();
    }

    async function setPaused(paused, selectedNames = null) {
        const selected = selectedNames ? new Set(selectedNames) : null;
        const targets = devices.filter(device => (!selected || selected.has(device.name))
            && settingsFor(device).pause && device.isReady !== false);
        const operations = targets.map(async device => {
            const settings = settingsFor(device);
            if (paused) {
                if (settings.paused) return;
                const resumeMin = Number(device.min ?? settings.rangeMin);
                const resumeMax = Number(device.max ?? settings.rangeMax);
                await post(`/Devices/${encodeURIComponent(device.name)}/Range/0-0`);
                settings.paused = true;
                settings.resumeRangeMin = resumeMin;
                settings.resumeRangeMax = resumeMax;
                device.min = 0; device.max = 0;
            } else {
                if (!settings.paused) return;
                const min = settings.resumeRangeMin != null && Number.isFinite(Number(settings.resumeRangeMin))
                    ? Number(settings.resumeRangeMin) : Number(settings.rangeMin);
                const max = settings.resumeRangeMax != null && Number.isFinite(Number(settings.resumeRangeMax))
                    ? Number(settings.resumeRangeMax) : Number(settings.rangeMax);
                const planned = planDeviceRange(false, min, Math.max(min, max));
                await post(`/Devices/${encodeURIComponent(device.name)}/Range/${planned.min}-${planned.max}`);
                settings.paused = false;
                settings.resumeRangeMin = null;
                settings.resumeRangeMax = null;
                device.min = planned.min; device.max = planned.max;
            }
        });
        await Promise.all(operations);
        saveDeviceSettings();
        renderDevices();
        return targets.length;
    }

    async function setRange(percent) {
        const targets = devices.filter(device => settingsFor(device).range && device.isReady !== false);
        await Promise.all(targets.map(async device => {
            const settings = settingsFor(device);
            const low = Math.max(0, Math.min(100, Math.round(settings.rangeMin)));
            const baseHigh = Math.max(low, Math.min(100, Math.round(settings.rangeMax)));
            const high = Math.round(low + (baseHigh - low) * percent / 100);
            const planned = planDeviceRange(settings.paused, low, high);
            if (planned.deferred) {
                settings.resumeRangeMin = low;
                settings.resumeRangeMax = high;
            } else {
                await post(`/Devices/${encodeURIComponent(device.name)}/Range/${planned.min}-${planned.max}`);
                device.min = planned.min; device.max = planned.max;
            }
        }));
        saveDeviceSettings();
        renderDevices();
        return targets.length;
    }

    window.ediDeviceControls = { setPaused, setRange, refresh: refreshDeviceState };

    toggle.addEventListener('click', () => {
        if (!globalSettings.enabled) {
            globalSettings.enabled = true;
            setPanelOpen(true);
        } else if (panel.hidden) setPanelOpen(true);
        else {
            globalSettings.enabled = false;
            setPanelOpen(false);
        }
        saveGlobalSettings();
        renderMasterState();
    });
    closePanel.addEventListener('click', event => {
        event.stopPropagation();
        setPanelOpen(false);
    });
    panel.addEventListener('pointerdown', () => setPanelOpen(true));
    panel.addEventListener('focusin', () => setPanelOpen(true));
    document.addEventListener('pointerdown', event => {
        if (!panel.hidden && !panel.contains(event.target) && !toggle.contains(event.target)) setPanelOpen(false);
    }, true);
    document.addEventListener('keydown', event => {
        if (event.key === 'Escape' && !panel.hidden) setPanelOpen(false);
    });

    const applyGlobalSelection = side => {
        const otherSide = side === 'primary' ? 'secondary' : 'primary';
        const previous = globalSettings[side];
        globalSettings[side] = side === 'primary' ? primarySelect.value : secondarySelect.value;
        if (globalSettings[side] && globalSettings[side] === globalSettings[otherSide])
            globalSettings[otherSide] = previous;
        devices.forEach(device => {
            const settings = settingsFor(device);
            if (!settings.customVariants) {
                settings.primary = globalSettings.primary;
                settings.secondary = globalSettings.secondary;
            }
        });
        saveGlobalSettings();
        saveDeviceSettings();
        refreshGlobalOptions();
        renderDevices();
        renderMasterState();
    };
    primarySelect.addEventListener('change', () => applyGlobalSelection('primary'));
    secondarySelect.addEventListener('change', () => applyGlobalSelection('secondary'));
    document.addEventListener('focusout', event => {
        const select = event.target;
        if (!(select instanceof HTMLSelectElement)
            || (select !== primarySelect && select !== secondarySelect
                && !select.matches('.device-variants select'))) return;
        setTimeout(() => {
            if (editingCount || variantSelectFocused()) return;
            refreshGlobalOptions();
            renderDevices();
        }, 0);
    });

    video?.addEventListener('mousedown', event => {
        if (event.button !== 2 || !globalSettings.enabled) return;
        event.preventDefault();
        switchQueue = switchQueue.then(async () => {
            deviceMutationRevision++;
            editingCount++;
            try {
                const targetNames = devices.filter(device => settingsFor(device).switch && device.isReady !== false)
                    .map(device => device.name);
                let changed = 0;
                for (const name of targetNames) {
                    const pair = await resolvedPair(name, true);
                    const device = devices.find(candidate => candidate.name === name);
                    if (!pair || !device) continue;
                    const settings = settingsFor(device);
                    const next = device.selectedVariant === pair.primary ? pair.secondary : pair.primary;
                    await post(`/Devices/${encodeURIComponent(name)}/Variant/${encodeURIComponent(next)}`);
                    device.selectedVariant = next;
                    changed++;
                }
                if (!changed) throw new Error('No switch-enabled device has two available variants.');
                saveDeviceSettings();
                renderDevices();
                document.dispatchEvent(new CustomEvent('edi-variant-switched', {
                    detail: { devices: currentVariantStates() }
                }));
                if (assetsReloadRevision) {
                    assetsReloadRevision = 0;
                    document.dispatchEvent(new CustomEvent('edi-assets-reloaded'));
                }
            } finally {
                editingCount--;
            }
        }).catch(error => setStatus(`Could not switch variants: ${error.message}`, true));
    });
    video?.addEventListener('contextmenu', event => event.preventDefault());
    document.addEventListener('edi-devices-refresh-requested', () => {
        void refreshDeviceState(true).catch(error => setStatus(error.message, true));
    });

    try { await refreshDeviceState(true); }
    catch (error) {
        setStatus(`Could not load devices: ${error.message}`, true);
        grids.forEach(grid => {
            const message = document.createElement('div');
            message.className = 'alert alert-danger mb-0';
            message.textContent = `Could not load devices: ${error.message}`;
            grid.replaceChildren(message);
        });
    }
    setInterval(() => {
        void refreshDeviceState(true).catch(error => setStatus(error.message, true));
    }, 10000);
});
