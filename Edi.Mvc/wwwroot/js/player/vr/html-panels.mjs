import * as THREE from '../../../lib/vr/three.module.min.js';
import { clamp } from './motion.mjs';
import { eyeAspect } from './format.mjs';

let snapshotLoader;
const layoutWidth = 1920;
const textureScale = .5;
export function loadSnapshotRenderer() {
    if (window.html2canvas) return Promise.resolve(window.html2canvas);
    if (!snapshotLoader) snapshotLoader = new Promise((resolve, reject) => {
        const script = document.createElement('script');
        script.src = '/lib/vr/html2canvas.min.js';
        script.onload = () => resolve(window.html2canvas);
        script.onerror = () => { snapshotLoader = null; script.remove(); reject(new Error('Could not load the HTML panel renderer.')); };
        document.head.append(script);
    });
    return snapshotLoader;
}

const clickable = 'button,input,select,a,label,.playlist-item,[draggable="true"],[title="Drag to reorder"]';
export async function createHtmlPanels({ rig, elements, preferences, exit, onError }) {
    const snapshot = await loadSnapshotRenderer();
    const dom = document.createElement('div');
    dom.className = 'vr-dom-panels';
    document.body.append(dom);
    const panels = [];
    let disposed = false, capturing = false, popup = null, hovered = null, surfacePanel;
    let menuVisible = true;
    let lastCapture = -Infinity;
    const diagnostics = { captures: 0, lastCaptureMs: 0, maxCaptureMs: 0, visiblePanels: 0, dirtyPanels: 0 };

    function add(source, position, { move = true, scroll = false, modal = false, top = false, worldWidth = null } = {}) {
        const home = move ? document.createComment('VR panel home') : null;
        const root = move ? document.createElement('div') : source;
        if (move) {
            source.before(home);
            root.className = 'vr-dom-panel';
            root.append(source); dom.append(root);
        }
        const texture = new THREE.CanvasTexture(document.createElement('canvas'));
        texture.colorSpace = THREE.SRGBColorSpace;
        texture.minFilter = THREE.LinearFilter;
        texture.generateMipmaps = false;
        const material = new THREE.MeshBasicMaterial({ map: texture, transparent: true, side: THREE.DoubleSide,
            depthTest: false, depthWrite: false, toneMapped: false });
        const mesh = new THREE.Mesh(new THREE.PlaneGeometry(1, 1), material);
        mesh.position.set(...position);
        mesh.renderOrder = modal ? 12 : 10;
        rig.add(mesh);
        const panel = { source, root, home, texture, mesh, modal, dirty: true, lastCapture: -Infinity,
            width: 1, height: 1, hits: [], removed: false, captured: false, available: false,
            topEdge: top ? position[1] : null, worldWidth };
        mesh.userData.panel = panel;
        const playbackTick = mutation => {
            if (mutation.type === 'attributes' && mutation.oldValue === mutation.target.getAttribute(mutation.attributeName)) return true;
            const element = mutation.target.nodeType === Node.ELEMENT_NODE ? mutation.target : mutation.target.parentElement;
            return Boolean(element?.closest('#customElapsed,#customDuration,#customSeek'));
        };
        const invalidate = mutations => {
            // timeupdate changes text and seek styling several times per second.
            // Re-running html2canvas for those passive ticks monopolizes the
            // headset's main thread; direct input/change events below still
            // invalidate immediately while the user operates a control.
            if (!Array.isArray(mutations) || mutations.some(mutation => !playbackTick(mutation))) panel.dirty = true;
        };
        const observer = new MutationObserver(invalidate);
        observer.observe(root, { subtree: true, childList: true, attributes: true, attributeOldValue: true, characterData: true });
        root.addEventListener('input', invalidate);
        root.addEventListener('change', invalidate);
        root.addEventListener('scroll', invalidate, true);
        panel.dispose = () => {
            panel.removed = true;
            observer.disconnect();
            root.removeEventListener('input', invalidate);
            root.removeEventListener('change', invalidate);
            root.removeEventListener('scroll', invalidate, true);
            mesh.removeFromParent(); mesh.geometry.dispose(); material.dispose(); texture.dispose();
            if (home) { home.after(source); home.remove(); root.remove(); }
            const index = panels.indexOf(panel);
            if (index >= 0) panels.splice(index, 1);
        };
        if (scroll) {
            const footer = document.createElement('div');
            footer.className = 'vr-panel-scroll';
            for (const [label, direction] of [['↑ Scroll', -1], ['↓ Scroll', 1]]) {
                const button = document.createElement('button');
                button.className = 'btn btn-sm btn-outline-secondary'; button.type = 'button'; button.textContent = label;
                button.addEventListener('click', () => {
                    const scrollable = [source, ...source.querySelectorAll('*')].filter(node => node.scrollHeight > node.clientHeight + 1
                        && ['auto', 'scroll'].includes(getComputedStyle(node).overflowY));
                    (scrollable.find(node => node.scrollTop > 0 || direction > 0 && node.scrollTop < node.scrollHeight - node.clientHeight) || scrollable[0])?.scrollBy(0, direction * 240);
                    invalidate();
                });
                footer.append(button);
            }
            root.append(footer);
        }
        panels.push(panel);
        return panel;
    }

    function measure(panel) {
        const bounds = panel.root.getBoundingClientRect();
        panel.width = bounds.width; panel.height = bounds.height;
        panel.hits = [...panel.root.querySelectorAll(clickable)].flatMap(element => {
            const style = getComputedStyle(element), rect = element.getBoundingClientRect();
            if (!rect.width || !rect.height || style.display === 'none' || style.visibility === 'hidden') return [];
            let left = rect.left, top = rect.top, right = rect.right, bottom = rect.bottom;
            for (let parent = element.parentElement; parent && parent !== panel.root.parentElement; parent = parent.parentElement) {
                const parentStyle = getComputedStyle(parent), clip = parent.getBoundingClientRect();
                if (parentStyle.overflowX !== 'visible') { left = Math.max(left, clip.left); right = Math.min(right, clip.right); }
                if (parentStyle.overflowY !== 'visible') { top = Math.max(top, clip.top); bottom = Math.min(bottom, clip.bottom); }
                if (parent === panel.root) break;
            }
            return right <= left || bottom <= top ? [] : [{ element, left: left - bounds.left, top: top - bounds.top,
                right: right - bounds.left, bottom: bottom - bounds.top,
                inputLeft: rect.left - bounds.left, inputWidth: rect.width }];
        });
    }

    async function capture(panel) {
        capturing = true; panel.dirty = false;
        const started = performance.now();
        try {
            measure(panel);
            // Layout at desktop 1080p proportions, but upload a half-resolution
            // texture. This keeps controls correctly sized without quadrupling
            // the texture and rasterization cost.
            const canvas = await snapshot(panel.root, { backgroundColor: null, scale: textureScale, logging: false,
                onclone(clone) {
                    // Browser-native range widgets are not painted consistently
                    // by DOM snapshotters. Paint their current value in the clone;
                    // interactions still hit the ORIGINAL inputs and handlers.
                    clone.querySelectorAll('input[type="range"]').forEach(input => {
                        const range = clone.createElement('div');
                        const fraction = clamp((Number(input.value) - Number(input.min || 0)) / (Number(input.max || 100) - Number(input.min || 0) || 1), 0, 1);
                        range.style.cssText = input.style.cssText;
                        range.className = input.className;
                        Object.assign(range.style, { height: '16px', minHeight: '16px', borderRadius: '8px',
                            background: `linear-gradient(to right, #58a6ff ${fraction * 100}%, #57606a ${fraction * 100}%)`, opacity: input.disabled ? '.4' : '1' });
                        if (input.classList.contains('device-range-input')) {
                            range.style.height = '1.55rem'; range.style.background = 'transparent';
                            const thumb = clone.createElement('span');
                            thumb.style.cssText = `position:absolute;left:calc(${fraction * 100}% + ${.625 - fraction * 1.25}rem);top:50%;width:1.25rem;height:1.25rem;border:2px solid #388bfd;border-radius:50%;background:#21262d;transform:translate(-50%,-50%);`;
                            if (input.classList.contains('device-range-center')) {
                                thumb.style.width = thumb.style.height = '.85rem'; thumb.style.borderRadius = '2px';
                                thumb.style.transform += ' rotate(45deg)';
                            }
                            range.append(thumb);
                        }
                        input.replaceWith(range);
                    });
                }
            });
            if (disposed || panel.removed) return;
            panel.texture.image = canvas; panel.texture.needsUpdate = true;
            panel.mesh.geometry.dispose();
            const width = typeof panel.worldWidth === 'function' ? panel.worldWidth() : panel.worldWidth || panel.width * .00135;
            const height = width * panel.height / panel.width;
            panel.mesh.geometry = new THREE.PlaneGeometry(width, height);
            if (panel.topEdge !== null) panel.mesh.position.y = panel.topEdge - height / 2;
            panel.captured = true;
            measure(panel);
        } catch (error) { onError(error); }
        finally {
            diagnostics.lastCaptureMs = performance.now() - started;
            diagnostics.maxCaptureMs = Math.max(diagnostics.maxCaptureMs, diagnostics.lastCaptureMs);
            diagnostics.captures++;
            capturing = false;
        }
    }

    function update(now) {
        const aspect = eyeAspect(elements.video.videoWidth, elements.video.videoHeight, preferences);
        if (Number(surface.dataset.aspect) !== aspect) {
            surface.dataset.aspect = String(aspect);
            surface.style.height = `${layoutWidth / aspect}px`;
            surfacePanel.dirty = true;
        }
        const dialogs = [...document.querySelectorAll('dialog[open]')];
        for (const dialog of dialogs) if (!panels.some(panel => panel.source === dialog))
            add(dialog, [0, 0, .012], { move: false, modal: true, worldWidth: () => preferences.width * .75 });
        for (const panel of [...panels]) {
            if (panel.modal && !panel.source.isConnected || panel.source.tagName === 'DIALOG' && !panel.source.open) {
                if (!panel.removed) panel.dispose();
            }
            const visible = !panel.removed && !panel.source.hidden && panel.source.isConnected
                && getComputedStyle(panel.source).opacity !== '0' && panel.source.getBoundingClientRect().height > 0;
            panel.available = visible && menuVisible;
            panel.mesh.visible = panel.available && panel.captured;
        }
        // html2canvas is CPU-heavy on standalone headsets. Keep one global
        // capture budget instead of allowing every panel to refresh at 5 Hz.
        // Unpainted panels go first so entering VR still becomes usable quickly.
        const dirty = panels.filter(panel => !panel.removed && panel.available && panel.dirty);
        diagnostics.visiblePanels = panels.filter(panel => !panel.removed && panel.available).length;
        diagnostics.dirtyPanels = dirty.length;
        const next = dirty.find(panel => !panel.captured)
            || dirty.find(panel => panel === hovered?.panel)
            || dirty.sort((a, b) => a.lastCapture - b.lastCapture)[0];
        const interval = next?.captured && next === hovered?.panel ? .2 : .45;
        if (next && !capturing && now - lastCapture >= interval) {
            next.lastCapture = lastCapture = now;
            void capture(next);
        }
    }

    function hitAt(panel, uv) {
        const x = uv.x * panel.width, y = (1 - uv.y) * panel.height;
        const matches = panel.hits.filter(hit => x >= hit.left && x <= hit.right && y >= hit.top && y <= hit.bottom && hit.element.isConnected);
        const ranges = matches.filter(hit => hit.element.matches('input[type="range"]') && !hit.element.disabled);
        // Device range handles overlap: choose the nearest thumb, including the
        // collapse point, rather than permanently selecting the topmost input.
        if (ranges.length > 1) ranges.sort((a, b) => {
            const thumb = hit => hit.inputLeft + hit.inputWidth * (Number(hit.element.value) - Number(hit.element.min || 0))
                / (Number(hit.element.max || 100) - Number(hit.element.min || 0) || 1);
            return Math.abs(thumb(a) - x) - Math.abs(thumb(b) - x);
        });
        const element = ranges[0]?.element || matches.at(-1)?.element;
        if (!element) return null;
        return { panel, element, x, y,
            range: ranges[0] || null };
    }
    function pointer(hit, type) {
        if (!hit?.element?.isConnected) return;
        // Native select popups belong to their source editor. The generated VR
        // options must not trigger the desktop "clicked outside" dismissal.
        if (type === 'pointerdown' && hit.panel.source.classList.contains('vr-popup')) return;
        const bounds = hit.panel.root.getBoundingClientRect();
        hit.element.dispatchEvent(new PointerEvent(type, { bubbles: true, cancelable: true,
            pointerId: 100, pointerType: 'mouse', clientX: bounds.left + hit.x, clientY: bounds.top + hit.y }));
    }
    function hover(hit) {
        if (hovered?.element !== hit?.element) {
            pointer(hovered, 'pointerout'); pointer(hovered, 'pointerleave');
            pointer(hit, 'pointerover');
            hovered = hit;
        }
        pointer(hit, 'pointermove');
    }
    function setRange(hit) {
        const input = hit.element;
        const bounds = hit.range || hit.panel.hits.find(item => item.element === input);
        if (!bounds) return;
        const min = Number(input.min || 0), max = Number(input.max || 100), step = Number(input.step) || .01;
        const value = Number(clamp(min + Math.round((clamp((hit.x - bounds.inputLeft) / bounds.inputWidth, 0, 1) * (max - min)) / step) * step, min, max).toFixed(8));
        if (Number(input.value) === value) return;
        input.value = value;
        input.dispatchEvent(new Event('input', { bubbles: true }));
        hit.panel.dirty = true;
    }
    function closePopup() {
        if (!popup) return;
        const { panel, source, owner } = popup;
        popup = null; source.remove(); owner?.blur(); panel.dirty = true;
    }
    function openEditor(source) {
        closePopup();
        source.focus({ preventScroll: true });
        const editor = document.createElement('div');
        editor.className = 'vr-popup';
        const title = document.createElement('h3');
        title.textContent = source.getAttribute('aria-label') || source.labels?.[0]?.textContent || 'Choose an option';
        editor.append(title);
        const list = document.createElement('div'); list.className = 'vr-popup-options'; editor.append(list);
        const button = (label, action, disabled = false) => {
            const node = document.createElement('button'); node.type = 'button'; node.textContent = label;
            node.className = 'btn btn-outline-secondary'; node.disabled = disabled;
            node.addEventListener('click', action); list.append(node); return node;
        };
        if (source.tagName === 'SELECT') {
            for (const option of source.options) {
                const node = button(option.textContent, () => {
                    if (!source.isConnected) { closePopup(); return; }
                    source.value = option.value;
                    source.dispatchEvent(new Event('input', { bubbles: true }));
                    source.dispatchEvent(new Event('change', { bubbles: true }));
                    closePopup();
                }, option.disabled || option.parentElement.disabled);
                node.classList.toggle('btn-primary', option.selected);
            }
        } else {
            let value = Number(source.value) || 0;
            const output = document.createElement('output'); list.append(output);
            const paint = () => { output.textContent = String(value); };
            for (const direction of [-1, 1]) button(direction < 0 ? '−' : '+', () => {
                value = clamp(value + direction * (Number(source.step) || 1), source.min === '' ? -Infinity : Number(source.min), source.max === '' ? Infinity : Number(source.max)); paint();
            });
            button('Apply', () => {
                source.value = value;
                source.dispatchEvent(new Event('input', { bubbles: true }));
                source.dispatchEvent(new Event('change', { bubbles: true })); closePopup();
            }); paint();
        }
        button('Cancel', closePopup);
        surface.append(editor);
        surfacePanel.dirty = true;
        popup = { panel: surfacePanel, source: editor, owner: source };
    }
    function press(hit) {
        if (!hit || hit.element.disabled) return null;
        const element = hit.element;
        pointer(hit, 'pointerdown');
        if (element.matches('input[type="range"]')) {
            element.focus({ preventScroll: true }); setRange(hit);
            return { hit, kind: 'range' };
        }
        const draggable = element.closest('[draggable="true"]');
        if (draggable && (element.matches('[draggable="true"]') && !element.classList.contains('playlist-item')
            || element.getAttribute('title') === 'Drag to reorder')) {
            const transfer = new DataTransfer();
            draggable.dispatchEvent(new DragEvent('dragstart', { bubbles: true, dataTransfer: transfer }));
            return { hit, kind: 'drag', draggable, transfer };
        }
        return { hit, kind: 'click' };
    }
    function move(press, hit) {
        if (!press || !hit) return;
        if (press.kind === 'range' && press.hit.panel === hit.panel)
            setRange({ ...hit, element: press.hit.element, range: press.hit.range });
        if (press.kind === 'drag') {
            const row = hit.element.closest('.playlist-item,.device-card');
            row?.dispatchEvent(new DragEvent('dragover', { bubbles: true, cancelable: true, dataTransfer: press.transfer }));
        }
    }
    function release(press, hit, cancelled = false) {
        if (!press) return;
        const { element } = press.hit;
        pointer(press.hit, cancelled ? 'pointercancel' : 'pointerup');
        if (press.kind === 'range') {
            if (!cancelled) element.dispatchEvent(new Event('change', { bubbles: true }));
            element.blur();
        } else if (press.kind === 'drag') {
            if (!cancelled) hit?.element.closest('.playlist-item,.device-card')?.dispatchEvent(new DragEvent('drop', { bubbles: true, cancelable: true, dataTransfer: press.transfer }));
            press.draggable.dispatchEvent(new DragEvent('dragend', { bubbles: true, dataTransfer: press.transfer }));
        } else if (!cancelled && hit?.element === element && element.isConnected) {
            if (element.tagName === 'SELECT' || element.matches('input[type="number"]')) openEditor(element);
            else if (element === elements.customFullscreen || element === elements.enterVr) void exit();
            else if (element === elements.addMediaFiles) void exit().then(() => element.click());
            else element.click();
        }
    }
    function meshes() {
        const visible = panels.filter(panel => !panel.removed && panel.mesh.visible);
        if (popup) return popup.panel.mesh.visible ? [popup.panel.mesh] : [];
        const modal = visible.filter(panel => panel.source.tagName === 'DIALOG');
        return (modal.length ? modal : visible).map(panel => panel.mesh);
    }

    const surface = document.createElement('div');
    surface.id = 'vrFullscreenSurface';
    surface.className = 'vr-dom-panel vr-fullscreen-surface';
    surface.style.height = `${layoutWidth / eyeAspect(elements.video.videoWidth, elements.video.videoHeight, preferences)}px`;
    const sources = [elements.playbackToolbar, elements.deviceControls, elements.customVideoControls,
        elements.dropZone, elements.devicesPanel, document.getElementById('vrSettings')].filter(Boolean);
    const homes = sources.map(source => {
        const home = document.createComment('VR fullscreen surface home');
        source.before(home); surface.append(source);
        return { source, home };
    });
    dom.append(surface);
    surfacePanel = add(surface, [0, 0, .006], { move: false, worldWidth: () => preferences.width });
    const disposeSurface = surfacePanel.dispose;
    surfacePanel.dispose = () => {
        for (const { source, home } of homes) { home.after(source); home.remove(); }
        disposeSurface(); surface.remove();
    };
    return { update, hitAt, hover, press, move, release, meshes,
        diagnostics() { return { ...diagnostics, capturing }; },
        toggle() { menuVisible = !menuVisible; return menuVisible; },
        show() { menuVisible = true; return menuVisible; },
        visible() { return menuVisible; },
        dispose() {
            disposed = true; closePopup(); hover(null);
            for (const panel of [...panels]) if (!panel.removed) panel.dispose();
            dom.remove();
        }
    };
}
