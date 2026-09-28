document.addEventListener('DOMContentLoaded', async () => {
    const {
        autoVariantName, changeDeviceVariantPair, initializeDeviceVariantPair,
        listOriginalVariants, parseAutoVariant
    } = await import('/js/funscript-tools.mjs');

    const { deviceRouting } = await import('/js/player/device-routing.mjs');
    const { variantsForVideo, videoVariantPair } = await import('/js/player/video-variants.mjs');
    const { assets: assetManager, assetVariantsForVideo, generationBasesForVideo } = await import('/js/player/assets.mjs');
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
    let globalSettings = readObject(globalSettingsKey, {
        primary: '', secondary: '', activeSide: 'primary', enabled: false
    });
    let storedPairs = readObject(devicePairsKey);
    const historyKey = 'edi-player-video-variant-pairs';
    const deviceOrderKey = 'edi-player-device-order';
    let videoPairs = readObject(historyKey);
    let deviceOrder = [];
    try { deviceOrder = JSON.parse(localStorage.getItem(deviceOrderKey) || '[]'); } catch {}
    if (!Array.isArray(deviceOrder)) deviceOrder = [];
    let videoContext = null, videoAssets = [], draggedDevice = null;
    const videoKey = () => videoContext?.name.toLowerCase();
    const videoKnown = () => videoContext && (assetVariantsForVideo(videoAssets, videoContext.name).length
        || videoContext.definitions.some(definition => definition.fileName
            && definition.fileName.replace(/\.[^.]+$/, '').toLowerCase()
                === videoContext.name.replace(/\.[^.]+$/, '').toLowerCase()));
    let devices = [];
    const rangePainters = new Map();
    document.addEventListener('scroll', () => {
        grid.querySelectorAll('.device-range-tooltip:popover-open').forEach(tooltip => tooltip.hidePopover());
    }, true);
    let editingCount = 0;
    let mutationRevision = 0;
    let refreshRevision = 0;
    let switchQueue = Promise.resolve();
    let panelHideTimer = null;
    let assetsReloadPending = false;
    let assetsNeedDeviceRefresh = false;

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
    const savePairs = () => {
        localStorage.setItem(devicePairsKey, JSON.stringify(storedPairs));
        if (videoKey() && videoKnown()) {
            videoPairs[videoKey()] = { ...videoPairs[videoKey()],
                ...Object.fromEntries(devices.map(device => [device.name, { ...storedPairs[device.name] }])) };
            localStorage.setItem(historyKey, JSON.stringify(videoPairs));
        }
    };
    const post = async (path, options = {}) => {
        mutationRevision++;
        const response = await fetch(path, { method: 'POST', ...options });
        if (!response.ok) throw new Error(await response.text() || response.statusText);
        return response;
    };

    const deviceVariants = device => variantsForVideo(device, videoContext, videoAssets).filter(Boolean);
    const findDeviceVariant = (device, variant) => deviceVariants(device)
        .find(candidate => candidate.toLowerCase() === variant?.toLowerCase()) || null;
    const physicalVariants = () => [...new Set(devices.flatMap(deviceVariants))];
    const realSelection = value => value?.startsWith('real:') ? value.slice(5) : null;
    const autoSelection = value => value?.startsWith('auto:') ? value.slice(5) : null;
    const physicalSelection = (value, base) => realSelection(value)
        || (autoSelection(value) ? autoVariantName(autoSelection(value), base) : null);
    const activeSide = () => globalSettings.activeSide === 'secondary' ? 'secondary' : 'primary';
    const variantOptionLabel = variant => {
        if (variant === 'None') return 'Stopped';
        const automatic = parseAutoVariant(variant);
        if (!automatic) return variant;
        const label = automatic.kind === 'double' ? 'Auto Double' : 'Auto Halve';
        let base = automatic.legacyBase;
        try { base = base && decodeURIComponent(base); } catch {}
        return base ? `${label} (${base})` : label;
    };

    function setStatus(message, error = false) {
        toggleStatus.textContent = message;
        toggleStatus.classList.toggle('text-danger', error);
        toggleStatus.classList.toggle('text-muted', !error);
    }

    function pairFor(device) {
        const current = storedPairs[device.name];
        if (videoContext && !videoKnown()) return { primary: 'real:None', secondary: 'real:None',
            activeSide: current?.activeSide || activeSide() };
        if (videoContext) return current || { primary: 'real:None', secondary: 'real:None', activeSide: activeSide() };
        const initialized = initializeDeviceVariantPair(
            current,
            [...new Set(device.variants || [])],
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
        if (deviceRouting.participates(device, 'variant')) return pairFor(device).activeSide || activeSide();
        const pair = pairFor(device);
        const variant = deviceRouting.visibleVariant(device);
        return variant === physicalSelection(pair.primary, pair.bases?.primary) ? 'primary'
            : variant === physicalSelection(pair.secondary, pair.bases?.secondary) ? 'secondary' : null;
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
            new Option('Auto Double', 'auto:double'),
            new Option('Auto Halve', 'auto:halve'));
        if (generationBasesForVideo(videoAssets, videoContext?.name).some(base =>
            variants.some(variant => variant.toLowerCase() === base.toLowerCase()))) select.append(generated);
        select.value = [...select.options].some(option => option.value === selectedValue)
            ? selectedValue
            : '';
    }

    function refreshGlobalOptions() {
        const variants = physicalVariants();
        if (!videoContext) {
            appendVariantOptions(primarySelect, variants, globalSettings.primary);
            appendVariantOptions(secondarySelect, variants, globalSettings.secondary);
            return;
        }
        if (videoContext) {
            const defaults = videoVariantPair({ previous: globalSettings, variants,
                defaults: { defaultVariant: 'default' } });
            globalSettings.primary = defaults.primary;
            globalSettings.secondary = defaults.secondary;
        }
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

    async function fetchAssets(videoName = videoContext?.name) {
        return assetManager.fetchForVariants(videoName);
    }

    async function chooseAutoBase(device, side, kind, candidates) {
        const pair = pairFor(device);
        const saved = pair.bases?.[side];
        let base = candidates.find(candidate => candidate.toLowerCase() === saved?.toLowerCase());
        if (base) return base;
        if (!base && candidates.length === 1) base = candidates[0];
        if (!base && candidates.length > 1) base = await new Promise(resolve => {
            const dialog = document.createElement('dialog');
            dialog.className = 'auto-variant-dialog';
            const form = document.createElement('form');
            form.method = 'dialog';
            const label = document.createElement('label');
            label.textContent = `Choose the original variant for Auto ${kind === 'double' ? 'Double' : 'Halve'} (${device.name})`;
            const select = document.createElement('select');
            select.className = 'form-select mt-2 mb-3';
            select.setAttribute('aria-label', 'Original base variant');
            candidates.forEach(candidate => select.append(new Option(candidate, candidate)));
            const confirm = document.createElement('button');
            confirm.className = 'btn btn-primary';
            confirm.textContent = 'Generate';
            confirm.value = 'generate';
            const cancel = document.createElement('button');
            cancel.className = 'btn btn-outline-secondary ms-2';
            cancel.textContent = 'Cancel';
            cancel.value = 'cancel';
            label.append(select);
            form.append(label, confirm, cancel);
            dialog.append(form);
            (document.fullscreenElement || document.body).append(dialog);
            dialog.addEventListener('close', () => {
                resolve(dialog.returnValue === 'generate' ? select.value : null);
                dialog.remove();
            }, { once: true });
            dialog.showModal();
        });
        if (!base) throw new Error('Automatic variant generation cancelled.');
        pair.bases = { ...pair.bases, [side]: base };
        savePairs();
        return base;
    }

    async function resolveSelection(selection, device, side = deviceSide(device) || activeSide()) {
        const physical = realSelection(selection);
        if (physical) {
            if (physical !== 'None') {
                const result = await assetManager.prepareVariant({ videoName: videoContext?.name, selection });
                if (result.changed) {
                    assetsReloadPending = true;
                    await refreshDeviceState({ render: false, force: true, syncVideo: false });
                }
            }
            return findDeviceVariant(devices.find(candidate => candidate.name === device.name) || device, physical);
        }
        const kind = autoSelection(selection);
        if (!kind) return null;
        const originals = listOriginalVariants(deviceVariants(device)).filter(variant => variant !== 'None');
        const bases = (await assetManager.generationBases(videoContext?.name)).filter(base =>
            originals.some(original => original.toLowerCase() === base.toLowerCase()));
        if (!bases.length) return null;
        const base = await chooseAutoBase(device, side, kind, bases);
        const result = await assetManager.prepareVariant({ videoName: videoContext?.name, selection, baseVariant: base });
        if (result.changed) {
            assetsReloadPending = true;
            await refreshDeviceState({ render: false, force: true, syncVideo: false });
        }
        const refreshed = devices.find(candidate => candidate.name === device.name) || device;
        return deviceVariants(refreshed).includes(result.variant) ? result.variant : null;
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
            if (value !== previous[side]) {
                next.bases = { ...previous.bases };
                delete next.bases[side];
            }
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
            if (current) {
                const selected = await resolveSelection(next[side], current, side);
                if (!selected) throw new Error('The saved variant is not currently available on this device.');
                if (deviceRouting.participates(current, 'variant') && deviceSide(current) === side) {
                    mutationRevision++;
                    await deviceRouting.setVariants({ [deviceName]: selected });
                }
            }
            setStatus(`${deviceName}: ${side === 'primary' ? 'Primary' : 'Secondary'} saved.`);
        } catch (error) {
            storedPairs[deviceName] = previous;
            savePairs();
            setStatus(`${deviceName}: ${error.message}`, true);
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
        card.dataset.deviceName = device.name;
        card.addEventListener('dragover', event => { if (draggedDevice) event.preventDefault(); });
        card.addEventListener('drop', event => {
            if (!draggedDevice) return;
            event.preventDefault();
            const ordered = orderedDevices().map(device => device.name);
            const from = ordered.indexOf(draggedDevice), to = ordered.indexOf(device.name);
            if (from < 0 || to < 0 || from === to) return;
            ordered.splice(to, 0, ordered.splice(from, 1)[0]);
            deviceOrder = [...ordered, ...deviceOrder.filter(name => !ordered.includes(name))];
            localStorage.setItem(deviceOrderKey, JSON.stringify(deviceOrder));
            draggedDevice = null;
            editingCount--;
            renderDevices();
        });

        const heading = document.createElement('div');
        heading.className = 'device-meta-line';
        const handle = document.createElement('span');
        handle.textContent = '↕';
        handle.className = 'text-muted device-drag-handle';
        handle.title = 'Drag to reorder';
        handle.draggable = true;
        handle.addEventListener('dragstart', event => {
            draggedDevice = device.name;
            editingCount++;
            event.dataTransfer.setData('text/plain', device.name);
            event.dataTransfer.effectAllowed = 'move';
            card.classList.add('dragging');
        });
        handle.addEventListener('dragend', () => {
            if (draggedDevice) editingCount--;
            draggedDevice = null;
            card.classList.remove('dragging');
        });
        heading.append(handle);
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
                            pair.activeSide = side;
                            savePairs();
                            const selected = await resolveSelection(pair[side], device, side);
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
            `Swap primary and secondary for ${device.name}`,
            'Swap primary and secondary selections',
            'M7 7h10M14 4l3 3-3 3M17 17H7M10 14l-3 3 3 3');
        switcher.classList.add('device-variant-switch');
        switcher.addEventListener('click', () => {
            switchQueue = switchQueue.then(async () => {
                editingCount++;
                try {
                    const pair = pairFor(device);
                    if (pair.primary === pair.secondary && pair.bases?.primary === pair.bases?.secondary) return;
                    const side = deviceSide(device) || activeSide();
                    const next = { ...pair, primary: pair.secondary, secondary: pair.primary,
                        bases: { primary: pair.bases?.secondary, secondary: pair.bases?.primary } };
                    storedPairs[device.name] = next;
                    savePairs();
                    if (deviceRouting.participates(device, 'variant')) {
                        const selected = await resolveSelection(next[side], device, side);
                        if (selected) { mutationRevision++; await deviceRouting.setVariants({ [device.name]: selected }); }
                    }
                } finally { editingCount--; renderDevices(); publishAssetsReload(); }
            }).catch(error => setStatus(error.message, true));
        });
        variants.append(
            variantField(primary, 'Primary'),
            switcher,
            variantField(secondary, 'Secondary'));

        let low = Math.max(0, Math.min(100, Number(device.baseMin ?? device.min ?? 0)));
        let high = Math.max(low, Math.min(100, Number(device.baseMax ?? device.max ?? 100)));
        const rangeField = document.createElement('div');
        rangeField.className = 'device-range-field';
        const range = document.createElement('div');
        range.className = 'device-range';
        const track = document.createElement('div');
        track.className = 'device-range-track';
        const effective = document.createElement('div');
        effective.className = 'device-range-effective';
        const tooltip = document.createElement('div');
        tooltip.className = 'player-tooltip device-range-tooltip';
        tooltip.popover = 'manual';
        tooltip.setAttribute('role', 'tooltip');
        let rangePoints = [], hoverPoint = null;
        const hideTooltip = () => {
            hoverPoint = null;
            if (tooltip.matches(':popover-open')) tooltip.hidePopover();
        };
        tooltip.addEventListener('toggle', event => {
            if (event.newState === 'closed') hoverPoint = null;
        });
        const showTooltip = event => {
            hoverPoint = { clientX: event.clientX, clientY: event.clientY };
            const bounds = range.getBoundingClientRect();
            const inset = track.getBoundingClientRect().left - bounds.left;
            const width = bounds.width - inset * 2;
            const points = rangePoints.map(point => ({ ...point,
                x: bounds.left + inset + width * point.value / 100,
                y: point.center ? center.getBoundingClientRect().top + center.offsetHeight / 2 : bounds.top + bounds.height / 2
            }));
            const nearest = points.sort((a, b) => Math.hypot(a.x - event.clientX, a.y - event.clientY)
                - Math.hypot(b.x - event.clientX, b.y - event.clientY))[0];
            if (!nearest || Math.hypot(nearest.x - event.clientX, nearest.y - event.clientY) > 24) {
                hideTooltip();
                return;
            }
            tooltip.textContent = nearest.text;
            if (!tooltip.matches(':popover-open')) tooltip.showPopover();
            const left = Math.max(8, Math.min(window.innerWidth - tooltip.offsetWidth - 8, nearest.x - tooltip.offsetWidth / 2));
            tooltip.style.left = `${left}px`;
            tooltip.style.top = `${Math.min(window.innerHeight - tooltip.offsetHeight - 8, bounds.bottom + 8)}px`;
            tooltip.style.setProperty('--tooltip-arrow', `${nearest.x - left}px`);
        };
        const min = document.createElement('input');
        const max = document.createElement('input');
        const center = document.createElement('input');
        [min, max, center].forEach(input => {
            input.className = 'device-range-input';
            input.type = 'range';
            input.min = 0;
            input.max = 100;
            input.step = 1;
        });
        min.value = low;
        max.value = high;
        center.value = deviceRouting.rangeCenter(device);
        center.classList.add('device-range-center');
        center.setAttribute('aria-label', `Intensity collapse point for ${device.name}`);
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
            const current = devices.find(candidate => candidate.name === device.name) || device;
            const result = changed
                ? deviceRouting.effectiveRange({ ...current, baseMin: low, baseMax: high }, undefined, Number(center.value))
                : { min: current.min ?? low, max: current.max ?? high };
            range.style.setProperty('--effective-low', `${result.min}%`);
            range.style.setProperty('--effective-high', `${result.max}%`);
            min.style.zIndex = changed === min ? 3 : 2;
            max.style.zIndex = changed === max ? 3 : 2;
            const boundsText = `Min ${low}% · Max ${high}%${low === result.min && high === result.max
                ? '' : `\nEffective ${result.min === result.max ? result.min : `${result.min}–${result.max}`}%`}`;
            rangePoints = [
                { value: low, text: boundsText }, { value: high, text: boundsText },
                { value: result.min, text: boundsText }, { value: result.max, text: boundsText },
                { value: Number(center.value), text: `Center ${center.value}%`, center: true }
            ];
            if (hoverPoint) showTooltip(hoverPoint);
        };
        const applyRange = async () => {
            editingCount++;
            try {
                await post(`/Devices/${encodeURIComponent(device.name)}/Range/${low}-${high}`);
                device.min = device.baseMin = low;
                device.max = device.baseMax = high;
                await deviceRouting.reapplyIntensity();
            } catch (error) {
                setStatus(`Could not change ${device.name} range: ${error.message}`, true);
            } finally {
                editingCount--;
                renderDevices();
            }
        };
        min.addEventListener('input', () => paintRange(min));
        max.addEventListener('input', () => paintRange(max));
        center.addEventListener('input', () => paintRange(center));
        center.addEventListener('change', async () => {
            editingCount++;
            mutationRevision++;
            try {
                await deviceRouting.setRangeCenter(device, center.value);
            } catch (error) {
                setStatus(`Could not apply ${device.name} collapse point: ${error.message}`, true);
            } finally {
                editingCount--;
                paintRange(center);
            }
        });
        min.addEventListener('change', applyRange);
        max.addEventListener('change', applyRange);
        range.append(track, effective, min, max, center);
        range.append(tooltip);
        rangeField.append(range);
        rangeField.addEventListener('pointermove', showTooltip);
        rangeField.addEventListener('pointerleave', hideTooltip);
        rangePainters.set(device.name, paintRange);
        paintRange();
        card.append(heading, variants, rangeField);
        return card;
    }

    function orderedDevices() {
        const rank = name => { const index = deviceOrder.indexOf(name); return index < 0 ? deviceOrder.length : index; };
        return [...devices].sort((a, b) => rank(a.name) - rank(b.name));
    }

    function renderDevices() {
        rangePainters.clear();
        grid.replaceChildren();
        if (!devices.length) {
            const empty = document.createElement('div');
            empty.className = 'alert alert-secondary mb-0';
            empty.textContent = 'No devices detected.';
            grid.append(empty);
        } else orderedDevices().forEach(device => grid.append(renderDevice(device)));
        renderFeatureState();
        publishVariantState();
    }

    function variantSelectFocused() {
        const focused = document.activeElement;
        return focused === primarySelect || focused === secondarySelect
            || grid.contains(focused)
                && focused?.matches?.('.device-variants select, .device-range-input');
    }

    async function reconcileVideoPairs(previous = storedPairs, selections = {}) {
        if (!videoKnown()) return;
        const changes = {};
        for (const device of [...devices]) {
            const pair = videoVariantPair({
                history: videoPairs[videoKey()]?.[device.name], previous: previous[device.name],
                variants: deviceVariants(device), selected: selections[device.name] ?? deviceRouting.visibleVariant(device),
                defaults: { ...globalSettings, defaultVariant: 'default' },
                side: previous[device.name]?.activeSide || activeSide()
            });
            storedPairs[device.name] = pair;
            const side = pair.activeSide, otherSide = side === 'primary' ? 'secondary' : 'primary';
            let selected;
            for (const key of [side, otherSide]) {
                if (key !== side && !autoSelection(pair[key])) continue;
                try {
                    const variant = await resolveSelection(pair[key], device, key);
                    if (!variant) throw new Error('The selected variant could not be prepared.');
                    if (key === side) selected = variant;
                } catch (error) {
                    const originals = listOriginalVariants(deviceVariants(device));
                    const fallback = originals.find(variant => variant === deviceRouting.visibleVariant(device))
                        || originals.find(variant => variant !== 'None')
                        || deviceVariants(device).find(variant => variant !== 'None') || 'None';
                    pair[key] = `real:${fallback}`;
                    if (key === side) selected = fallback;
                    setStatus(`${device.name}: ${error.message}`, true);
                }
            }
            const current = selections[device.name] ?? deviceRouting.visibleVariant(device);
            if (device.isReady !== false && (deviceRouting.participates(device, 'variant')
                || !deviceVariants(device).includes(current)) && selected) changes[device.name] = selected;
        }
        savePairs();
        mutationRevision++;
        await deviceRouting.setVariants(changes);
    }

    async function refreshDeviceState({ render = true, force = false, syncVideo = true } = {}) {
        if (editingCount && !force) return;
        const requestRevision = ++refreshRevision;
        const mutationAtStart = mutationRevision;
        const response = await fetch('/Devices');
        if (!response.ok) throw new Error(await response.text() || response.statusText);
        const refreshed = await response.json();
        if (requestRevision !== refreshRevision || mutationAtStart !== mutationRevision) return;
        devices = refreshed;
        assetsNeedDeviceRefresh = false;
        await deviceRouting.setDevices(devices);
        if (syncVideo && videoContext) await reconcileVideoPairs();
        devices.forEach(pairFor);
        const preserveEditor = variantSelectFocused();
        if (!preserveEditor) refreshGlobalOptions();
        if (render && !preserveEditor) renderDevices();
        else {
            rangePainters.forEach(paint => paint());
            renderFeatureState();
        }
    }

    function currentVariantStates() {
        return devices.map(device => {
            const settings = pairFor(device);
            const primary = physicalSelection(settings.primary, settings.bases?.primary);
            const secondary = physicalSelection(settings.secondary, settings.bases?.secondary);
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
                    try {
                        const selected = await resolveSelection(pair[targetSide], snapshot, targetSide);
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
                snapshots.forEach(device => { pairFor(device).activeSide = targetSide; });
                savePairs();
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
        else rangePainters.forEach(paint => paint());
    });

    video?.addEventListener('mousedown', event => {
        if (event.button !== 2 || !globalSettings.enabled) return;
        event.preventDefault();
        switchVariantSide(activeSide() === 'primary' ? 'secondary' : 'primary');
    });
    video?.addEventListener('contextmenu', event => event.preventDefault());
    document.addEventListener('edi-assets-cached', event => {
        videoAssets = event.detail?.files || [];
        assetsNeedDeviceRefresh = true;
    });
    document.addEventListener('edi-devices-refresh-requested', () => {
        void refreshDeviceState().catch(error => setStatus(`Could not refresh devices: ${error.message}`, true));
    });

    const initialRefresh = refreshDeviceState();
    deviceRouting.setVideoHandler(async context => {
        await initialRefresh;
        switchQueue = switchQueue.catch(() => {}).then(async () => {
            editingCount++;
            try {
                const previous = { ...storedPairs };
                const selections = Object.fromEntries(devices.map(device => [device.name, deviceRouting.visibleVariant(device)]));
                if (context) {
                    const prepared = await assetManager.prepareVariant({ videoName: context.name });
                    videoAssets = await fetchAssets(context.name);
                    if (prepared.changed) assetsReloadPending = true;
                }
                if (assetsNeedDeviceRefresh) await refreshDeviceState({ render: false, force: true, syncVideo: false });
                videoContext = context;
                if (context) await reconcileVideoPairs(previous, selections);
                refreshGlobalOptions();
                renderDevices();
                publishAssetsReload();
            } finally { editingCount--; }
        });
        return switchQueue;
    });
    try {
        await initialRefresh;
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
