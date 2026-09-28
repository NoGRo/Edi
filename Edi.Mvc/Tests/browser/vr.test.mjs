import test from 'node:test';
import assert from 'node:assert/strict';
import { createServer } from 'node:http';
import { readFile, writeFile } from 'node:fs/promises';
import { createRequire } from 'node:module';
import { fileURLToPath } from 'node:url';
import { resolve, extname, sep, join } from 'node:path';
import { tmpdir } from 'node:os';
import { installFakeXr } from './fake-xr.mjs';

const { chromium } = createRequire(import.meta.url)('playwright');
const root = fileURLToPath(new URL('../../wwwroot/', import.meta.url));
async function rig(run) {
    const server = createServer(async (request, response) => {
        const pathname = new URL(request.url, 'http://localhost').pathname;
        const path = resolve(root, pathname === '/' ? 'index.html' : `.${pathname}`);
        if (!path.startsWith(root.endsWith(sep) ? root : root + sep)) { response.writeHead(403).end(); return; }
        try {
            response.writeHead(200, { 'Content-Type': { '.js': 'text/javascript', '.mjs': 'text/javascript', '.css': 'text/css', '.html': 'text/html' }[extname(path)] || 'application/octet-stream' });
            response.end(await readFile(path));
        } catch { response.writeHead(404).end(); }
    });
    await new Promise(done => server.listen(0, '127.0.0.1', done));
    let browser;
    try {
        browser = await chromium.launch({ channel: 'msedge', headless: true, args: ['--enable-webxr-testing', '--enable-blink-test-features'] });
        const page = await browser.newPage({ viewport: { width: 1400, height: 1000 } }), errors = [], calls = [];
        page.on('pageerror', error => errors.push(error.message));
        await page.route('**/Edi/**', route => { calls.push(route.request().url()); return route.fulfill({ json: [] }); });
        await page.route('**/Devices**', route => route.fulfill({ json: [{ name: 'Preview', isReady: true,
            variants: ['default', 'fast', 'None'], selectedVariant: 'default', min: 0, max: 100, baseMin: 0, baseMax: 100 }] }));
        await page.goto(`http://127.0.0.1:${server.address().port}/`);
        await page.waitForFunction(() => document.querySelector('.device-card') && document.querySelector('#vrSettings'));
        await run(page, calls);
        assert.deepEqual(errors, []);
    } finally {
        await browser?.close();
        await new Promise(done => server.close(done));
    }
}

async function addVideo(page, name = 'scene_180_LR.webm') {
    const fixture = await readFile(new URL('./drop-video.webm', import.meta.url));
    await page.evaluate(({ encoded, name }) => {
        const data = Uint8Array.from(atob(encoded), character => character.charCodeAt(0));
        const transfer = new DataTransfer(); transfer.items.add(new File([data], name, { type: 'video/webm' }));
        window.dispatchEvent(new DragEvent('drop', { bubbles: true, cancelable: true, dataTransfer: transfer }));
    }, { encoded: fixture.toString('base64'), name });
    await page.waitForFunction(() => document.querySelector('#videoPlayer').readyState >= 2);
}

test('VR settings detect media, remember overrides and explain unavailable sessions', async () => rig(async page => {
    await addVideo(page);
    await page.locator('#vrSettingsToggle').click();
    assert.equal(await page.locator('[data-vr-setting=projection]').count(), 0);
    assert.equal(await page.locator('[data-vr-setting=stereo]').inputValue(), 'sbs');
    await page.locator('[data-vr-setting=stereo]').selectOption('mono');
    await page.locator('#vrFollowToggle').click();
    assert.equal(await page.locator('#vrFollowToggle').getAttribute('aria-pressed'), 'true');
    assert.equal(await page.locator('[data-vr-setting=follow]').isChecked(), true);
    assert.equal(await page.evaluate(() => JSON.parse(localStorage.getItem('edi-player-vr')).formats['scene_180_lr.webm'].stereo), 'mono');
    await page.locator('[data-vr-auto]').click();
    assert.equal(await page.locator('[data-vr-setting=stereo]').inputValue(), 'sbs');
    await page.evaluate(() => {
        Object.defineProperty(navigator, 'xr', { configurable: true, value: undefined });
        document.querySelector('#enterVr').hidden = true;
    });
    assert.equal(await page.locator('#enterVr').isHidden(), true);
    assert.equal(await page.locator('#playbackToolbar').evaluate(node => node.parentElement.className), 'player-controls mb-2');
    assert.equal(await page.locator('#enterVr').isEnabled(), true);
}));

