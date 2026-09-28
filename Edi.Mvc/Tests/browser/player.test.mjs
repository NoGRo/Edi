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

test('device buttons route controls selectively and keep hidden pause out of variant selections', async () => {
    const server = await staticServer();
    let browser;
    try {
        browser = await chromium.launch({ channel: 'msedge', headless: true });
        const page = await browser.newPage();
        const errors = [], calls = [];
        page.on('pageerror', error => errors.push(error.message));
        const devices = ['First', 'Second'].map(name => ({ name, isReady: true,
            variants: ['primary', 'secondary', 'None'], selectedVariant: 'primary',
            min: 0, max: 100, baseMin: 0, baseMax: 100
        }));
        await page.route('**/Edi/**', route => route.fulfill({ json: [] }));
        await page.route('**/Devices**', route => {
            const request = route.request();
            const url = new URL(request.url());
            if (request.method() === 'GET') return route.fulfill({ json: devices });
            const body = request.postDataJSON();
            calls.push({ path: url.pathname + url.search, body });
            if (url.pathname === '/Devices/Variants') {
                devices.forEach(device => {
                    if (body[device.name]) device.selectedVariant = body[device.name];
                });
            }
            return route.fulfill({ status: 200, body: '' });
        });
        await page.goto(`http://127.0.0.1:${server.address().port}/`);
        await page.waitForFunction(() => document.querySelectorAll('.device-control-buttons button').length === 6);
        await page.evaluate(async () => {
            const { assets } = await import('/js/player/assets.mjs');
            await assets.upload(['primary', 'secondary'].map(variant => new File([JSON.stringify({
                actions: [{ at: 0, pos: 0 }, { at: 1000, pos: 100 }]
            })], `scene.${variant}.funscript`)));
            await (await import('/js/player/device-routing.mjs')).deviceRouting.setVideoContext({
                name: 'scene.mp4', definitions: [{ name: 'scene', fileName: 'scene.funscript' }]
            });
        });
        const first = page.locator('.device-card').nth(0), second = page.locator('.device-card').nth(1);
        await second.locator('select').nth(1).selectOption('real:secondary');
        const { heading, buttons } = await first.evaluate(card => ({
            heading: card.querySelector('.device-meta-line').getBoundingClientRect().toJSON(),
            buttons: card.querySelector('.device-control-buttons').getBoundingClientRect().toJSON()
        }));
        assert.ok(Math.abs(buttons.x + buttons.width - heading.x - heading.width) < 2);
        assert.ok(Math.abs(buttons.y + buttons.height / 2 - heading.y - heading.height / 2) < 2);

        await first.locator('[data-control=variant]').click();
        calls.length = 0;
        await first.locator('select').first().selectOption('real:secondary');
        await first.locator('select').nth(1).selectOption('real:primary');
        assert.deepEqual(calls, []);
        assert.equal(await first.locator('select').first().isEnabled(), true);
        assert.equal(await first.locator('select').nth(1).isEnabled(), true);
        await first.locator('select').first().selectOption('real:primary');
        await first.locator('select').nth(1).selectOption('real:secondary');
        await second.locator('[data-control=intensity]').click();
        calls.length = 0;
        await page.evaluate(async () => (await import('/js/player/device-routing.mjs')).deviceRouting.setIntensity(40));
        assert.deepEqual(calls.map(call => call.path), ['/Devices/First/Range/0-40?persist=false']);

        await second.locator('[data-control=pause]').click();
        calls.length = 0;
        await page.evaluate(async () => (await import('/js/player/device-routing.mjs')).deviceRouting.setPaused(true));
        assert.deepEqual(calls, [{ path: '/Devices/Variants?persist=false', body: { First: 'None' } }]);
        assert.equal(await first.locator('[data-control=pause]').evaluate(button => button.classList.contains('btn-danger')), true);
        assert.equal(await first.locator('select').first().inputValue(), 'real:primary');
        assert.equal(await first.locator('select').first().evaluate(select => select.classList.contains('device-variant-stopped')), false);

        calls.length = 0;
        await first.locator('.device-variant-switch').click();
        assert.equal(await first.locator('select').first().inputValue(), 'real:secondary');
        assert.equal(await first.locator('select').nth(1).inputValue(), 'real:primary');
        assert.deepEqual(calls, []);
        await first.locator('.device-variant-switch').click();
        await page.locator('#variantToggle').click();
        await page.locator('#videoPlayer').dispatchEvent('mousedown', { button: 2 });
        await page.waitForFunction(() => document.querySelectorAll('.device-variant-active')[1]?.value === 'real:secondary');
        assert.deepEqual(calls, [{ path: '/Devices/Variants?persist=false', body: { Second: 'secondary' } }]);
        assert.equal(await first.locator('.device-variant-active').inputValue(), 'real:primary');
        calls.length = 0;
        await page.evaluate(async () => (await import('/js/player/device-routing.mjs')).deviceRouting.setPaused(false));
        assert.deepEqual(calls, [{ path: '/Devices/Variants?persist=false', body: { First: 'primary' } }]);
        calls.length = 0;
        await first.locator('[data-control=variant]').click();
        await page.waitForFunction(() => document.querySelectorAll('.device-variant-active')[0]?.value === 'real:secondary');
        assert.deepEqual(calls, [{ path: '/Devices/Variants?persist=false', body: { First: 'secondary' } }]);
        assert.deepEqual(errors, []);
    } finally {
        await browser?.close();
        await new Promise(done => server.close(done));
    }
});

