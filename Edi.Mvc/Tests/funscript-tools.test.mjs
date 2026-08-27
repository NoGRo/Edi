import test from 'node:test';
import assert from 'node:assert/strict';
import {
    autoVariantName, buildRelevantDeviceCache, conceptualSelectionForPhysical,
    generatedFunscriptName, getDoubleSpeedScript, getHalfSpeedScript,
    isAutoVariant, listOriginalVariants, parseFunscriptName, planDeviceRange,
    reorderValues, swapPairValues, switchRelevantDeviceCache
} from '../wwwroot/js/funscript-tools.mjs';

test('Auto Double preserves order, input, extrema, and has no synthetic +10ms point', () => {
    const actions = [{ at: 300, pos: 100 }, { at: 0, pos: 0 }, { at: 100, pos: 100 }, { at: 200, pos: 0 }];
    const original = structuredClone(actions);
    const result = getDoubleSpeedScript({ actions }, {});
    assert.deepEqual(actions, original);
    assert.deepEqual(result.actions.map(action => action.at), [...result.actions.map(action => action.at)].sort((a, b) => a - b));
    assert.equal(result.actions.some((action, index, all) => index && action.at - all[index - 1].at === 10 && action.pos === all[index - 1].pos), false);
    assert.deepEqual(result.actions.map(({ at, pos }) => ({ at, pos })), [
        { at: 0, pos: 0 }, { at: 50, pos: 100 }, { at: 100, pos: 0 },
        { at: 150, pos: 100 }, { at: 200, pos: 0 }, { at: 250, pos: 100 }, { at: 300, pos: 0 }
    ]);
});

test('empty and single-action transforms are defensive', () => {
    assert.deepEqual(getDoubleSpeedScript({ actions: [] }, {}).actions, []);
    assert.deepEqual(getHalfSpeedScript({ actions: [{ at: 1, pos: 2 }] }, {}).actions, [{ at: 1, pos: 2 }]);
});

test('generated names are stable, hidden-detectable, and preserve axes', () => {
    const physical = autoVariantName('double');
    assert.equal(physical, '__auto_double');
    assert.equal(isAutoVariant(physical), true);
    assert.deepEqual(parseFunscriptName('scene.Fast.Sway.funscript'), { name: 'scene', variant: 'Fast', axis: 'Sway' });
    assert.equal(generatedFunscriptName('scene.Fast.Sway.funscript', physical), `scene.${physical}.Sway.funscript`);
});

test('relevant cache excludes other variants and equal selections', () => {
    const devices = [
        { name: 'A', selectedVariant: 'Normal' }, { name: 'B', selectedVariant: 'Fast' },
        { name: 'C', selectedVariant: 'Slow' }
    ];
    assert.deepEqual(buildRelevantDeviceCache(devices, 'Normal', 'Fast').map(device => device.name), ['A', 'B']);
    assert.deepEqual(buildRelevantDeviceCache(devices, 'Normal', 'Normal'), []);
});

test('automatic variants are stable and hidden from originals', () => {
    const physical = autoVariantName('double');
    assert.deepEqual(listOriginalVariants(['Normal', 'None', physical, 'Fast']), ['Normal', 'None', 'Fast']);
    assert.equal(conceptualSelectionForPhysical(physical), 'auto:double');
});

test('repeated switches update and reuse cached device state', async () => {
    const cache = buildRelevantDeviceCache([
        { name: 'A', selectedVariant: 'Normal' }, { name: 'B', selectedVariant: 'Auto' },
        { name: 'Ignored', selectedVariant: 'Slow' }
    ], 'Normal', 'Auto');
    const calls = [];
    const apply = async (name, variant) => calls.push([name, variant]);
    assert.deepEqual(await switchRelevantDeviceCache(cache, 'Normal', 'Auto', apply), []);
    assert.deepEqual(await switchRelevantDeviceCache(cache, 'Normal', 'Auto', apply), []);
    assert.deepEqual(calls, [
        ['A', 'Auto'], ['B', 'Normal'], ['A', 'Normal'], ['B', 'Auto']
    ]);
});

test('a paused device keeps range changes deferred at zero until resume', () => {
    assert.deepEqual(planDeviceRange(true, 20, 80),
        { deferred: true, min: 0, max: 0 });
    assert.deepEqual(planDeviceRange(false, 20, 80),
        { deferred: false, min: 20, max: 80 });
});

test('device order can move before or after another device', () => {
    assert.deepEqual(reorderValues(['A', 'B', 'C'], 'C', 'A'), ['C', 'A', 'B']);
    assert.deepEqual(reorderValues(['A', 'B', 'C'], 'A', 'B', true), ['B', 'A', 'C']);
});

test('primary and secondary selections can be swapped without changing their values', () => {
    assert.deepEqual(swapPairValues('real:Default', 'real:Halved'), {
        primary: 'real:Halved', secondary: 'real:Default'
    });
});