test('VR HTML panels paint the original controls, dispatch changes, reflect mutations and restore them', async () => rig(async page => {
    await addVideo(page);
    await page.locator('#vrSettingsToggle').click();
    await page.evaluate(async () => {
        const { getPlayerElements } = await import('/js/player/elements.mjs');
        const { createHtmlPanels } = await import('/js/player/vr/html-panels.mjs');
        const THREE = await import('/lib/vr/three.module.min.js');
        const elements = getPlayerElements(); elements.intensityOverlay = document.querySelector('.edi-intensity-overlay');
        const originals = [elements.playbackToolbar, elements.deviceControls, elements.customVideoControls,
            elements.dropZone, elements.devicesPanel, document.querySelector('#vrSettings')].map(node => ({ node, parent: node.parentElement }));
        const preferences = { width: 2.4, stereo: 'sbs', swapEyes: false, halfResolution: false };
        const errors = [], panels = await createHtmlPanels({ rig: new THREE.Group(), elements, preferences, exit: async () => {}, onError: error => errors.push(error.message) });
        const timer = setInterval(() => panels.update(performance.now() / 1000), 25);
        window.vrTest = { panels, originals, errors, timer,
            hit(selector) {
                const element = document.querySelector(selector);
                const panel = panels.meshes().map(mesh => mesh.userData.panel).find(panel => panel.root.contains(element));
                const rect = element.getBoundingClientRect(), bounds = panel.root.getBoundingClientRect();
                return panels.hitAt(panel, { x: (rect.x + rect.width / 2 - bounds.x) / panel.width,
                    y: 1 - (rect.y + rect.height / 2 - bounds.y) / panel.height });
            },
            click(selector) { const hit = this.hit(selector); const press = panels.press(hit); panels.release(press, hit); }
        };
    });
    await page.waitForFunction(() => window.vrTest.panels.meshes().length === 1 && window.vrTest.panels.meshes()[0].userData.panel.captured);
    const snapshot = await page.evaluate(() => window.vrTest.panels.meshes()[0].material.map.image.toDataURL());
    await writeFile(join(tmpdir(), 'edi-vr-settings-qa.png'), Buffer.from(snapshot.split(',')[1], 'base64'));
    await page.evaluate(() => {
        window.vrTest.loopTextureVersion = window.vrTest.panels.meshes()[0].material.map.version;
        window.vrTest.click('#loopToggle');
    });
    assert.equal(await page.locator('#loopToggle').getAttribute('aria-label'), 'Repeat video');
    await page.waitForFunction(() => window.vrTest.panels.meshes()[0].material.map.version > window.vrTest.loopTextureVersion);
    await page.evaluate(() => window.vrTest.click('[data-vr-setting=stereo]'));
    await page.waitForFunction(() => window.vrTest.panels.meshes()[0]?.userData.panel.hits.length > 0 && document.querySelector('.vr-popup'));
    const option = await page.evaluate(() => [...document.querySelectorAll('.vr-popup button')].find(button => button.textContent.includes('Mono')).textContent);
    await page.evaluate(label => {
        const button = [...document.querySelectorAll('.vr-popup button')].find(node => node.textContent === label);
        button.id = 'testVrOption'; window.vrTest.click('#testVrOption');
    }, option);
    assert.equal(await page.locator('[data-vr-setting=stereo]').inputValue(), 'mono');
    assert.equal(await page.locator('.vr-popup').count(), 0);
    await page.evaluate(() => {
        const hit = window.vrTest.hit('#customVolume');
        const range = hit.panel.hits.find(item => item.element.id === 'customVolume');
        hit.x = range.inputLeft + range.inputWidth * .25;
        const press = window.vrTest.panels.press(hit); window.vrTest.panels.release(press, hit);
    });
    assert.equal(await page.locator('#videoPlayer').evaluate(video => video.volume), .25);
    const restored = await page.evaluate(() => {
        clearInterval(window.vrTest.timer); window.vrTest.panels.dispose();
        return { errors: window.vrTest.errors, restored: window.vrTest.originals.every(({ node, parent }) => node.parentElement === parent) };
    });
    assert.deepEqual(restored, { errors: [], restored: true });
    assert.equal(await page.locator('.vr-dom-panels').count(), 0);
    assert.equal(await page.locator('#customVideoControls').count(), 1);
}));

