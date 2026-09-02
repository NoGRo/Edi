import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';

const markup = readFileSync(new URL('../wwwroot/index.html', import.meta.url), 'utf8');
const player = readFileSync(new URL('../wwwroot/js/edi-player.js', import.meta.url), 'utf8');
const devices = readFileSync(new URL('../wwwroot/js/edi-devices.js', import.meta.url), 'utf8');

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
    assert.match(markup, /edi-devices\.js\?v=20260901-variant-stop/);
    assert.doesNotMatch(devices, /getVariantStopState/);
    assert.match(devices, /const connected = devices\.filter\(device => device\.isReady !== false\)/);
    assert.match(devices, /const allStopped = active && connected\.length > 0 && stoppedCount === connected\.length/);
    assert.match(devices, /variant-toggle-partially-stopped/);
});
