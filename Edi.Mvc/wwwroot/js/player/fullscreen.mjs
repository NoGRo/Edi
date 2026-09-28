

export function createFullscreen({ elements, presentation, hidePlayerOverlay, renderCustomControls, restoreDeviceControls, handlePlayerSummaryMovement, resetFullscreenSummary, placeFullscreenPlaybackToolbar, restorePlaybackToolbar }) {
    const { dropZone, fullscreenTarget, customVideoControls, videoStage, video } = elements;
    const fullscreenPlaybackOverlay = elements.fullscreenPlaybackOverlay;

    const playbackToolbar = elements.playbackToolbar;

    const playlistPanelToggle = elements.playlistPanelToggle;

    const devicesPanelToggle = elements.devicesPanelToggle;

    const devicesPanel = elements.devicesPanel;

    const playbackToolbarHome = elements.playbackToolbarHome;



    const deviceControls = elements.deviceControls;

    const intensityOverlay = elements.intensityOverlay;

    function setSidePanelOpen(panelElement, button, open) {
        if (!panelElement) return;
        panelElement.hidden = !open;
        button?.setAttribute('aria-expanded', String(open));
        button?.setAttribute('aria-label', `${open ? 'Hide' : 'Show'} ${panelElement === dropZone ? 'playlist' : 'devices'}`);
        if (open && document.fullscreenElement === fullscreenTarget) showFullscreenToolbar();
    }

    function toggleSidePanel(panelElement, button) {
        setSidePanelOpen(panelElement, button, panelElement?.hidden !== false);
    }

    function hideFullscreenToolbar(force = false) {
        if (!force && playerUiActive()) return;
        fullscreenPlaybackOverlay?.classList.remove('visible');
    }

    function showFullscreenToolbar() {
        if (document.fullscreenElement !== fullscreenTarget) return;
        // The top-right summary sits above the side panels. Desktop hover usually
        // replaces it in time, but Quest ray clicks can keep targeting that old
        // surface. Remove it before exposing any interactive fullscreen control.
        resetFullscreenSummary();
        if (presentation.intensityHideTimer) clearTimeout(presentation.intensityHideTimer);
        presentation.intensityHideTimer = null;
        fullscreenPlaybackOverlay?.classList.add('visible');
    }

    function playerUiActive() {
        const activeElement = document.activeElement;
        return presentation.pointerOverCustomControls || presentation.pointerOverFullscreenPlayback
            || [fullscreenPlaybackOverlay, customVideoControls, dropZone, devicesPanel, intensityOverlay]
                .some(element => element && (element.matches(':hover') || element.contains(activeElement)));
    }

    let __controlsVisible = false;

    let __hideTimer = null;

    let __cursorHideTimer = null;


    function clearFullscreenCursor() {
        if (__cursorHideTimer) clearTimeout(__cursorHideTimer);
        __cursorHideTimer = null;
        fullscreenTarget.classList.remove('fullscreen-cursor-hidden', 'fullscreen-cursor-visible');
    }

    function blurFullscreenToolbarFocus() {
        if (document.fullscreenElement !== fullscreenTarget) return;
        const focused = document.activeElement;
        if (focused instanceof HTMLElement && [
            playbackToolbar,
            deviceControls,
            customVideoControls
        ].some(toolbar => toolbar?.contains(focused))) focused.blur();
    }

    function updateFullscreenCursor(overControls = false) {
        if (document.fullscreenElement !== fullscreenTarget) {
            clearFullscreenCursor();
            return;
        }
        if (__cursorHideTimer) clearTimeout(__cursorHideTimer);
        fullscreenTarget.classList.remove('fullscreen-cursor-hidden');
        fullscreenTarget.classList.add('fullscreen-cursor-visible');
        if (overControls) {
            __cursorHideTimer = null;
            return;
        }
        __cursorHideTimer = setTimeout(() => {
            blurFullscreenToolbarFocus();
            if (presentation.fullscreenDeviceControlsVisible) restoreDeviceControls();
            hideControlsNow(true);
            hidePlayerOverlay(true);
            fullscreenTarget.classList.remove('fullscreen-cursor-visible');
            fullscreenTarget.classList.add('fullscreen-cursor-hidden');
            __cursorHideTimer = null;
        }, 650);
    }

    function hideControlsNow(force = false) {
        if (!force && playerUiActive()) return;
        blurFullscreenToolbarFocus();
        videoStage?.classList.remove('controls-visible');
        __controlsVisible = false;
    }

    function scheduleAutoHide(duration = 1500) {
        if (__hideTimer) clearTimeout(__hideTimer);
        __hideTimer = setTimeout(() => {
            hideControlsNow();
            __hideTimer = null;
        }, duration);
    }

    function showControlsTemporarily(duration = 1800) {
        video.controls = false;
        videoStage?.classList.add('controls-visible');
        __controlsVisible = true;
        if (duration == null) {
            if (__hideTimer) clearTimeout(__hideTimer);
            __hideTimer = null;
        } else scheduleAutoHide(duration);
    }

    function updateControlsVisibility(event) {
        if (!video) return;
        if (!event) {
            hideControlsNow();
            return;
        }

        const fullscreen = document.fullscreenElement === fullscreenTarget;
        const bounds = fullscreen
            ? { left: 0, right: window.innerWidth, top: 0, bottom: window.innerHeight }
            : videoStage.getBoundingClientRect();
        const withinStage = event.clientX >= bounds.left && event.clientX <= bounds.right
            && event.clientY >= bounds.top && event.clientY <= bounds.bottom;
        const controlsBounds = customVideoControls?.getBoundingClientRect();
        const overControls = Boolean(customVideoControls?.contains(event.target)
            || fullscreenPlaybackOverlay?.contains(event.target)
            || dropZone?.contains(event.target)
            || devicesPanel?.contains(event.target)
            || intensityOverlay?.contains(event.target)
            || controlsBounds && event.clientX >= controlsBounds.left && event.clientX <= controlsBounds.right
                && event.clientY >= controlsBounds.top && event.clientY <= controlsBounds.bottom);
        const revealHeight = Math.max(72, (customVideoControls?.offsetHeight || 0) + 24);
        const withinRevealZone = withinStage && event.clientY >= bounds.bottom - revealHeight;

        if (fullscreen) {
            updateFullscreenCursor(overControls || withinRevealZone);
            handlePlayerSummaryMovement(overControls);
            if (overControls) showFullscreenToolbar();
        } else {
            clearFullscreenCursor();
            handlePlayerSummaryMovement(overControls, withinStage);
        }

        if (overControls) showControlsTemporarily(null);
        else if (withinRevealZone) showControlsTemporarily(1200);
        else if (withinStage && __controlsVisible) scheduleAutoHide(650);
        else if (!fullscreen && __controlsVisible) {
            hideControlsNow();
        }
    }

    function handleStagePointerLeave() {
        video.controls = false;
        presentation.pointerOverCustomControls = false;
        hideControlsNow();
        if (document.fullscreenElement === fullscreenTarget) {
            fullscreenTarget.classList.remove('fullscreen-cursor-visible');
            fullscreenTarget.classList.add('fullscreen-cursor-hidden');
            resetFullscreenSummary();
            hideFullscreenToolbar();
        } else resetFullscreenSummary();
    }

    let __fsEl = null;

    function onFullscreenChange() {
        video.controls = false;
        renderCustomControls();

        // Attach pointermove directly to the fullscreen element for browsers
        // that do not dispatch document pointer events while in fullscreen.
        const fs = document.fullscreenElement;
        if (fs === fullscreenTarget) {
            presentation.pointerOverCustomControls = false;
            placeFullscreenPlaybackToolbar();
            hideControlsNow();
            updateFullscreenCursor(false);
        }
        else {
            restoreDeviceControls();
            restorePlaybackToolbar();
            clearFullscreenCursor();
            resetFullscreenSummary();
        }
        if (fs && fs !== __fsEl) {
            // remove previous if any
            if (__fsEl) {
                try { __fsEl.removeEventListener('pointermove', updateControlsVisibility, true); } catch {}
                try { __fsEl.removeEventListener('pointerleave', handleStagePointerLeave, true); } catch {}
            }
            __fsEl = fs;
            try { __fsEl.addEventListener('pointermove', updateControlsVisibility, true); } catch {}
            try { __fsEl.addEventListener('pointerleave', handleStagePointerLeave, true); } catch {}
            return;
        }

        if (!fs && __fsEl) {
            try { __fsEl.removeEventListener('pointermove', updateControlsVisibility, true); } catch {}
            try { __fsEl.removeEventListener('pointerleave', handleStagePointerLeave, true); } catch {}
            __fsEl = null;
        }
    }

    function fsInteractionHandler(e) {
        if (!document.fullscreenElement) return;
        showControlsTemporarily();
    }

    function mount() {
        if (playbackToolbarHome) playbackToolbar.before(playbackToolbarHome);
        playlistPanelToggle?.addEventListener('click', event => {
            event.stopPropagation();
            toggleSidePanel(dropZone, playlistPanelToggle);
        });
        devicesPanelToggle?.addEventListener('click', event => {
            event.stopPropagation();
            toggleSidePanel(devicesPanel, devicesPanelToggle);
        });
        fullscreenPlaybackOverlay?.addEventListener('pointerenter', () => {
            presentation.pointerOverFullscreenPlayback = true;
            showFullscreenToolbar();
        });
        fullscreenPlaybackOverlay?.addEventListener('pointerleave', () => {
            presentation.pointerOverFullscreenPlayback = false;
            if (presentation.intensityHideTimer) clearTimeout(presentation.intensityHideTimer);
            presentation.intensityHideTimer = setTimeout(hidePlayerOverlay, 650);
        });
        customVideoControls?.addEventListener('pointerenter', () => {
            presentation.pointerOverCustomControls = true;
            showControlsTemporarily(null);
        });
        customVideoControls?.addEventListener('pointerleave', () => {
            presentation.pointerOverCustomControls = false;
            if (customVideoControls.contains(document.activeElement)) document.activeElement.blur();
            scheduleAutoHide(650);
        });
        customVideoControls?.addEventListener('focusout', () => window.setTimeout(() => {
            if (!customVideoControls.contains(document.activeElement)) scheduleAutoHide();
        }, 0));
        renderCustomControls();
        document.addEventListener('pointermove', updateControlsVisibility, true);
        videoStage.addEventListener('pointerleave', handleStagePointerLeave, true);
        document.addEventListener('fullscreenchange', onFullscreenChange);
        document.addEventListener('touchstart', fsInteractionHandler, { passive: true });
        document.addEventListener('touchmove', fsInteractionHandler, { passive: true });
    }
    return { setSidePanelOpen, hideFullscreenToolbar, showFullscreenToolbar, playerUiActive, clearFullscreenCursor, hideControlsNow, scheduleAutoHide, showControlsTemporarily, updateControlsVisibility, mount };
}

