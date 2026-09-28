import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';

const markup = readFileSync(new URL('../wwwroot/index.html', import.meta.url), 'utf8');
const player = ['input', 'fullscreen'].map(name =>
    readFileSync(new URL(`../wwwroot/js/player/${name}.mjs`, import.meta.url), 'utf8')).join('\n');
const assets = readFileSync(new URL('../wwwroot/js/player/assets.mjs', import.meta.url), 'utf8');
const bootstrap = readFileSync(new URL('../wwwroot/js/edi-player.js', import.meta.url), 'utf8');
const devices = readFileSync(new URL('../wwwroot/js/edi-devices.js', import.meta.url), 'utf8').replace(/\r\n/g, '\n');
const routing = readFileSync(new URL('../wwwroot/js/player/device-routing.mjs', import.meta.url), 'utf8');

const occurrences = value => markup.split(value).length - 1;

test('playlist and devices use one shared panel in windowed and fullscreen modes', () => {
    assert.equal(occurrences('id="videoPlaylist"'), 1);
    assert.equal(occurrences('id="devicesGrid"'), 1);
    assert.equal(occurrences('id="fullscreenVideoPlaylist"'), 0);
    assert.equal(occurrences('id="fullscreenDevicesGrid"'), 0);
    assert.match(markup, /id="playlistPanelToggle"[^>]+aria-controls="fileDrop"/s);
    assert.match(markup, /id="devicesPanelToggle"[^>]+aria-controls="devicesPanel"/s);
});

test('file drag reveals and paints the shared playlist panel', () => {
    assert.match(player, /window\.addEventListener\('dragenter'/);
    assert.match(player, /window\.addEventListener\('dragover'/);
    assert.match(player, /window\.addEventListener\('drop'/);
    assert.match(player, /dragenter[\s\S]+setSidePanelOpen\(dropZone, playlistPanelToggle, true\)[\s\S]+dropZone\.classList\.add\('drag-over'\)/);
});

test('file selection supports multiple files and complete folders through the same loader', () => {
    assert.match(markup, /id="mediaFiles"[^>]+multiple/s);
    assert.match(markup, /id="mediaFolder"[^>]+multiple[^>]+webkitdirectory/s);
    assert.match(player, /folderInput\.addEventListener\('change'[\s\S]+addFiles\(folderInput\.files\)/);
});

test('fullscreen inactivity pauses while the pointer is over controls', () => {
    assert.match(player, /if \(overControls\) \{\s*__cursorHideTimer = null;\s*return;/);
    assert.match(player, /__cursorHideTimer = setTimeout\(\(\) => \{\s*blurFullscreenToolbarFocus\(\)/);
    assert.match(player, /hideControlsNow\(true\);\s*hidePlayerOverlay\(true\)/);
    assert.doesNotMatch(markup, /fullscreen-playback-overlay:focus-within/);
    assert.doesNotMatch(markup, /fullscreen-playback-overlay:hover/);
});

test('empty stage fills the available viewport and panels cannot scroll sideways', () => {
    assert.match(markup, /\.video-stage\.video-empty \{[^}]+height: calc\(100dvh - 8rem\)/);
    assert.match(markup, /\.video-side-panel \{[^}]+overflow-x: hidden/);
    assert.match(markup, /\.playlist-total \{[^}]+flex-wrap: wrap[^}]+overflow-x: hidden/);
});

test('fullscreen side panels share toolbar visibility and device focus uses the shared grid', () => {
    assert.match(markup, /fullscreen-playback-overlay\.visible ~ \.video-side-panel/);
    assert.doesNotMatch(devices, /\bgrids\b/);
});

test('variant stop feedback is calculated locally without a cache-sensitive module export', () => {
    assert.match(markup, /edi-devices\.js\?v=20260928-device-routing/);
    assert.doesNotMatch(devices, /getVariantStopState/);
    assert.match(devices, /const connected = devices\.filter\(device => device\.isReady !== false\)/);
    assert.match(devices, /const allStopped = active && connected\.length > 0 && stoppedCount === connected\.length/);
    assert.match(devices, /variant-toggle-partially-stopped/);
});

test('the middle button swaps only its device pair and retains column highlighting', () => {
    assert.match(devices, /const primaryActive = deviceSide\(device\) === 'primary'/);
    assert.match(devices, /const secondaryActive = deviceSide\(device\) === 'secondary'/);
    assert.match(devices, /primary: pair\.secondary, secondary: pair\.primary/);
    assert.match(devices, /deviceRouting\.setVariants\(\{ \[device\.name\]: selected \}\)/);
});

test('a variant mode switch updates devices as one batch while global switches stay queued', () => {
    assert.match(devices, /switchQueue = switchQueue\.then\(async \(\) =>/);
    assert.match(devices, /deviceRouting\.setVariants\(selections\)/);
    assert.match(routing, /request\('\/Devices\/Variants\?persist=false'/);
    assert.match(routing, /body: JSON\.stringify\(changes\)/);
    assert.doesNotMatch(devices, /for \(const snapshot of \[\.\.\.devices\]\)/);
});

test('a variant mode switch prepares missing selections in parallel, then batches activation', () => {
    const switchBody = devices.match(/function switchVariantSide\(targetSide\) \{([\s\S]+?)\n    \}\n\n    document\.addEventListener/)[1];
    assert.match(switchBody, /await Promise\.all\(snapshots\.map\(async snapshot =>/);
    assert.match(switchBody, /await resolveSelection\(/);
    assert.doesNotMatch(switchBody, /resolvePair|generateVariant|fetchAssets|\/Edi\/Assets/);
    assert.doesNotMatch(switchBody, /publishAssetsReload|edi-assets-reloaded|Intensity/);
});

test('saved real variants resolve to the backend canonical casing', () => {
    assert.match(devices, /const findDeviceVariant = \(device, variant\) => deviceVariants\(device\)/);
    assert.match(devices, /return findDeviceVariant\([\s\S]+?, physical\)/);
});

test('asset manager owns automatic generation and incremental uploads', () => {
    assert.match(devices, /assetManager\.prepareVariant\(/);
    assert.doesNotMatch(devices, /getDoubleSpeedScript|generatedFunscriptName/);
    assert.match(assets, /generated\.forEach\(file => form\.append\('files', file, file\.name\)\)/);
    assert.match(assets, /request\('\/Edi\/Assets', \{ method: 'PUT', body: form \}\)/);
    assert.match(assets, /uploadGenerated\(additions, \[\.\.\.merged\.values\(\)\]\)/);
});

test('uploaded assets are shared with variant generation as an in-memory cache', () => {
    assert.match(markup, /type="module" src="\/js\/edi-player\.js\?v=20260928-immersive-player/);
    assert.match(assets, /window\.ediPlayerAssetCache = \[\.\.\.files\]/);
    assert.match(bootstrap, /await assets\.restore\(\)/);
    assert.match(assets, /document\.addEventListener\('edi-assets-persist-requested'/);
    assert.match(devices, /assetManager\.fetchForVariants\(videoName\)/);
    assert.match(assets, /new CustomEvent\('edi-assets-persist-requested'/);
});