test('immersive session renders, uses configured A/B actions and restores the desktop after repeated exits', async () => rig(async (page, calls) => {
    await addVideo(page, 'scene_SBS.webm');
    await installFakeXr(page);
    await page.locator('#enterVr').click();
    await page.waitForFunction(() => document.querySelector('#enterVr').getAttribute('aria-pressed') === 'true'
        && window.fakeXr.session.frames > 10);
    // Point only at controls that have actually been painted in the XR scene.
    await page.waitForFunction(() => window.fakeXr.panels.some(mesh => mesh.userData.panel?.root.contains(document.querySelector('#customVideoControls'))
        && mesh.visible && mesh.userData.panel.captured));
    assert.equal(await page.locator('.vr-canvas').count(), 1);
    await page.evaluate(() => { window.fakeXr.source.gamepad.buttons[3].pressed = true; });
    await page.waitForFunction(() => window.fakeXr.panels.every(mesh => !mesh.visible)
        && document.querySelector('#vrMenusToggle').getAttribute('aria-pressed') === 'false');
    await page.evaluate(() => {
        window.fakeXr.source.gamepad.buttons[3].pressed = false;
        window.menuReleaseFrame = window.fakeXr.session.frames;
    });
    await page.waitForFunction(() => window.fakeXr.session.frames > window.menuReleaseFrame + 1);
    await page.evaluate(() => { window.fakeXr.source.gamepad.buttons[3].pressed = true; });
    await page.waitForFunction(() => window.fakeXr.panels.some(mesh => mesh.visible)
        && document.querySelector('#vrMenusToggle').getAttribute('aria-pressed') === 'true');
    await page.evaluate(() => { window.fakeXr.source.gamepad.buttons[3].pressed = false; });
    await page.evaluate(() => {
        window.mouseButtons = [];
        document.querySelector('#videoPlayer').addEventListener('mousedown', event => window.mouseButtons.push(event.button));
        window.fakeXr.source.gamepad.buttons[4].pressed = true;
    });
    await page.waitForFunction(() => !document.querySelector('#videoPlayer').paused);
    await page.evaluate(() => {
        window.fakeXr.source.gamepad.buttons[4].pressed = false;
        window.buttonReleaseFrame = window.fakeXr.session.frames;
    });
    await page.waitForFunction(() => window.fakeXr.session.frames > window.buttonReleaseFrame + 1);
    await page.evaluate(() => {
        document.querySelector('#strokerToggle').click();
        window.fakeXr.source.gamepad.buttons[4].pressed = true;
    });
    await page.waitForFunction(() => document.querySelector('#strokerToggle').classList.contains('stroker-paused'));
    assert.equal(await page.locator('#videoPlayer').evaluate(video => video.paused), false);
    await page.evaluate(() => { window.fakeXr.source.gamepad.buttons[5].pressed = true; });
    await page.waitForFunction(() => window.mouseButtons.includes(2));
    await page.evaluate(() => {
        window.fakeXr.source.gamepad.buttons[4].pressed = false;
        window.fakeXr.source.gamepad.buttons[5].pressed = false;
        window.fakeXr.session.pointAt(0, 2.8, -3); // Outside all control panels.
        window.fakeXr.source.gamepad.axes[3] = -1;
    });
    await page.waitForFunction(() => Number(localStorage.getItem('edi-player-intensity')) >= 5);
    assert.ok(calls.some(path => path.includes('/Edi/Intensity/')));
    const intensity = await page.evaluate(() => {
        window.fakeXr.source.gamepad.axes[3] = 0;
        return Number(localStorage.getItem('edi-player-intensity'));
    });
    await page.evaluate(() => {
        window.fakeXr.source.gamepad.axes[3] = 0;
        document.querySelector('#videoPlayer').pause();
        document.querySelector('#videoPlayer').volume = .5;
        window.fakeXr.pointControl = selector => {
            const node = document.querySelector(selector), root = node.closest('.vr-dom-panel');
            const rect = node.getBoundingClientRect(), bounds = root.getBoundingClientRect();
            window.fakeXr.session.pointAt((rect.x + rect.width / 2 - bounds.x - bounds.width / 2) * .00135,
                1.6 - .4 + (bounds.height / 2 - (rect.y + rect.height / 2 - bounds.y)) * .00135, -1.4);
        };
        window.fakeXr.pointControl('#customVolume');
        window.fakeXr.source.gamepad.axes[3] = -1;
    });
    await page.waitForFunction(() => document.querySelector('#videoPlayer').volume >= .6);
    assert.equal(await page.evaluate(() => Number(localStorage.getItem('edi-player-intensity'))), intensity);
    await page.evaluate(() => {
        window.fakeXr.source.gamepad.axes[3] = 0;
        document.querySelector('#videoPlayer').currentTime = 0;
        window.fakeXr.pointControl('#customTime');
        window.fakeXr.source.gamepad.axes[2] = 1;
    });
    await page.waitForFunction(() => document.querySelector('#videoPlayer').currentTime > .1);
    await page.evaluate(() => { window.fakeXr.source.gamepad.axes[2] = -1; });
    await page.waitForFunction(() => document.querySelector('#videoPlayer').currentTime === 0);
    assert.equal(await page.evaluate(() => Number(localStorage.getItem('edi-player-intensity'))), intensity);
    await page.evaluate(() => {
        window.fakeXr.source.gamepad.axes[2] = 0;
        window.fakeXr.session.pointAt(0, 1.6, -2);
        window.fakeXr.session.trigger('selectstart');
        window.fakeXr.session.rayPosition[0] += .3;
        window.dragFrame = window.fakeXr.session.frames;
    });
    await page.waitForFunction(() => window.fakeXr.session.frames > window.dragFrame + 3);
    await page.evaluate(() => window.fakeXr.session.trigger('selectend'));
    assert.ok(Math.abs(await page.evaluate(() => JSON.parse(localStorage.getItem('edi-player-vr')).view.offsetX) - .3) < .02);
    for (const [stereo, delta, expected] of [['sbs', .3, .6], ['mono', -.25, .35]]) {
        await page.evaluate(({ stereo, delta }) => {
            const field = document.querySelector('[data-vr-setting=stereo]'); field.value = stereo; field.dispatchEvent(new Event('input', { bubbles: true }));
            const offset = JSON.parse(localStorage.getItem('edi-player-vr')).view.offsetX;
            window.fakeXr.session.pointAt(offset, 1.6, -2);
            window.fakeXr.session.trigger('squeezestart');
            window.fakeXr.session.rayPosition[0] += delta;
            window.dragFrame = window.fakeXr.session.frames;
        }, { stereo, delta });
        await page.waitForFunction(() => window.fakeXr.session.frames > window.dragFrame + 3);
        await page.evaluate(() => window.fakeXr.session.trigger('squeezeend'));
        assert.ok(Math.abs(await page.evaluate(() => JSON.parse(localStorage.getItem('edi-player-vr')).view.offsetX) - expected) < .02, `grip ${stereo}`);
    }
    await page.evaluate(async () => { window.fakeXr.source.gamepad.axes[3] = 0; await window.fakeXr.session.end(); });
    await page.waitForFunction(() => !document.querySelector('.vr-canvas') && !document.querySelector('.vr-dom-panels'));
    assert.equal(await page.locator('#customVideoControls').evaluate(node => node.parentElement.id), 'videoStage');
    assert.equal(await page.locator('#enterVr').getAttribute('aria-pressed'), 'false');
    assert.equal(await page.locator('#enterVr').isEnabled(), true);
    await page.evaluate(() => document.querySelector('#videoPlayer').pause());
    await page.locator('#enterVr').click();
    await page.waitForFunction(() => document.querySelector('#enterVr').getAttribute('aria-pressed') === 'true');
    await page.evaluate(async () => { await window.fakeXr.session.end(); });
    assert.equal(await page.locator('#customVideoControls').count(), 1);
    assert.equal(await page.locator('.vr-dom-panels').count(), 0);
}));

