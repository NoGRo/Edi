const AUTO_PREFIX = '__auto_';
const AUTO_KINDS = new Set(['double', 'halve']);
const AXES = new Set([
    'default', 'surge', 'sway', 'twist', 'roll', 'pitch', 'vibrate',
    'valve', 'suction', 'rotate', 'frequency', 'volume', 'pulsewidth'
]);

const sign = value => value === 0 ? 0 : value > 0 ? 1 : -1;
const roundAction = action => ({ ...action, at: Math.round(action.at), pos: Math.round(action.pos) });

// The supplied transforms depend on getActionGroups. A long gap is a real pause and starts a
// new cadence group; the actions themselves are copied so callers' scripts are never mutated.
export function getActionGroups(actions, pauseDuration = 2000) {
    if (!actions.length) return [];
    const groups = [[{ ...actions[0] }]];
    for (let index = 1; index < actions.length; index++) {
        const action = actions[index];
        const previous = actions[index - 1];
        if (action.at - previous.at > pauseDuration) groups.push([]);
        groups[groups.length - 1].push({ ...action });
    }
    return groups;
}

export function getDoubleSpeedGroup(actionGroup, options = {}) {
    if (actionGroup.length <= 1) return [...actionGroup];
    const shortPauseDuration = options.shortPauseDuration ?? 100;
    const noShortPauses = actionGroup.filter((action, index) =>
        index === 0 || action.pos !== actionGroup[index - 1].pos
        || Math.abs(action.at - actionGroup[index - 1].at) > shortPauseDuration);
    if (noShortPauses.length <= 1) return [...noShortPauses];

    const simplifiedGroup = noShortPauses.filter((action, index, actions) => {
        if (index === 0 || index === actions.length - 1) return true;
        return sign(action.pos - actions[index - 1].pos)
            !== sign(actions[index + 1].pos - action.pos);
    });
    if (simplifiedGroup.length <= 1) return [...simplifiedGroup];

    let currentPos = simplifiedGroup[0].pos;
    const finalGroup = [simplifiedGroup[0]];
    for (let index = 0; index < simplifiedGroup.length - 1; index++) {
        const current = simplifiedGroup[index];
        const next = simplifiedGroup[index + 1];
        if (next.pos === current.pos) {
            finalGroup.push({ ...next, pos: currentPos });
            continue;
        }
        if (index === simplifiedGroup.length - 2 && options.matchGroupEnd) {
            finalGroup.push({ ...next });
            currentPos = next.pos;
            continue;
        }
        const min = Math.min(current.pos, next.pos);
        const max = Math.max(current.pos, next.pos);
        const halfPos = Math.abs(currentPos - min) > Math.abs(currentPos - max) ? min : max;
        const endPos = halfPos === min ? max : min;
        finalGroup.push({ at: current.at + (next.at - current.at) / 2, pos: halfPos });
        finalGroup.push({ at: next.at, pos: endPos });
        currentPos = endPos;
        // Intentionally no synthetic same-position point at next.at + 10ms.
    }
    return finalGroup;
}

export function getDoubleSpeedScript(script, options = {}) {
    if (script.actions.length <= 1) return { ...script, actions: [...script.actions] };
    const output = { ...script, actions: [] };
    const orderedActions = [...script.actions].sort((left, right) => left.at - right.at);
    const longFirstWait = orderedActions[1].at - orderedActions[0].at > 5000;
    if (longFirstWait) output.actions.push(orderedActions[0]);
    const groups = getActionGroups(longFirstWait ? orderedActions.slice(1) : orderedActions);
    output.actions.push(...groups.flatMap(group => getDoubleSpeedGroup(group, options)));
    if (!output.actions.length) return output;
    const sourceEnd = orderedActions[orderedActions.length - 1].at;
    const outputEnd = output.actions[output.actions.length - 1];
    if (outputEnd.at !== sourceEnd) output.actions.push({ at: sourceEnd, pos: outputEnd.pos });
    output.actions = output.actions.map(roundAction);
    return output;
}

