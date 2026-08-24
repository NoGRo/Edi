document.addEventListener('DOMContentLoaded', async () => {
    const {
        autoVariantName, buildRelevantDeviceCache, generatedFunscriptName,
        getDoubleSpeedScript, getHalfSpeedScript, isAutoVariant, listOriginalVariants,
        parseAutoVariant, parseFunscriptName, switchRelevantDeviceCache
    } = await import('/js/funscript-tools.mjs');

    const grid = document.getElementById('devicesGrid');
    const video = document.getElementById('videoPlayer');
    const toggle = document.getElementById('variantToggle');
    const panel = document.getElementById('variantTogglePanel');
    const closePanel = document.getElementById('closeVariantPanel');
    const primarySelect = document.getElementById('primaryVariant');
    const secondarySelect = document.getElementById('secondaryVariant');
    const toggleStatus = document.getElementById('variantToggleStatus');
    const storedSelectionsKey = 'edi-player-variant-toggle';
    const generationLocks = new Map();
    const generatedBaseCache = new Map();
    let generationQueue = Promise.resolve();
    let panelHideTimer = null;
    let editingCount = 0;
    let devices = [];
    let originalVariants = [];
    let relevantDevices = [];
    let resolvedPrimary = null;
    let resolvedSecondary = null;
    let selectionRevision = 0;
    let switchQueue = Promise.resolve();
    let featureRequested = false;
    let resolvingSelections = false;
    let deviceRefreshPending = false;
    let assetsReloadRevision = 0;
    let notifiedAssetsReloadRevision = 0;

    const post = async (path, options = {}) => {
        const response = await fetch(path, { method: 'POST', ...options });
        if (!response.ok) throw new Error(await response.text() || response.statusText);
        return response;
    };

    const readSelections = () => {
        try { return JSON.parse(localStorage.getItem(storedSelectionsKey)) || {}; }
        catch { return {}; }
    };

    const saveSelections = () => {
        const stored = readSelections();
        const devicesExposeVariants = physicalVariants().length > 0;
        localStorage.setItem(storedSelectionsKey, JSON.stringify({
            primary: primarySelect.value || (!devicesExposeVariants ? stored.primary : ''),
            secondary: secondarySelect.value || (!devicesExposeVariants ? stored.secondary : ''),
            enabled: featureRequested
        }));
    };

    const physicalVariants = () => [...new Set(devices.flatMap(device => device.variants || []))];
    const realSelection = value => value?.startsWith('real:') ? value.slice(5) : null;
    const autoSelection = value => value?.startsWith('auto:') ? value.slice(5) : null;
    const selectionAvailable = value => {
        const original = realSelection(value);
        if (original) return originalVariants.includes(original);
        return ['double', 'halve'].includes(autoSelection(value));
    };

    function setStatus(message, error = false) {
        toggleStatus.textContent = message;
        toggleStatus.classList.toggle('text-danger', error);
        toggleStatus.classList.toggle('text-muted', !error);
    }

    function renderFeatureState(active) {
        toggle.setAttribute('aria-pressed', String(active));
        toggle.classList.toggle('btn-primary', active);
        toggle.classList.toggle('btn-outline-secondary', !active);
    }

    function publishVariantState(detail = null) {
        document.dispatchEvent(new CustomEvent('edi-variant-state', { detail }));
    }

    function setFeatureEnabled(enabled) {
        featureRequested = enabled;
        relevantDevices = [];
        renderFeatureState(false);
        saveSelections();
        if (!enabled) rebuildRelevantCache();
    }

    function setPanelOpen(open) {
        toggle.setAttribute('aria-expanded', String(open));
        panel.hidden = !open;
        document.dispatchEvent(new CustomEvent('edi-variant-panel', { detail: { open } }));
        if (panelHideTimer) clearTimeout(panelHideTimer);
        panelHideTimer = open ? setTimeout(() => setPanelOpen(false), 8000) : null;
    }

    function keepPanelOpen() {
        if (!panel.hidden) setPanelOpen(true);
    }

    function variantOptionLabel(variant) {
        const generated = parseAutoVariant(variant);
        if (!generated) return variant;
        return generated.kind === 'double' ? 'Auto Double' : 'Auto Halve';
    }

    function selectionLabel(value) {
        const original = realSelection(value);
        if (original) return original;
        return autoSelection(value) === 'double' ? 'Auto Double' : 'Auto Halve';
    }

    function appendToggleOptions(select, selectedValue) {
        select.replaceChildren();
        select.append(new Option('Select a variant', ''));
        const originals = document.createElement('optgroup');
        originals.label = 'Original variants';
        originalVariants.forEach(variant => originals.append(new Option(variant, `real:${variant}`)));
        select.append(originals);
        const separator = new Option('────────────', 'separator');
        separator.disabled = true;
        select.append(separator);
        const automatic = document.createElement('optgroup');
        automatic.label = 'Automatic variants';
        automatic.append(new Option('Auto Double', 'auto:double'), new Option('Auto Halve', 'auto:halve'));
        select.append(automatic);
        select.value = [...select.options].some(option => option.value === selectedValue) ? selectedValue : '';
    }

    function inferSelections() {
        const stored = readSelections();
        let primary = selectionAvailable(stored.primary) ? stored.primary : '';
        let secondary = selectionAvailable(stored.secondary) ? stored.secondary : '';
        const selectedPhysical = devices.map(device => device.selectedVariant).filter(Boolean);
        const selectedOriginal = selectedPhysical.find(variant => originalVariants.includes(variant));
        const selectedAuto = selectedPhysical.map(parseAutoVariant).find(Boolean);
        const namedDefault = originalVariants.find(variant => variant.toLowerCase() === 'default');
        const defaultOriginal = namedDefault
            || selectedOriginal
            || originalVariants[0];
        if (!primary && defaultOriginal) primary = `real:${defaultOriginal}`;
        if (secondary === primary) secondary = '';
        const selectedAutoValue = selectedAuto ? `auto:${selectedAuto.kind}` : '';
        if (!secondary && selectedAutoValue !== primary) secondary = selectedAutoValue;
        if (!secondary) {
            const primaryOriginal = realSelection(primary);
            const alternateOriginal = originalVariants.find(variant => variant !== primaryOriginal);
            if (alternateOriginal) secondary = `real:${alternateOriginal}`;
        }
        const defaultSelection = namedDefault ? `real:${namedDefault}` : null;
        if (defaultSelection && secondary === defaultSelection && primary !== defaultSelection) {
            [primary, secondary] = [secondary, primary];
        }
        return { primary, secondary, enabled: stored.enabled === true };
    }

    function refreshToggleOptions(firstLoad = false) {
        const previousPrimary = primarySelect.value;
        const previousSecondary = secondarySelect.value;
        originalVariants = listOriginalVariants(physicalVariants());
        const inferred = inferSelections();
        appendToggleOptions(primarySelect, selectionAvailable(previousPrimary) ? previousPrimary : inferred.primary);
        appendToggleOptions(secondarySelect, selectionAvailable(previousSecondary) ? previousSecondary : inferred.secondary);
        if (firstLoad) featureRequested = inferred.enabled;
    }

    function resolveBase(selection, otherSelection) {
        const otherReal = realSelection(otherSelection);
        if (otherReal) return otherReal;
        return originalVariants[0] || null;
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

    async function generateVariant(kind, baseVariant, physicalVariant) {
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
            const retainedAssets = assets.filter(file => {
                if (!file.name.toLowerCase().endsWith('.funscript')) return true;
                return parseAutoVariant(parseFunscriptName(file.name).variant)?.kind !== kind;
            });
            const merged = new Map(retainedAssets.map(file => [file.name.toLowerCase(), file]));
            generated.forEach(file => merged.set(file.name.toLowerCase(), file));
            const form = new FormData();
            [...merged.values()].forEach(file => form.append('files', file, file.name));
            await post('/Edi/Assets', { body: form });
            assetsReloadRevision++;
            await refreshDeviceState({ render: true, firstLoad: false, resolve: false });
            if (!physicalVariants().includes(physicalVariant)) {
                throw new Error('EDI did not expose the generated variant after upload.');
            }
            generatedBaseCache.set(kind, baseVariant);
            setStatus('Automatic variant ready.');
            return physicalVariant;
        }).finally(() => generationLocks.delete(physicalVariant));
        generationLocks.set(physicalVariant, generation);
        generationQueue = generation.catch(() => {});
        return generation;
    }

    async function getGeneratedBase(kind) {
        if (generatedBaseCache.has(kind)) return generatedBaseCache.get(kind);
        const response = await fetch('/Edi/Assets');
        if (!response.ok) throw new Error(await response.text() || response.statusText);
        const paths = await response.json();
        const path = paths.find(value => {
            if (typeof value !== 'string' || !value.toLowerCase().endsWith('.funscript')) return false;
            const name = decodeURIComponent(value.split('/').pop() || '');
            return parseAutoVariant(parseFunscriptName(name).variant)?.kind === kind;
        });
        if (!path) return null;
        const scriptResponse = await fetch(path);
        if (!scriptResponse.ok) return null;
        const script = await scriptResponse.json();
        const base = script.metadata?.ediAutoVariant?.baseVariant || null;
        generatedBaseCache.set(kind, base);
        return base;
    }

    async function resolveSelection(selection, otherSelection, allowGeneration) {
        const original = realSelection(selection);
        if (original) return original;
        const kind = autoSelection(selection);
        if (!kind) return null;
        const base = resolveBase(selection, otherSelection);
        if (!base) throw new Error('An original variant is required as the automatic variant source.');
        const physical = autoVariantName(kind);
        if (!allowGeneration && !physicalVariants().includes(physical)) return null;
        if (!allowGeneration) return physical;
        const hasLegacyVariant = physicalVariants().some(variant => {
            const automatic = parseAutoVariant(variant);
            return automatic?.kind === kind && variant !== physical;
        });
        const compatibleBase = physicalVariants().includes(physical) ? await getGeneratedBase(kind) : null;
        return compatibleBase === base && !hasLegacyVariant
            ? physical
            : generateVariant(kind, base, physical);
    }

    function rebuildRelevantCache() {
        const validSelections = Boolean(resolvedPrimary && resolvedSecondary && resolvedPrimary !== resolvedSecondary);
        const compatibleDevices = validSelections ? devices.filter(device => device.isReady !== false
            && (device.variants || []).includes(resolvedPrimary)
            && (device.variants || []).includes(resolvedSecondary)) : [];
        const matches = validSelections
            ? buildRelevantDeviceCache(compatibleDevices, resolvedPrimary, resolvedSecondary)
            : [];
        const active = featureRequested && matches.length > 0;
        relevantDevices = active ? matches : [];
        renderFeatureState(active);
        const selectedVariant = active ? matches[0]?.selectedVariant : null;
        const side = selectedVariant === resolvedPrimary
            ? 'P'
            : selectedVariant === resolvedSecondary ? 'S' : null;
        publishVariantState(side ? {
                side,
                variant: selectionLabel(side === 'P' ? primarySelect.value : secondarySelect.value)
            } : null);
        if (!toggleStatus.classList.contains('text-danger') && !featureRequested) {
            setStatus('Variant switching is off.');
        } else if (!toggleStatus.classList.contains('text-danger') && !validSelections) {
            setStatus('Choose two different variants to enable right-click switching.');
        } else if (!toggleStatus.classList.contains('text-danger') && !matches.length) {
            setStatus('No connected device can switch between the selected variants.');
        } else if (!toggleStatus.classList.contains('text-danger')) {
            setStatus(`${matches.length} matching device${matches.length === 1 ? '' : 's'} ready.`);
        }
        return active;
    }

    async function moveDevicesToReplacement(previousVariant, nextVariant) {
        if (!previousVariant || !nextVariant || previousVariant === nextVariant) return;
        await switchQueue;
        const targets = devices.filter(device => device.selectedVariant === previousVariant);
        if (!targets.length) return;
        const unavailable = targets.filter(device => device.isReady === false
            || !(device.variants || []).includes(nextVariant));
        if (unavailable.length) {
            throw new Error(`${unavailable.length} device${unavailable.length === 1 ? '' : 's'} cannot use the replacement variant.`);
        }
        setStatus(`Updating ${targets.length} device${targets.length === 1 ? '' : 's'} to the replacement variant…`);
        const results = await Promise.allSettled(targets.map(async device => {
            await post(`/Devices/${encodeURIComponent(device.name)}/Variant/${encodeURIComponent(nextVariant)}`);
            device.selectedVariant = nextVariant;
        }));
        await refreshDeviceState({ render: true, firstLoad: false, resolve: false });
        const failures = results.filter(result => result.status === 'rejected');
        if (failures.length) {
            throw new Error(`${failures.length} device${failures.length === 1 ? '' : 's'} could not be updated to the replacement variant.`);
        }
    }

    async function resolveSelections(allowGeneration, changedSide = null, previousVariant = null) {
        if (resolvingSelections) return;
        resolvingSelections = true;
        const revision = ++selectionRevision;
        resolvedPrimary = resolvedSecondary = null;
        relevantDevices = [];
        renderFeatureState(false);
        publishVariantState();
        primarySelect.disabled = secondarySelect.disabled = true;
        setStatus('Resolving variants…');
        try {
            const [primary, secondary] = await Promise.all([
                resolveSelection(primarySelect.value, secondarySelect.value, allowGeneration),
                resolveSelection(secondarySelect.value, primarySelect.value, allowGeneration)
            ]);
            if (revision !== selectionRevision) return;
            resolvedPrimary = primary;
            resolvedSecondary = secondary;
            if (primary && secondary && primary === secondary) {
                resolvedPrimary = resolvedSecondary = null;
                setStatus('Primary and Secondary must resolve to different variants.', true);
            } else {
                const replacement = changedSide === 'primary' ? primary : changedSide === 'secondary' ? secondary : null;
                await moveDevicesToReplacement(previousVariant, replacement);
                if (revision !== selectionRevision) return;
                setStatus('');
                rebuildRelevantCache();
            }
            saveSelections();
        } catch (error) {
            if (revision !== selectionRevision) return;
            resolvedPrimary = resolvedSecondary = null;
            setStatus(`Could not activate variant switching: ${error.message}`, true);
            saveSelections();
            rebuildRelevantCache();
        } finally {
            if (assetsReloadRevision > notifiedAssetsReloadRevision) {
                notifiedAssetsReloadRevision = assetsReloadRevision;
                document.dispatchEvent(new CustomEvent('edi-assets-reloaded'));
            }
            if (revision === selectionRevision) {
                primarySelect.disabled = secondarySelect.disabled = false;
                resolvingSelections = false;
                if (deviceRefreshPending) requestDeviceRefresh();
            }
        }
    }

    function field(labelText, control) {
        const wrapper = document.createElement('div');
        wrapper.className = 'col-md-4';
        const label = document.createElement('label');
        label.className = 'form-label';
        label.textContent = labelText;
        wrapper.append(label, control);
        return wrapper;
    }

    function renderDevice(device) {
        const card = document.createElement('div');
        card.className = 'device-card';
        const heading = document.createElement('div');
        heading.className = 'device-meta-line';
        const name = document.createElement('strong');
        name.className = 'device-meta-name';
        name.textContent = device.name;
        const badge = document.createElement('span');
        badge.className = `badge ${device.isReady ? 'bg-success' : 'bg-secondary'}`;
        badge.textContent = device.isReady ? 'Connected' : 'Unavailable';
        const variant = document.createElement('select');
        variant.className = 'form-select form-select-sm';
        variant.setAttribute('aria-label', `Variant for ${device.name}`);
        for (const value of device.variants || []) {
            variant.append(new Option(variantOptionLabel(value), value, false, value === device.selectedVariant));
        }
        variant.disabled = !variant.options.length;
        variant.addEventListener('change', async () => {
            editingCount++;
            try {
                await post(`/Devices/${encodeURIComponent(device.name)}/Variant/${encodeURIComponent(variant.value)}`);
                device.selectedVariant = variant.value;
                rebuildRelevantCache();
            } catch (error) {
                setStatus(`Could not change ${device.name}: ${error.message}`, true);
            } finally { editingCount--; }
        });

        const values = [device.min, device.max]
            .map(value => Math.min(100, Math.max(0, Number(value))))
            .sort((left, right) => left - right);
        let initialLow = Number.isFinite(values[0]) ? values[0] : 0;
        let initialHigh = Number.isFinite(values[1]) ? values[1] : 100;
        if (initialLow === initialHigh) {
            if (initialHigh < 100) initialHigh += 1;
            else initialLow -= 1;
        }

        const min = document.createElement('input');
        min.className = 'device-range-input';
        min.type = 'range'; min.min = 0; min.max = 100; min.step = 1; min.value = initialLow;
        min.setAttribute('aria-label', `Lower limit for ${device.name}`);
        const max = document.createElement('input');
        max.className = 'device-range-input';
        max.type = 'range'; max.min = 0; max.max = 100; max.step = 1; max.value = initialHigh;
        max.setAttribute('aria-label', `Upper limit for ${device.name}`);
        const minLabel = document.createElement('span');
        const maxLabel = document.createElement('span');
        const range = document.createElement('div');
        range.className = 'device-range';
        const track = document.createElement('div');
        track.className = 'device-range-track';
        const rangeValues = document.createElement('div');
        rangeValues.className = 'device-range-values';

        const renderRange = changedInput => {
            let low = Number(min.value), high = Number(max.value);
            if (changedInput === min && low >= high) low = high - 1;
            if (changedInput === max && high <= low) high = low + 1;
            min.value = low; max.value = high;
            minLabel.textContent = `Min ${low}%`;
            maxLabel.textContent = `Max ${high}%`;
            range.style.setProperty('--range-low', `${low}%`);
            range.style.setProperty('--range-high', `${high}%`);
            min.style.zIndex = changedInput === min ? 3 : 2;
            max.style.zIndex = changedInput === max ? 3 : 2;
        };
        const applyRange = async () => {
            editingCount++;
            try { await post(`/Devices/${encodeURIComponent(device.name)}/Range/${min.value}-${max.value}`); }
            finally { editingCount--; }
        };
        min.addEventListener('input', () => renderRange(min));
        max.addEventListener('input', () => renderRange(max));
        min.addEventListener('change', applyRange);
        max.addEventListener('change', applyRange);
        rangeValues.append(minLabel, maxLabel);
        range.append(track, min, max);
        const rangeControls = document.createElement('div');
        rangeControls.className = 'flex-grow-1';
        rangeControls.append(rangeValues, range);
        const rangeBox = document.createElement('div');
        rangeBox.className = 'd-flex align-items-end gap-2';
        rangeBox.append(rangeControls);
        const variantBox = document.createElement('div');
        variantBox.className = 'device-meta-variant';
        variantBox.append(variant);
        heading.append(name, variantBox, badge);
        const rangeField = field('Range', rangeBox);
        rangeField.className = 'device-range-field';
        renderRange();
        card.append(heading, rangeField);
        return card;
    }

    async function refreshDeviceState({ render = true, firstLoad = false, resolve = true } = {}) {
        if (editingCount) return;
        const response = await fetch('/Devices');
        if (!response.ok) throw new Error(await response.text() || response.statusText);
        devices = await response.json();
        if (render) {
            grid.replaceChildren();
            if (!devices.length) {
                const empty = document.createElement('div');
                empty.className = 'alert alert-secondary';
                empty.textContent = 'No devices detected.';
                grid.append(empty);
            } else devices.forEach(device => grid.append(renderDevice(device)));
        }
        refreshToggleOptions(firstLoad);
        if (resolve) await resolveSelections(featureRequested);
        else rebuildRelevantCache();
    }

    async function loadDevices(firstLoad = false) {
        if (resolvingSelections) return;
        try { await refreshDeviceState({ render: true, firstLoad }); }
        catch (error) {
            relevantDevices = [];
            renderFeatureState(false);
            publishVariantState();
            setStatus(`Could not validate variant switching: ${error.message}`, true);
            const message = document.createElement('div');
            message.className = 'alert alert-danger';
            message.textContent = `Could not load devices: ${error.message}`;
            grid.replaceChildren(message);
        }
    }

    function requestDeviceRefresh() {
        deviceRefreshPending = true;
        if (resolvingSelections) return;
        deviceRefreshPending = false;
        void loadDevices(false);
    }

        toggle.addEventListener('click', () => {
            if (!featureRequested) {
                setFeatureEnabled(true);
                setPanelOpen(true);
                void resolveSelections(true);
            } else if (panel.hidden) {
                setPanelOpen(true);
            } else {
                setFeatureEnabled(false);
                setPanelOpen(false);
                toggle.blur();
            }
        });
    closePanel.addEventListener('click', event => {
        event.stopPropagation();
        closePanel.blur();
        setPanelOpen(false);
    });
    panel.addEventListener('pointerdown', keepPanelOpen);
    panel.addEventListener('input', keepPanelOpen);
    panel.addEventListener('focusin', keepPanelOpen);
    document.addEventListener('pointerdown', event => {
        if (!panel.hidden && !panel.contains(event.target) && !toggle.contains(event.target)) setPanelOpen(false);
    }, true);
    document.addEventListener('keydown', event => {
        if (event.key === 'Escape' && !panel.hidden) setPanelOpen(false);
    });
    primarySelect.addEventListener('change', () => resolveSelections(true, 'primary', resolvedPrimary));
    secondarySelect.addEventListener('change', () => resolveSelections(true, 'secondary', resolvedSecondary));
    video?.addEventListener('mousedown', event => {
        if (event.button !== 2) return;
        if (!rebuildRelevantCache() || !relevantDevices.length) return;
        event.preventDefault();
        const activatesSecondary = relevantDevices[0].selectedVariant === resolvedPrimary;
        document.dispatchEvent(new CustomEvent('edi-variant-switched', {
            detail: {
                side: activatesSecondary ? 'S' : 'P',
                variant: selectionLabel(activatesSecondary ? secondarySelect.value : primarySelect.value)
            }
        }));
        switchQueue = switchQueue.then(async () => {
            const targets = relevantDevices.map(device => ({ ...device }));
            const failures = await switchRelevantDeviceCache(
                targets, resolvedPrimary, resolvedSecondary, async (name, next) => {
                    await post(`/Devices/${encodeURIComponent(name)}/Variant/${encodeURIComponent(next)}`);
                    const cached = devices.find(device => device.name === name);
                    if (cached) cached.selectedVariant = next;
                });
            if (failures.length) {
                setFeatureEnabled(false);
                setStatus(`${failures.length} device switch${failures.length === 1 ? '' : 'es'} failed.`, true);
            } else rebuildRelevantCache();
        }).catch(error => {
            setFeatureEnabled(false);
            setStatus(`Could not toggle variants: ${error.message}`, true);
        });
    });
    video?.addEventListener('contextmenu', event => {
        event.preventDefault();
    });
    document.addEventListener('edi-devices-refresh-requested', requestDeviceRefresh);

    await loadDevices(true);
    setInterval(loadDevices, 10000);
});