async function staticServer() {
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
    return server;
}

test('fresh file drop uploads first, shows every video duration and plays a real video', async () => {
    const server = await staticServer();
    let browser;
    try {
        browser = await chromium.launch({ channel: 'msedge', headless: true });
        const page = await browser.newPage(), calls = [], errors = [];
        page.on('pageerror', error => errors.push(error.message));
        let definitions = [];
        const device = { name: 'Preview', isReady: true, variants: ['default', 'None'],
            selectedVariant: 'default', min: 0, max: 100, baseMin: 0, baseMax: 100 };
        await page.route('**/Devices**', route => route.fulfill({ json: [device] }));
        await page.route('**/Edi/**', route => {
            const request = route.request(), path = new URL(request.url()).pathname;
            calls.push([request.method(), path]);
            if (path === '/Edi/Assets' && request.method() === 'GET')
                return route.fulfill({ json: ['/Edi/Assets/other-game.funscript'] });
            if (path === '/Edi/Assets' && request.method() === 'POST') {
                definitions = [{ name: 'one', fileName: 'one.funscript', startTime: 0, endTime: 2000 },
                    { name: 'two', fileName: 'two.funscript', startTime: 0, endTime: 5000 }];
            }
            return route.fulfill({ json: definitions });
        });
        await page.goto(`http://127.0.0.1:${server.address().port}/`);
        await page.waitForFunction(() => document.querySelector('.device-card'));
        const video = await readFile(new URL('./drop-video.webm', import.meta.url));
        await page.evaluate(encoded => {
            const data = Uint8Array.from(atob(encoded), character => character.charCodeAt(0));
            const transfer = new DataTransfer();
            for (const [name, duration] of [['one', 2000], ['two', 5000]]) {
                transfer.items.add(new File([data], `${name}.webm`, { type: 'video/webm' }));
                transfer.items.add(new File([JSON.stringify({ actions: [{ at: 0, pos: 0 }, { at: duration, pos: 100 }] })],
                    `${name}.funscript`, { type: 'application/json' }));
            }
            window.dispatchEvent(new DragEvent('drop', { bubbles: true, cancelable: true, dataTransfer: transfer }));
        }, video.toString('base64'));
        await page.waitForFunction(() => document.querySelector('#videoPlayer').readyState >= 2
            && document.querySelector('.playlist-item.active') && document.querySelector('#playlistDurationTotal').textContent === '0:07');
        assert.deepEqual(await page.locator('.playlist-item .playlist-duration').allTextContents(), ['0:02', '0:05']);
        assert.equal(await page.locator('#customDuration').textContent(), '0:02');
        assert.equal(calls.some(([method, path]) => method === 'POST' && path === '/Edi/Assets'), true);
        assert.equal(calls.some(([method, path]) => method === 'GET' && path.startsWith('/Edi/Assets')), false);
        const stage = await page.locator('#videoStage').boundingBox();
        await page.mouse.move(stage.x + stage.width / 2, stage.y + stage.height - 12);
        await page.locator('#customPlayPause').click();
        await page.waitForFunction(() => !document.querySelector('#videoPlayer').paused
            && document.querySelector('#videoPlayer').currentTime > 0);
        await page.evaluate(() => document.querySelector('#videoPlayer').pause());
        await page.locator('.playlist-name').filter({ hasText: 'two.webm' }).click();
        await page.waitForFunction(() => document.querySelector('.playlist-item.active .playlist-name')?.textContent === 'two.webm');
        assert.deepEqual(await page.locator('.playlist-item .playlist-duration').allTextContents(), ['0:02', '0:05']);
        assert.deepEqual(errors, []);
    } finally {
        await browser?.close();
        await new Promise(done => server.close(done));
    }
});

