import test from 'node:test';
import assert from 'node:assert/strict';
import { createServer } from 'node:http';
import { readFile } from 'node:fs/promises';
import { createRequire } from 'node:module';
import { fileURLToPath } from 'node:url';
import { resolve, extname, sep } from 'node:path';

// Optional integration suite: requires Playwright and an installed Edge browser.
const { chromium } = createRequire(import.meta.url)('playwright');
const root = fileURLToPath(new URL('../../wwwroot/', import.meta.url));

test('browser loads all modules, edits the playlist and restores the persistent asset library', async () => {
    const server = createServer(async (request, response) => {
        const pathname = new URL(request.url, 'http://localhost').pathname;
        const path = resolve(root, pathname === '/' ? 'index.html' : `.${pathname}`);
        if (!path.startsWith(root.endsWith(sep) ? root : root + sep)) {
            response.writeHead(403).end();
            return;
        }
        try {
            const mime = { '.js': 'text/javascript', '.mjs': 'text/javascript', '.css': 'text/css', '.html': 'text/html' };
            response.writeHead(200, { 'Content-Type': mime[extname(path)] || 'application/octet-stream' });
            response.end(await readFile(path));
        } catch {
            response.writeHead(404).end();
        }
    });
    await new Promise(done => server.listen(0, '127.0.0.1', done));
    let browser;
    try {
        browser = await chromium.launch({ channel: 'msedge', headless: true });
        const page = await browser.newPage();
        const errors = [];
        page.on('pageerror', error => { errors.push(error.message); console.error(error.stack); });
        const definitions = [{ name: 'scene', fileName: 'scene.funscript', startTime: 0, endTime: 10000 }];
        const commands = [];
        await page.route('**/Edi/**', route => {
            commands.push([route.request().method(), new URL(route.request().url()).pathname]);
            return route.fulfill({ json: definitions });
        });
        await page.route('**/Devices', route => route.fulfill({ json: [] }));
        await page.goto(`http://127.0.0.1:${server.address().port}/`);
        await page.waitForFunction(() => document.getElementById('videoPlaylist').textContent.includes('You have not added videos yet.'));
        if (await page.locator('#fileDrop').evaluate(panel => panel.hidden)) {
            await page.locator('#playlistPanelToggle').click();
        }
        await page.locator('#mediaFiles').setInputFiles([
            { name: 'scene.mp4', mimeType: 'video/mp4', buffer: Buffer.from('fixture') },
            { name: 'scene.funscript', mimeType: 'application/json', buffer: Buffer.from('{"actions":[]}') }
        ]);
        await page.waitForFunction(() => window.ediPlayerAssetCache?.some(file => file.name === 'scene.funscript'));
        await page.waitForFunction(async () => {
            const storage = await import('/js/player/asset-storage.mjs');
            return (await storage.getStoredItems('assets')).length === 1;
        });
        assert.equal(await page.locator('#playlistCount').textContent(), '1 video');
        assert.equal(await page.locator('#playlistDurationTotal').textContent(), '0:10');
        assert.equal(await page.locator('#videoPlayer').evaluate(video => video.controls), false);
        await page.locator('#customProgressMode').evaluate(button => button.click());
        assert.equal(await page.locator('#customProgressMode').getAttribute('data-mode'), 'video');
        await page.locator('#loopToggle').click();
        assert.equal(await page.locator('#loopModeIndicator').textContent(), '1');
        await page.locator('#videoStage').evaluate(stage => stage.requestFullscreen());
        assert.equal(await page.locator('#playbackToolbar').evaluate(toolbar => toolbar.parentElement.id), 'fullscreenPlaybackOverlay');
        await page.evaluate(() => document.exitFullscreen());
        await page.waitForFunction(() => document.getElementById('playbackToolbar').parentElement.id !== 'fullscreenPlaybackOverlay');
        await page.reload();
        await page.waitForFunction(() => window.ediPlayerAssetCache?.length === 1);
        await page.waitForFunction(() => document.getElementById('playlistCount').textContent === '0 videos');
        assert.equal(await page.locator('#loopModeIndicator').textContent(), '1');
        assert.equal(await page.locator('#customProgressMode').getAttribute('data-mode'), 'video');
        if (await page.locator('#fileDrop').evaluate(panel => panel.hidden)) {
            await page.locator('#playlistPanelToggle').click();
        }
        page.on('dialog', dialog => dialog.accept());
        await page.locator('#clearPlaylist').click();
        await page.waitForFunction(() => window.ediPlayerAssetCache?.length === 0);
        assert.ok(commands.some(([method, path]) => method === 'POST' && path === '/Edi/Assets'));
        assert.ok(commands.some(([method, path]) => method === 'DELETE' && path === '/Edi/Assets'));
        assert.deepEqual(errors, []);
    } finally {
        await browser?.close();
        await new Promise(done => server.close(done));
    }
});