export function getHalfSpeedGroup(actionGroup, options = {}) {
    if (actionGroup.length <= 1) return [...actionGroup];
    const keyActions = [];
    let apexCount = 0;
    let filteredGroup = actionGroup.filter((action, index) => {
        if (index === 0 || index === actionGroup.length - 1) return true;
        return !(action.pos === actionGroup[index - 1].pos && action.pos === actionGroup[index + 1].pos);
    });
    if (options.removeShortPauses && filteredGroup.length > 1) {
        const nextGroup = [];
        const pauseTime = options.shortPauseDuration > 0 ? options.shortPauseDuration : 2000;
        filteredGroup.forEach((action, index) => {
            if (index === 0 || index === filteredGroup.length - 1) {
                nextGroup.push(action);
                return;
            }
            const previous = filteredGroup[index - 1];
            const next = filteredGroup[index + 1];
            if (action.pos === previous.pos && Math.abs(action.at - previous.at) < pauseTime) {
                nextGroup.push({ at: (action.at + previous.at) * .5, pos: action.pos });
                return;
            }
            if (action.pos !== next.pos || Math.abs(action.at - next.at) >= pauseTime) nextGroup.push(action);
        });
        filteredGroup = nextGroup;
    }
    if (filteredGroup.length <= 1) return [...filteredGroup];

    filteredGroup.forEach((action, index) => {
        if (index === 0 || index === filteredGroup.length - 1) {
            keyActions.push({ ...action, subActions: [], type: index === 0 ? 'first' : 'last' });
            return;
        }
        const previous = filteredGroup[index - 1];
        const next = filteredGroup[index + 1];
        if (action.pos === previous.pos || action.pos === next.pos) {
            keyActions.push({ ...action, subActions: [], type: action.pos === previous.pos ? 'pause' : 'prepause' });
            apexCount = 0;
            return;
        }
        if (options.matchFirstDownstroke && index === 1 && action.pos < previous.pos) apexCount = 1;
        if (sign(action.pos - previous.pos) !== sign(next.pos - action.pos)) {
            if (apexCount === 0) {
                const lastKey = keyActions[keyActions.length - 1];
                lastKey.subActions = [...(lastKey.subActions || []), action];
                apexCount++;
                return;
            }
            keyActions.push({ ...action, subActions: [], type: 'apex' });
            apexCount = 0;
            return;
        }
        const lastKey = keyActions[keyActions.length - 1];
        lastKey.subActions = [...(lastKey.subActions || []), action];
    });

    if (!keyActions.length) return [];
    let pos = options.resetAfterPause ? 100 : keyActions[0].pos;
    const finalActions = keyActions.map((action, index) => {
        if (index === 0) return { at: action.at, pos };
        const previous = keyActions[index - 1];
        if (action.type === 'pause') pos = action.pos;
        else if (previous.subActions?.length) {
            const positions = [...previous.subActions.map(item => item.pos), action.pos];
            const min = Math.min(...positions), max = Math.max(...positions);
            pos = Math.abs(pos - min) > Math.abs(pos - max) ? min : max;
        } else pos = action.pos;
        return { at: action.at, pos };
    });
    if (options.matchGroupEndPosition && finalActions.length) {
        const finalAction = finalActions[finalActions.length - 1];
        const sourceFinal = actionGroup[actionGroup.length - 1];
        if (finalAction.pos !== sourceFinal.pos) {
            const previous = finalActions[finalActions.length - 2];
            finalActions.push({ at: finalAction.at + (previous ? finalAction.at - previous.at : 0), pos: sourceFinal.pos });
        }
    }
    return finalActions.map(roundAction);
}

export function getHalfSpeedScript(script, options = {}) {
    if (script.actions.length <= 1) return { ...script, actions: [...script.actions] };
    const output = { ...script, actions: [] };
    const orderedActions = [...script.actions].sort((left, right) => left.at - right.at);
    const longFirstWait = orderedActions[1].at - orderedActions[0].at > 5000;
    if (longFirstWait) output.actions.push(orderedActions[0]);
    const groups = getActionGroups(longFirstWait ? orderedActions.slice(1) : orderedActions);
    output.actions.push(...groups.flatMap(group => getHalfSpeedGroup(group, options)));
    if (!output.actions.length) return output;
    const sourceEnd = orderedActions[orderedActions.length - 1].at;
    const outputEnd = output.actions[output.actions.length - 1];
    if (outputEnd.at !== sourceEnd) output.actions.push({ at: sourceEnd, pos: outputEnd.pos });
    output.actions = output.actions.map(roundAction);
    return output;
}