test('video variants prepare before activation, remember base choices, retain stops and reorder devices', async () => {
    const server = await staticServer();
    let browser;
    try {
        browser = await chromium.launch({ channel: 'msedge', headless: true });
        const page = await browser.newPage(), calls = [], errors = [];
        page.on('pageerror', error => errors.push(error.message));
        const definitions = ['one', 'two', 'three'].map(name => ({ name,
            fileName: `${name}.funscript`, startTime: 0, endTime: 1000 }));
        const devices = [{ name: 'First', selectedVariant: 'fast' }, { name: 'Second', selectedVariant: 'None' }]
            .map(device => ({ ...device, isReady: true, variants: ['fast', 'slow', 'default', 'unrelated', 'None'],
                min: 0, max: 100, baseMin: 0, baseMax: 100 }));
        let serverNames = [];
        await page.route('**/Edi/**', route => {
            const request = route.request(), url = new URL(request.url());
            if (url.pathname === '/Edi/Definitions') return route.fulfill({ json: definitions });
            if (url.pathname === '/Edi/Assets') {
                if (request.method() === 'GET') return route.fulfill({ json: serverNames.map(name => `/Edi/Upload/${name}`) });
                const names = [...request.postData().matchAll(/filename="([^"]+)"/g)].map(match => match[1]);
                calls.push({ method: request.method(), path: url.pathname, names });
                serverNames = request.method() === 'POST' ? names : [...new Set([...serverNames, ...names])];
                for (const name of names) {
                    const match = name.match(/\.(__auto_[^.]+)\.funscript$/);
                    if (match) devices.forEach(device => {
                        if (!device.variants.includes(match[1])) device.variants.push(match[1]);
                    });
                }
                return route.fulfill({ json: definitions });
            }
            return route.fulfill({ status: 200, body: '' });
        });
        await page.route('**/Devices**', route => {
            const request = route.request(), url = new URL(request.url());
            if (request.method() === 'GET') return route.fulfill({ json: devices });
            const body = request.postDataJSON();
            calls.push({ path: url.pathname, body });
            devices.forEach(device => { if (body[device.name]) device.selectedVariant = body[device.name]; });
            return route.fulfill({ status: 200, body: '' });
        });
        await page.goto(`http://127.0.0.1:${server.address().port}/`);
        await page.waitForFunction(() => document.querySelectorAll('.device-card').length === 2);
        await page.evaluate(async () => {
            const { assets } = await import('/js/player/assets.mjs');
            const names = ['one.fast.funscript', 'one.slow.funscript', 'two.fast.funscript',
                'two.slow.funscript', 'three.default.funscript', 'other.unrelated.funscript'];
            await assets.upload(names.map(name => new File([JSON.stringify({
                actions: [{ at: 0, pos: 0 }, { at: 1000, pos: 100 }]
            })], name)));
        });
        await page.locator('#mediaFiles').setInputFiles(['one', 'two', 'three'].map(name => ({
            name: `${name}.mp4`, mimeType: 'video/mp4', buffer: Buffer.from('fixture')
        })));
        const context = async name => {
            await page.locator('.playlist-name').filter({ hasText: `${name}.mp4` }).click();
            await page.waitForFunction(expected => document.querySelector('.playlist-item.active .playlist-name')?.textContent === expected,
                `${name}.mp4`);
            // The active row changes before asynchronous variant preparation.
            // Wait for this video's originals before asserting its fallback.
            await page.waitForFunction(expected => document.querySelector(`[data-device-name=First] optgroup option[value="${expected}"]`),
                name === 'three' ? 'real:default' : 'real:fast');
        };
        await page.waitForFunction(() => document.querySelector('.playlist-item.active .playlist-name')?.textContent === 'one.mp4');
        await context('one');
        const first = page.locator('[data-device-name=First]'), second = page.locator('[data-device-name=Second]');
        assert.equal(await first.locator('select').first().inputValue(), 'real:fast');
        assert.equal(await second.locator('select').first().inputValue(), 'real:None');
        assert.deepEqual(await first.locator('select').first().locator('optgroup').first().locator('option')
            .evaluateAll(options => options.map(option => option.value)), ['real:fast', 'real:slow', 'real:None']);
        await first.locator('select').nth(1).selectOption('real:slow');
        calls.length = 0;
        await first.locator('.device-variant-switch').click();
        await page.waitForFunction(() => document.querySelector('[data-device-name=First] select').value === 'real:slow');
        assert.deepEqual(calls, [{ path: '/Devices/Variants', body: { First: 'slow' } }]);
        await first.locator('.device-variant-switch').click();
        await page.waitForFunction(() => document.querySelector('[data-device-name=First] select').value === 'real:fast');
        calls.length = 0;
        await first.locator('select').nth(1).selectOption('auto:double');
        await page.getByRole('dialog').waitFor();
        assert.deepEqual(await page.getByLabel('Original base variant').locator('option')
            .evaluateAll(options => options.map(option => option.value)), ['fast', 'slow']);
        await page.getByRole('button', { name: 'Cancel', exact: true }).click();
        await page.waitForFunction(() => !document.querySelector('dialog')
            && document.querySelectorAll('[data-device-name=First] select')[1].value === 'real:slow');
        await first.locator('select').nth(1).selectOption('auto:double');
        await page.getByLabel('Original base variant').selectOption('slow');
        await page.getByRole('button', { name: 'Generate', exact: true }).click();
        await page.waitForFunction(() => JSON.parse(localStorage.getItem('edi-player-video-variant-pairs'))
            ['one.mp4']?.First?.bases?.secondary === 'slow' && !document.querySelector('dialog'));
        await page.waitForFunction(() => document.querySelector('[data-device-name=First] option[value="real:__auto_double_slow"]'));
        assert.equal(calls.filter(call => call.method === 'PUT').length, 1);
        assert.deepEqual(calls.find(call => call.method === 'PUT').names, ['one.__auto_double_slow.funscript']);
        calls.length = 0;
        await context('two');
        assert.equal(await page.locator('dialog').count(), 0);
        assert.equal(await first.locator('select').first().inputValue(), 'real:fast');
        assert.equal(await second.locator('select').first().inputValue(), 'real:None');
        assert.deepEqual(calls.filter(call => call.method === 'PUT').map(call => call.names), [['two.__auto_double_slow.funscript']]);
        assert.equal(calls.filter(call => call.path === '/Devices/Variants').length, 0);
        calls.length = 0;
        await context('one');
        assert.deepEqual(calls, []);
        await context('three');
        assert.equal(await first.locator('select').first().inputValue(), 'real:default');
        assert.equal(await second.locator('select').first().inputValue(), 'real:None');
        await page.evaluate(() => {
            const handle = document.querySelector('[data-device-name=First] .device-drag-handle');
            const transfer = new DataTransfer();
            handle.dispatchEvent(new DragEvent('dragstart', { bubbles: true, dataTransfer: transfer }));
            document.querySelector('[data-device-name=Second]').dispatchEvent(new DragEvent('drop', { bubbles: true, dataTransfer: transfer }));
            handle.dispatchEvent(new DragEvent('dragend', { bubbles: true, dataTransfer: transfer }));
        });
        assert.deepEqual(await page.locator('.device-meta-name').allTextContents(), ['Second', 'First']);
        await page.reload();
        await page.waitForFunction(() => document.querySelectorAll('.device-card').length === 2);
        assert.deepEqual(await page.locator('.device-meta-name').allTextContents(), ['Second', 'First']);
        assert.deepEqual(errors, []);
    } finally {
        await browser?.close();
        await new Promise(done => server.close(done));
    }
});