test('grip rotates the real video surface and locked follow waits, eases and ignores micro motion', async () => rig(async page => {
    await addVideo(page, 'scene_SBS.webm');
    await installFakeXr(page);
    await page.evaluate(async () => {
        const THREE = await import('/lib/vr/three.module.min.js');
        const add = THREE.Group.prototype.add;
        THREE.Group.prototype.add = function(...objects) {
            const result = add.apply(this, objects);
            if (objects.some(mesh => mesh.material?.map?.isVideoTexture)) window.videoSurface = this;
            return result;
        };
    });
    await page.locator('#enterVr').click();
    await page.waitForFunction(() => window.videoSurface && window.fakeXr.session.frames > 5);
    const step = async milliseconds => {
        await page.evaluate(milliseconds => {
            window.fakeXr.session.clock += milliseconds;
            window.poseFrame = window.fakeXr.session.frames;
        }, milliseconds);
        await page.waitForFunction(() => window.fakeXr.session.frames > window.poseFrame + 1);
    };
    const pose = () => page.evaluate(() => ({ position: window.videoSurface.position.toArray(), rotation: window.videoSurface.quaternion.toArray() }));
    const initial = await pose();
    await page.evaluate(() => {
        window.fakeXr.session.clock = 0;
        for (const [key, value] of [['followDelay', .5], ['followEase', .15]]) {
            const field = document.querySelector(`[data-vr-setting=${key}]`);
            field.value = value; field.dispatchEvent(new Event('input', { bubbles: true }));
        }
        document.querySelector('#vrFollowToggle').click();
    });
    await step(0);
    await page.evaluate(() => {
        window.fakeXr.session.headPosition[0] = .01;
        window.fakeXr.session.headRotation = [0, Math.sin(.01), 0, Math.cos(.01)];
    });
    await step(600);
    assert.deepEqual(await pose(), initial);
    await page.evaluate(() => {
        window.fakeXr.session.headPosition[0] = .3;
        window.fakeXr.session.headRotation = [0, Math.sin(.2), 0, Math.cos(.2)];
    });
    await step(0);
    await step(400);
    assert.deepEqual(await pose(), initial);
    await step(200);
    const targetX = .3 - 2 * Math.sin(.4), targetZ = -2 * Math.cos(.4);
    const first = await pose();
    assert.ok(first.position[0] < -.03 && first.position[0] > targetX + .02, 'follow eases instead of snapping');
    for (let index = 0; index < 15; index++) await step(50);
    const settled = await pose();
    assert.ok(Math.abs(settled.position[0] - targetX) < .01 && Math.abs(settled.position[2] - targetZ) < .01);
    assert.ok(Math.abs(settled.rotation[1] - Math.sin(.2)) < .01);
    await page.evaluate(() => document.querySelector('#vrFollowToggle').click());
    await page.evaluate(() => {
        window.fakeXr.session.headPosition[0] = -1;
        window.fakeXr.session.headRotation = [0, Math.sin(-.35), 0, Math.cos(-.35)];
    });
    await step(1000);
    assert.deepEqual(await pose(), settled, 'unlock leaves the video in the world');
    await page.evaluate(() => {
        window.fakeXr.session.headPosition = [0, 1.6, 0];
        window.fakeXr.session.headRotation = [0, 0, 0, 1];
    });
    await step(0);
    await page.evaluate(() => {
        document.querySelector('[data-vr-recenter]').click();
        window.fakeXr.session.pointAt(0, 1.6, -2);
        window.fakeXr.session.trigger('squeezestart');
    });
    await page.evaluate(async () => {
        const THREE = await import('/lib/vr/three.module.min.js');
        const hand = new THREE.Quaternion(...window.fakeXr.session.rayRotation);
        window.fakeXr.session.rayRotation = new THREE.Quaternion().setFromAxisAngle(new THREE.Vector3(0, 1, 0), .25).multiply(hand).toArray();
    });
    await step(50);
    await page.evaluate(() => window.fakeXr.session.trigger('squeezeend'));
    assert.ok(Math.abs((await pose()).rotation[1] - Math.sin(.125)) < .01, 'grip preserves hand rotation');
    await page.evaluate(() => window.fakeXr.session.end());
}));

test('a failed XR request or HTML renderer returns to usable desktop controls', async () => rig(async page => {
    await page.evaluate(() => Object.defineProperty(navigator, 'xr', { configurable: true, value: {
        async requestSession() { throw new Error('Test session rejected'); }
    } }));
    await page.locator('#enterVr').click();
    await page.waitForFunction(() => document.querySelector('#vrStatus').textContent.includes('Test session rejected'));
    assert.equal(await page.locator('.vr-dom-panels').count(), 0);
    await installFakeXr(page);
    await page.evaluate(() => { window.html2canvas = async () => { throw new Error('Test snapshot failed'); }; });
    await page.locator('#enterVr').click();
    await page.waitForFunction(() => document.querySelector('#vrStatus').textContent.includes('Test snapshot failed'));
    assert.equal(await page.locator('.vr-canvas').count(), 0);
    assert.equal(await page.locator('.vr-dom-panels').count(), 0);
    assert.equal(await page.locator('#enterVr').isEnabled(), true);
    assert.equal(await page.locator('#customVideoControls').evaluate(node => node.parentElement.id), 'videoStage');
}));