export function autoVariantName(kind, baseVariant) {
    if (!AUTO_KINDS.has(kind)) throw new Error(`Unknown automatic variant: ${kind}`);
    return `${AUTO_PREFIX}${kind}${baseVariant ? `_${encodeURIComponent(baseVariant.toLowerCase()).replace(/\./g, '%2E')}` : ''}`;
}

export function parseAutoVariant(value) {
    const match = /^__auto_(double|halve)(?:_(.+))?$/i.exec(value || '');
    return match ? { kind: match[1].toLowerCase(), legacyBase: match[2] || null } : null;
}

export function isAutoVariant(value) { return Boolean(parseAutoVariant(value)); }

export function listOriginalVariants(variants) {
    return [...new Set(variants)].filter(variant => !isAutoVariant(variant));
}

export function conceptualSelectionForPhysical(variant) {
    const automatic = parseAutoVariant(variant);
    return automatic ? `auto:${automatic.kind}` : variant ? `real:${variant}` : '';
}

const isConceptualSelection = value => typeof value === 'string'
    && (/^real:.+/.test(value) || /^auto:(double|halve)$/.test(value));

export function initializeDeviceVariantPair(existing, variants, selectedVariant, defaults = {}) {
    const stored = existing && typeof existing === 'object' ? existing : {};
    const available = [...new Set((variants || []).filter(Boolean))];
    const availableSelection = value => {
        if (!isConceptualSelection(value)) return false;
        if (value.startsWith('auto:')) return true;
        return available.includes(value.slice(5));
    };
    const first = (selectedVariant !== 'None' && available.includes(selectedVariant) ? selectedVariant : '')
        || available.find(value => value !== 'None')
        || available[0]
        || '';

    // A stored choice is intentionally preserved even while the device stops advertising it.
    // Refreshes describe availability; only an explicit user edit owns these preferences.
    const primary = isConceptualSelection(stored.primary)
        ? stored.primary
        : availableSelection(defaults.primary) ? defaults.primary
            : first ? `real:${first}` : '';
    const secondary = isConceptualSelection(stored.secondary)
        ? stored.secondary
        : availableSelection(defaults.secondary)
            ? defaults.secondary
            : first ? `real:${first}` : '';

    return { ...stored, primary, secondary };
}

export function changeDeviceVariantPair(pair, side, value) {
    if (!['primary', 'secondary'].includes(side)) throw new Error(`Unknown variant side: ${side}`);
    if (!isConceptualSelection(value)) throw new Error('A variant must be selected.');
    return { ...pair, [side]: value };
}

export function parseFunscriptName(fileName) {
    const stem = fileName.replace(/\.funscript$/i, '');
    const parts = stem.split('.');
    const axis = parts.length > 1 && AXES.has(parts[parts.length - 1].toLowerCase()) ? parts.pop() : null;
    const variant = parts.length > 1 ? parts.pop() : 'default';
    return { name: parts.join('.'), variant, axis };
}

export function generatedFunscriptName(fileName, physicalVariant) {
    const parsed = parseFunscriptName(fileName);
    return [parsed.name, physicalVariant, parsed.axis].filter(Boolean).join('.') + '.funscript';
}

export function buildRelevantDeviceCache(devices, primary, secondary) {
    if (!primary || !secondary || primary === secondary) return [];
    return devices.filter(device => device.selectedVariant === primary || device.selectedVariant === secondary)
        .map(device => ({ name: device.name, selectedVariant: device.selectedVariant }));
}

export async function switchRelevantDeviceCache(cache, primary, secondary, apply) {
    const results = await Promise.allSettled(cache.map(async device => {
        const next = device.selectedVariant === primary ? secondary : primary;
        await apply(device.name, next);
        device.selectedVariant = next;
    }));
    return results.filter(result => result.status === 'rejected');
}