test('one device bar shows configured and effective ranges and saves its player-only collapse point', async () => {
    const server = await staticServer();
    let browser;
    try {
        browser = await chromium.launch({ channel: 'msedge', headless: true });
        const page = await browser.newPage();
        const errors = [], calls = [];
        page.on('pageerror', error => errors.push(error.message));
        const device = { name: 'Preview', isReady: true, variants: ['primary', 'None'],
            selectedVariant: 'primary', min: 0, max: 100, baseMin: 0, baseMax: 100 };
        await page.route('**/Edi/**', route => route.fulfill({ json: [] }));
        await page.route('**/Devices**', route => {
            const request = route.request(), url = new URL(request.url());
            if (request.method() === 'GET') return route.fulfill({ json: [device] });
            calls.push(url.pathname + url.search);
            const range = url.pathname.match(/\/Range\/(\d+)-(\d+)/);
            if (range) {
                device.min = Number(range[1]); device.max = Number(range[2]);
                if (!url.searchParams.has('persist')) {
                    device.baseMin = device.min; device.baseMax = device.max;
                }
            }
            return route.fulfill({ status: 200, body: '' });
        });
        await page.goto(`http://127.0.0.1:${server.address().port}/`);
        const center = page.getByRole('slider', { name: 'Intensity collapse point for Preview' });
        await center.waitFor();
        assert.equal(await center.inputValue(), '0');
        await center.focus();
        await center.press('End');
        await page.waitForFunction(() => JSON.parse(localStorage.getItem('edi-player-device-controls')).Preview.rangeCenter === 100);
        await center.fill('80');
        await center.dispatchEvent('input');
        await center.dispatchEvent('change');
        await page.waitForFunction(() => JSON.parse(localStorage.getItem('edi-player-device-controls')).Preview.rangeCenter === 80);
        await page.evaluate(async () => (await import('/js/player/device-routing.mjs')).deviceRouting.setIntensity(50));
        const tips = page.locator('.device-range-tooltip');
        assert.equal(await page.locator('.device-range-values').count(), 0);
        assert.equal(await tips.count(), 1);
        assert.equal(await page.locator('.device-range').evaluate(element => element.style.getPropertyValue('--effective-low')), '40%');
        assert.equal(await tips.first().isVisible(), false);
        if (await page.locator('#fileDrop').evaluate(panel => panel.hidden)) await page.locator('#playlistPanelToggle').click();
        const before = await page.locator('.device-card').boundingBox();
        const bar = await page.locator('.device-range').boundingBox();
        await page.mouse.move(bar.x + 10, bar.y + bar.height / 2);
        assert.equal(await tips.first().isVisible(), true);
        assert.equal(await tips.textContent(), 'Min 0% · Max 100%\nEffective 40–90%');
        const after = await page.locator('.device-card').boundingBox();
        assert.equal(after.height, before.height);
        const tooltipBox = await tips.boundingBox();
        assert.ok(tooltipBox.y >= bar.y + bar.height);
        const centerBox = await center.boundingBox();
        await page.mouse.move(centerBox.x + 10 + (centerBox.width - 20) * .8, centerBox.y + centerBox.height / 2);
        assert.equal(await tips.textContent(), 'Center 80%');
        await page.mouse.move(bar.x + bar.width - 10, bar.y + bar.height / 2);
        assert.equal(await tips.textContent(), 'Min 0% · Max 100%\nEffective 40–90%');
        await page.mouse.move(0, 0);
        assert.equal(await tips.first().isVisible(), false);
        assert.equal(await page.locator('.device-range').count(), 1);
        assert.equal(await page.locator('.device-range-effective').evaluate(element => getComputedStyle(element).backgroundImage.includes('110, 168, 254')), true);
        await page.evaluate(async () => (await import('/js/player/device-routing.mjs')).deviceRouting.setIntensity(100));
        await page.mouse.move(bar.x + 10, bar.y + bar.height / 2);
        assert.equal(await tips.textContent(), 'Min 0% · Max 100%');
        await page.mouse.move(0, 0);
        await page.evaluate(async () => (await import('/js/player/device-routing.mjs')).deviceRouting.setIntensity(0));
        assert.ok(calls.includes('/Devices/Preview/Range/80-80?persist=false'));
        const min = page.getByRole('slider', { name: 'Minimum range for Preview' });
        const reapplied = page.waitForResponse(response => response.url().endsWith('/Range/80-80?persist=false'));
        await min.fill('20');
        await min.dispatchEvent('input');
        await min.dispatchEvent('change');
        await reapplied;
        await page.waitForFunction(() => document.querySelector('input[aria-label="Minimum range for Preview"]').value === '20');
        assert.equal(device.min, 80);
        assert.equal(device.max, 80);
        await page.evaluate(async () => (await import('/js/player/device-routing.mjs')).deviceRouting.setIntensity(50));
        await page.reload();
        await center.waitFor();
        assert.equal(await center.inputValue(), '80');
        assert.equal(await page.locator('.device-range').evaluate(element => element.style.getPropertyValue('--effective-low')), '50%');
        assert.ok(calls.every(path => !/Center|Middle/.test(path)));
        assert.deepEqual(errors, []);
        if (process.env.EDI_RANGE_SCREENSHOT) {
            if (await page.locator('#fileDrop').evaluate(panel => panel.hidden)) await page.locator('#playlistPanelToggle').click();
            const currentBar = await page.locator('.device-range').boundingBox();
            await page.mouse.move(currentBar.x + 10 + (currentBar.width - 20) * .2, currentBar.y + currentBar.height / 2);
            await page.screenshot({ path: process.env.EDI_RANGE_SCREENSHOT });
        }
    } finally {
        await browser?.close();
        await new Promise(done => server.close(done));
    }
});

test('browser loads all modules, edits the playlist and restores the persistent asset library', async () => {
    const server = await staticServer();
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
