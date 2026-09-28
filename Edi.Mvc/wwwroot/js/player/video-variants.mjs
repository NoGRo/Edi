import { conceptualSelectionForPhysical, parseAutoVariant } from '../funscript-tools.mjs';
import { assetVariantsForVideo } from './assets.mjs';

export function variantsForVideo(device, context, assets = []) {
    if (!context) return (device.variants || []).filter(variant => variant === 'None');
    const available = new Set(assetVariantsForVideo(assets, context.name).map(variant => variant.toLowerCase()));
    return (device.variants || []).filter(variant => variant === 'None' || available.has(variant.toLowerCase()));
}

export function videoVariantPair({ history, previous = {}, variants, selected, defaults = {}, side = 'primary' }) {
    const normalize = value => {
        if (value?.startsWith('real:')) {
            const variant = variants.find(variant => variant.toLowerCase() === value.slice(5).toLowerCase());
            return variant ? `real:${variant}` : null;
        }
        return ['auto:double', 'auto:halve'].includes(value)
            && (variants.some(variant => parseAutoVariant(variant)?.kind === value.slice(5))
                || variants.some(variant => variant !== 'None' && !parseAutoVariant(variant))) ? value : null;
    };
    const current = conceptualSelectionForPhysical(selected);
    const first = variants.find(value => value !== 'None' && value === defaults.defaultVariant)
        || variants.find(value => value !== 'None') || 'None';
    const fallback = `real:${first}`;
    const pair = { activeSide: history?.activeSide || side,
        bases: { ...previous.bases, ...history?.bases } };
    const automatic = parseAutoVariant(selected);
    if (!history?.[pair.activeSide] && automatic?.legacyBase) {
        try { pair.bases[pair.activeSide] = decodeURIComponent(automatic.legacyBase); } catch {}
    }
    for (const key of ['primary', 'secondary']) {
        pair[key] = [history?.[key], key === pair.activeSide ? current : null, previous[key],
            defaults[key], fallback].map(normalize).find(Boolean) || 'real:None';
    }
    // An explicit stop survives navigation, even when this video has an older active preference.
    if (selected === 'None') pair[pair.activeSide] = 'real:None';
    return pair;
}
