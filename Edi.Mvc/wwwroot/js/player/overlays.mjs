

export function createOverlays({ state, elements, presentation, playerUiActive, hideFullscreenToolbar }) {
    const { optionButtons, playbackToolbarHome } = elements;
    const { video, fullscreenTarget, playbackToolbar, fullscreenPlaybackOverlay } = elements;
    let currentVariantSummary = null;

    const deviceControls = elements.deviceControls;

    const deviceControlsHome = deviceControls ? document.createComment('device controls home') : null;

    const intensityOverlay = elements.intensityOverlay;

    function positionIntensityOverlay() {
        if (!video || !intensityOverlay) return;
        try {
            const rect = video.getBoundingClientRect();
            const margin = 12;
            intensityOverlay.style.opacity = intensityOverlay.style.opacity || '0';
            const fs = document.fullscreenElement;
            if (fs === fullscreenTarget) {
                if (intensityOverlay.parentElement !== fullscreenTarget) fullscreenTarget.appendChild(intensityOverlay);
                intensityOverlay.style.position = 'absolute';
                intensityOverlay.style.top = `${margin}px`;
                intensityOverlay.style.right = `${margin}px`;
                intensityOverlay.style.left = '';
            } else {
                if (intensityOverlay.parentElement !== document.body) document.body.appendChild(intensityOverlay);
                // normal page mode: fixed on window aligned to video
                intensityOverlay.style.position = 'fixed';
                const overlayWidth = intensityOverlay.offsetWidth || 60;
                const left = Math.max(8, Math.min(window.innerWidth - overlayWidth - 8, rect.right - overlayWidth - margin));
                const top = Math.max(8, rect.top + margin);
                intensityOverlay.style.left = `${left}px`;
                intensityOverlay.style.top = `${top}px`;
                intensityOverlay.style.right = '';
            }
            // ensure overlay appears above fullscreen video
            intensityOverlay.style.zIndex = '1080';
        } catch (e) { }
    }

    function hidePlayerOverlay(force = false) {
        if (presentation.fullscreenDeviceControlsVisible
            || !force && playerUiActive()) return;
        if (presentation.intensityHideTimer) clearTimeout(presentation.intensityHideTimer);
        presentation.intensityHideTimer = null;
        intensityOverlay.style.opacity = '0';
        intensityOverlay.style.transform = 'scale(0.96)';
        hideFullscreenToolbar(force);
    }

    function showPlayerOverlay(content, duration = 1000) {
        if (presentation.fullscreenDeviceControlsVisible) return;
        intensityOverlay.replaceChildren();
        if (content?.nodeType) intensityOverlay.append(content);
        else intensityOverlay.textContent = String(content ?? '');
        positionIntensityOverlay();
        intensityOverlay.style.transform = 'scale(1)';
        intensityOverlay.style.opacity = '1';
        if (presentation.intensityHideTimer) clearTimeout(presentation.intensityHideTimer);
        presentation.intensityHideTimer = duration == null ? null : setTimeout(hidePlayerOverlay, duration);
    }

    function createIconOverlayContent(button, text, danger = false) {
        const content = document.createElement('span');
        content.style.display = 'inline-flex';
        content.style.alignItems = 'center';
        content.style.gap = '0.55rem';
        if (danger) content.style.color = 'var(--bs-danger, #dc3545)';

        const icon = button?.querySelector('svg')?.cloneNode(true);
        if (icon) {
            icon.querySelectorAll('[id]').forEach(element => element.removeAttribute('id'));
            icon.style.width = '1.7rem';
            icon.style.height = '1.7rem';
            icon.style.flex = '0 0 auto';
            content.append(icon);
        }

        const label = document.createElement('span');
        label.textContent = text;
        content.append(label);
        return content;
    }

    function showIntensityOverlay(value) {
        showPlayerOverlay(createIconOverlayContent(optionButtons.intensity, `${value}%`, value === 0));
    }

    function shortenOverlayText(value, maximum = 28) {
        const characters = Array.from(value || '');
        return characters.length > maximum
            ? `${characters.slice(0, maximum - 3).join('')}...`
            : value;
    }

    function createStrokerStateIcon(paused) {
        const namespace = 'http://www.w3.org/2000/svg';
        const svg = document.createElementNS(namespace, 'svg');
        svg.setAttribute('viewBox', '0 0 24 24');
        svg.setAttribute('aria-hidden', 'true');
        svg.style.width = '1.7rem';
        svg.style.height = '1.7rem';
        svg.style.display = 'block';
        svg.style.fill = 'none';
        svg.style.stroke = 'currentColor';
        svg.style.strokeWidth = '2.2';
        svg.style.strokeLinecap = 'round';
        svg.style.strokeLinejoin = 'round';
        if (paused) svg.style.color = 'var(--bs-danger, #dc3545)';

        const body = document.createElementNS(namespace, 'rect');
        body.setAttribute('x', '7');
        body.setAttribute('y', '2.5');
        body.setAttribute('width', '10');
        body.setAttribute('height', '19');
        body.setAttribute('rx', '5');
        const arrows = document.createElementNS(namespace, 'path');
        arrows.setAttribute('d', 'M12 6v12M9.5 8.5 12 6l2.5 2.5M9.5 15.5 12 18l2.5-2.5');
        svg.append(body, arrows);

        if (paused) {
            const slash = document.createElementNS(namespace, 'path');
            slash.setAttribute('d', 'M3.5 3.5l17 17');
            slash.style.strokeWidth = '3.2';
            svg.append(slash);
        }
        return svg;
    }

    function showStrokerStateOverlay(paused) {
        showPlayerOverlay(createStrokerStateIcon(paused));
    }

    function createFullscreenSummary() {
        const summary = document.createElement('span');
        summary.style.display = 'inline-flex';
        summary.style.alignItems = 'center';
        summary.style.gap = '0.65rem';
        summary.append(createStrokerStateIcon(state.strokerPaused || video.paused || video.ended));

        const intensity = document.createElement('span');
        intensity.textContent = `${state.currentIntensity}%`;
        if (state.currentIntensity === 0) intensity.style.color = 'var(--bs-danger, #dc3545)';
        summary.append(intensity);

        if (currentVariantSummary?.length) {
            const variant = document.createElement('span');
            const values = currentVariantSummary.slice(0, 3).map(state =>
                `${state.side ? `${state.side}: ` : ''}${shortenOverlayText(state.variant, 18)}`);
            if (currentVariantSummary.length > 3) values.push('…');
            variant.textContent = values.join(' · ');
            summary.append(variant);
        }
        return summary;
    }

    function showPlayerSummary(keepVisible = false) {
        if (!document.getElementById('variantTogglePanel')?.hidden) return;
        showPlayerOverlay(createFullscreenSummary(), keepVisible ? null : 650);
        const fullscreen = document.fullscreenElement === fullscreenTarget;
        intensityOverlay.style.pointerEvents = fullscreen ? 'auto' : 'none';
    }

    function restoreDeviceControls() {
        if (presentation.fullscreenDeviceControlsVisible && deviceControls && deviceControlsHome?.parentNode) {
            deviceControlsHome.after(deviceControls);
        }
        deviceControls?.classList.remove('fullscreen-device-controls');
        presentation.fullscreenDeviceControlsVisible = false;
        intensityOverlay.style.pointerEvents = 'none';
        intensityOverlay.style.overflow = 'hidden';
    }

    function placeFullscreenPlaybackToolbar() {
        if (playbackToolbar && fullscreenPlaybackOverlay
            && playbackToolbar.parentElement !== fullscreenPlaybackOverlay) {
            fullscreenPlaybackOverlay.prepend(playbackToolbar);
        }
        playbackToolbar?.classList.add('fullscreen-control-surface');
    }

    function restorePlaybackToolbar() {
        if (playbackToolbar && playbackToolbarHome?.parentNode)
            playbackToolbarHome.after(playbackToolbar);
        playbackToolbar?.classList.remove('fullscreen-control-surface');
        fullscreenPlaybackOverlay?.classList.remove('visible');
        presentation.pointerOverFullscreenPlayback = false;
    }

    function showFullscreenDeviceControls() {
        if (document.fullscreenElement !== fullscreenTarget || !deviceControls || presentation.fullscreenDeviceControlsVisible) return;
        presentation.fullscreenDeviceControlsVisible = true;
        if (presentation.intensityHideTimer) clearTimeout(presentation.intensityHideTimer);
        presentation.intensityHideTimer = null;
        intensityOverlay.replaceChildren(deviceControls);
        deviceControls.classList.add('fullscreen-device-controls');
        intensityOverlay.style.pointerEvents = 'auto';
        intensityOverlay.style.overflow = 'visible';
        intensityOverlay.style.transform = 'scale(1)';
        intensityOverlay.style.opacity = '1';
    }

    let fullscreenSummaryRevealTimer = null;

    let fullscreenSummaryMotionTimer = null;

    let fullscreenSummaryHideTimer = null;

    let fullscreenSummaryVisible = false;

    let fullscreenSummaryOverControls = false;

    function resetFullscreenSummary(hide = true) {
        if (fullscreenSummaryRevealTimer) clearTimeout(fullscreenSummaryRevealTimer);
        if (fullscreenSummaryMotionTimer) clearTimeout(fullscreenSummaryMotionTimer);
        if (fullscreenSummaryHideTimer) clearTimeout(fullscreenSummaryHideTimer);
        fullscreenSummaryRevealTimer = null;
        fullscreenSummaryMotionTimer = null;
        fullscreenSummaryHideTimer = null;
        fullscreenSummaryVisible = false;
        if (document.fullscreenElement !== fullscreenTarget) restoreDeviceControls();
        if (hide) hidePlayerOverlay();
    }

    function handlePlayerSummaryMovement(overControls, pointerOverVideo = true) {
        if (!document.getElementById('variantTogglePanel')?.hidden) {
            resetFullscreenSummary();
            return;
        }
        if (presentation.fullscreenDeviceControlsVisible) return;
        const fullscreen = document.fullscreenElement === fullscreenTarget;
        const windowed = !document.fullscreenElement && pointerOverVideo;
        if (!fullscreen && !windowed) {
            resetFullscreenSummary(false);
            return;
        }

        fullscreenSummaryOverControls = fullscreen && overControls;
        if (fullscreenSummaryMotionTimer) clearTimeout(fullscreenSummaryMotionTimer);
        fullscreenSummaryMotionTimer = setTimeout(() => {
            fullscreenSummaryMotionTimer = null;
            if (!fullscreenSummaryVisible && fullscreenSummaryRevealTimer) {
                clearTimeout(fullscreenSummaryRevealTimer);
                fullscreenSummaryRevealTimer = null;
            }
        }, 200);

        if (!fullscreenSummaryVisible && !fullscreenSummaryRevealTimer) {
            fullscreenSummaryRevealTimer = setTimeout(() => {
                fullscreenSummaryRevealTimer = null;
                fullscreenSummaryVisible = true;
                showPlayerSummary(fullscreenSummaryOverControls);
                if (!fullscreenSummaryOverControls) {
                    fullscreenSummaryHideTimer = setTimeout(() => resetFullscreenSummary(), 650);
                }
            }, 650);
            return;
        }

        if (!fullscreenSummaryVisible) return;
        if (fullscreenSummaryHideTimer) clearTimeout(fullscreenSummaryHideTimer);
        fullscreenSummaryHideTimer = null;
        const keepVisible = fullscreen && overControls;
        showPlayerSummary(keepVisible);
        if (!keepVisible) {
            fullscreenSummaryHideTimer = setTimeout(() => resetFullscreenSummary(), 650);
        }
    }

    function mount() {
        if (deviceControlsHome) deviceControls.before(deviceControlsHome);
        intensityOverlay.className = 'edi-intensity-overlay';
        intensityOverlay.style.position = 'fixed';
        intensityOverlay.style.top = '0';
        intensityOverlay.style.left = '0';
        intensityOverlay.classList.add('fullscreen-control-surface');
        intensityOverlay.style.fontSize = '1.5rem';
        intensityOverlay.style.opacity = '0';
        intensityOverlay.style.pointerEvents = 'none';
        intensityOverlay.style.transition = 'opacity 180ms ease-out, transform 180ms ease-out';
        intensityOverlay.style.transform = 'scale(0.96)';
        intensityOverlay.style.zIndex = '1080';
        intensityOverlay.style.display = 'inline-block';
        intensityOverlay.style.whiteSpace = 'nowrap';
        intensityOverlay.style.maxWidth = 'calc(100vw - 24px)';
        intensityOverlay.style.overflow = 'hidden';
        intensityOverlay.style.textOverflow = 'ellipsis';
        intensityOverlay.textContent = `${state.currentIntensity}%`;
        try { document.body.appendChild(intensityOverlay); } catch (e) { /* ignore */ }
        intensityOverlay.addEventListener('pointerenter', showFullscreenDeviceControls);
        intensityOverlay.addEventListener('pointerleave', () => {
            if (!presentation.fullscreenDeviceControlsVisible
                || !document.getElementById('variantTogglePanel')?.hidden) return;
            restoreDeviceControls();
            fullscreenSummaryVisible = true;
            showPlayerSummary(true);
        });
        document.addEventListener('edi-variant-state', event => {
            currentVariantSummary = Array.isArray(event.detail?.devices)
                ? event.detail.devices.filter(state => state?.variant)
                : [];
        });
        document.addEventListener('edi-variant-switched', event => {
            const states = Array.isArray(event.detail?.devices)
                ? event.detail.devices.filter(state => state?.variant)
                : [];
            if (!states.length) return;
            currentVariantSummary = states;
            const text = states.slice(0, 3).map(state =>
                `${state.side ? `${state.side}: ` : ''}${shortenOverlayText(state.variant, 18)}`).join(' · ');
            showPlayerOverlay(createIconOverlayContent(
                document.getElementById('variantToggle'),
                text));
        });
        document.addEventListener('edi-variant-panel', event => {
            if (event.detail?.open && !presentation.fullscreenDeviceControlsVisible) resetFullscreenSummary();
        });
        document.addEventListener('fullscreenchange', () => positionIntensityOverlay());
        window.addEventListener('resize', () => positionIntensityOverlay());
        document.addEventListener('scroll', () => positionIntensityOverlay(), true);
        video.addEventListener('loadedmetadata', () => positionIntensityOverlay());
    }
    return { hidePlayerOverlay, showPlayerOverlay, createIconOverlayContent, showIntensityOverlay, showStrokerStateOverlay, restoreDeviceControls, placeFullscreenPlaybackToolbar, restorePlaybackToolbar, showFullscreenDeviceControls, resetFullscreenSummary, handlePlayerSummaryMovement, mount };
}

