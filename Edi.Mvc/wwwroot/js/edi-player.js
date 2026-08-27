(() => {
    const videoExtensions = ['.mp4', '.webm', '.avi', '.mkv', '.mov'];
    const playerShell = document.querySelector('.player-shell');
    const video = document.getElementById('videoPlayer');
    const videoStage = document.getElementById('videoStage');
    const fullscreenTarget = videoStage || video;
    if (video) video.controls = false;
    const customVideoControls = document.getElementById('customVideoControls');
    const customPlayPause = document.getElementById('customPlayPause');
    const customPlayIcon = document.getElementById('customPlayIcon');
    const customPauseIcon = document.getElementById('customPauseIcon');
    const customMute = document.getElementById('customMute');
    const customVolume = document.getElementById('customVolume');
    const customVolumeWaves = document.getElementById('customVolumeWaves');
    const customVolumeSlash = document.getElementById('customVolumeSlash');
    const customElapsed = document.getElementById('customElapsed');
    const customDuration = document.getElementById('customDuration');
    const customSeek = document.getElementById('customSeek');
    const customFullscreen = document.getElementById('customFullscreen');
    const fullscreenPlaybackOverlay = document.getElementById('fullscreenPlaybackOverlay');
    const playbackToolbar = document.getElementById('playbackToolbar');
    const fullscreenPlaylistToggle = document.getElementById('fullscreenPlaylistToggle');
    const fullscreenDevicesToggle = document.getElementById('fullscreenDevicesToggle');
    const fullscreenPlaylistPanel = document.getElementById('fullscreenPlaylistPanel');
    const fullscreenDevicesPanel = document.getElementById('fullscreenDevicesPanel');
    const fullscreenPlaylistElement = document.getElementById('fullscreenVideoPlaylist');
    const fullscreenPlaylistCount = document.getElementById('fullscreenPlaylistCount');
    const playbackToolbarHome = playbackToolbar
        ? document.createComment('playback toolbar home')
        : null;
    if (playbackToolbarHome) playbackToolbar.before(playbackToolbarHome);
    let pointerOverFullscreenPlayback = false;
    let activeTooltipButton = null;

    function alignTooltip(button) {
        const bounds = button.getBoundingClientRect();
        const edgeThreshold = Math.min(190, window.innerWidth / 2);
        const alignLeft = bounds.left < edgeThreshold;
        const alignRight = !alignLeft && window.innerWidth - bounds.right < edgeThreshold;
        button.classList.toggle('tooltip-align-left', alignLeft);
        button.classList.toggle('tooltip-align-right', alignRight);
    }

    document.addEventListener('pointerover', event => {
        const button = event.target instanceof Element ? event.target.closest('button[data-tooltip]') : null;
        if (!button || button === activeTooltipButton) return;
        activeTooltipButton = button;
        alignTooltip(button);
    }, true);
    document.addEventListener('pointerout', event => {
        if (!activeTooltipButton
            || event.relatedTarget instanceof Node && activeTooltipButton.contains(event.relatedTarget)) return;
        activeTooltipButton = null;
    }, true);
    document.addEventListener('focusin', event => {
        const button = event.target instanceof Element ? event.target.closest('button[data-tooltip]') : null;
        if (button) alignTooltip(button);
    });
    window.addEventListener('resize', () => {
        if (activeTooltipButton?.isConnected) alignTooltip(activeTooltipButton);
    });
    const fileInput = document.getElementById('mediaFiles');
    const addMediaFiles = document.getElementById('addMediaFiles');
    const dropZone = document.getElementById('fileDrop');
    const workspacePanels = document.getElementById('workspacePanels');
    const playlistPanelContent = document.getElementById('playlistPanelContent');
    const devicesPanelContent = document.getElementById('devicesPanelContent');
    const playlistElement = document.getElementById('videoPlaylist');
    const playlistCount = document.getElementById('playlistCount');
    const collapsedCurrentVideo = document.getElementById('collapsedCurrentVideo');
    const playlistToggleIcon = document.getElementById('playlistToggleIcon');
    const devicesToggleIcon = document.getElementById('devicesToggleIcon');
    const totalPlayback = document.getElementById('totalPlayback');
    const playlistDurationTotal = document.getElementById('playlistDurationTotal');
    const loopModeIndicator = document.getElementById('loopModeIndicator');
    const optionButtons = {
        autoplay: document.getElementById('autoplayToggle'),
        loop: document.getElementById('loopToggle'),
        resume: document.getElementById('resumeToggle'),
        stroker: document.getElementById('strokerToggle'),
        intensity: document.getElementById('intensityToggle')
    };
    const playlist = [];
    const databaseName = 'edi-player';
    const playlistStore = 'playlist';
    const assetStore = 'assets';
    const currentVideoKey = 'edi-player-current-video';
    const playbackOptionsKey = 'edi-player-options';
    const playbackPositionsKey = 'edi-player-positions';
    const videoAudioKey = 'edi-player-video-audio';
    let playbackOptions = readStoredObject(playbackOptionsKey, { autoplay: false, loop: false, resume: false, stroker: false });
    if (!['none', 'video', 'playlist'].includes(playbackOptions.loopMode)) {
        playbackOptions.loopMode = playbackOptions.loop ? 'video' : 'none';
    }
    let savedPositions = readStoredObject(playbackPositionsKey, {});
    // intensity (0-100) persisted locally
    let currentIntensity = Number(localStorage.getItem('edi-player-intensity'));
    if (!Number.isFinite(currentIntensity)) currentIntensity = 50;
    // intensity enabled flag
    let intensityEnabled = readStoredObject(playbackOptionsKey, {}).intensity ?? true;
    let intensityNeedsResync = false;
    let definitions = [];
    let playlistDefinitions = [];
    let hasUploadedAssets = false;
    let currentId = null;
    let draggedId = null;
    let lastGallery = null;
    let suppressPause = false;
    let ediStopped = true;
    let strokerPaused = false;
    let strokerPauseMethod = null;
    let preserveStrokerPauseOnVideoPause = false;
    let suppressNextStrokerVideoClick = false;
    let suppressNextPlaybackVideoClick = false;
    let strokerNeedsResync = false;
    let strokerCommandsPending = 0;
    let commandQueue = Promise.resolve();
    let playbackMilliseconds = 0;
    let playbackStartedAt = null;
    let currentVariantSummary = null;
    const deviceControls = document.querySelector('.variant-toggle-wrap');
    const deviceControlsHome = deviceControls ? document.createComment('device controls home') : null;
    if (deviceControlsHome) deviceControls.before(deviceControlsHome);
    let fullscreenDeviceControlsVisible = false;

    function readStoredObject(key, fallback) {
        try {
            return { ...fallback, ...JSON.parse(localStorage.getItem(key) || '{}') };
        } catch {
            return { ...fallback };
        }

    }

    function restoreVideoAudio() {
        if (!video) return;
        const savedAudio = readStoredObject(videoAudioKey, {});
        const savedVolume = Number(savedAudio.volume);
        if (Number.isFinite(savedVolume)) video.volume = Math.max(0, Math.min(1, savedVolume));
        if (typeof savedAudio.muted === 'boolean') video.muted = savedAudio.muted;
    }

    restoreVideoAudio();
    let lastAudibleVolume = video?.volume > 0 ? video.volume : 1;
    video?.addEventListener('volumechange', () => {
        if (video.volume > 0) lastAudibleVolume = video.volume;
        try {
            localStorage.setItem(videoAudioKey, JSON.stringify({
                volume: video.volume,
                muted: video.muted
            }));
        } catch { }
        renderCustomAudio();
    });

    let customSeekDragging = false;

    function formatMediaTime(seconds) {
        if (!Number.isFinite(seconds) || seconds < 0) return '0:00';
        const wholeSeconds = Math.floor(seconds);
        const hours = Math.floor(wholeSeconds / 3600);
        const minutes = Math.floor(wholeSeconds % 3600 / 60);
        const remainder = wholeSeconds % 60;
        return hours
            ? `${hours}:${String(minutes).padStart(2, '0')}:${String(remainder).padStart(2, '0')}`
            : `${minutes}:${String(remainder).padStart(2, '0')}`;
    }

    function renderCustomAudio() {
        if (!video || !customVolume) return;
        const muted = video.muted || video.volume === 0;
        customVolume.value = String(video.volume);
        customVolume.style.setProperty('--range-progress', `${video.volume * 100}%`);
        customVolumeWaves.removeAttribute('hidden');
        customVolumeSlash.removeAttribute('hidden');
        customVolumeWaves.style.display = muted ? 'none' : '';
        customVolumeSlash.style.display = muted ? '' : 'none';
        customMute.setAttribute('aria-label', muted ? 'Unmute' : 'Mute');
        customMute.setAttribute('aria-pressed', String(muted));
    }

    function renderCustomControls() {
        if (!video || !customSeek) return;
        const hasVideo = Boolean(video.currentSrc || video.getAttribute('src'));
        const duration = Number.isFinite(video.duration) ? Math.max(0, video.duration) : 0;
        customPlayPause.disabled = !hasVideo;
        customFullscreen.disabled = !hasVideo;
        customSeek.disabled = !hasVideo || duration === 0;
        const playing = !video.paused && !video.ended;
        customPlayIcon.removeAttribute('hidden');
        customPauseIcon.removeAttribute('hidden');
        customPlayIcon.style.display = playing ? 'none' : '';
        customPauseIcon.style.display = playing ? '' : 'none';
        customPlayPause.setAttribute('aria-label', playing ? 'Pause' : 'Play');
        customDuration.textContent = formatMediaTime(duration);
        customSeek.max = String(duration);
        if (!customSeekDragging) {
            customSeek.value = String(Math.min(duration, Math.max(0, video.currentTime || 0)));
            customElapsed.textContent = formatMediaTime(video.currentTime);
        }
        customSeek.style.setProperty('--range-progress', `${duration ? Number(customSeek.value) / duration * 100 : 0}%`);
        const fullscreen = document.fullscreenElement === fullscreenTarget;
        customFullscreen.setAttribute('aria-label', fullscreen ? 'Exit fullscreen' : 'Enter fullscreen');
        renderCustomAudio();
    }

    customPlayPause?.addEventListener('click', event => {
        event.currentTarget.blur();
        toggleVideoPlayback();
    });
    customMute?.addEventListener('click', event => {
        event.currentTarget.blur();
        if (video.muted || video.volume === 0) {
            if (video.volume === 0) video.volume = lastAudibleVolume;
            video.muted = false;
        } else {
            lastAudibleVolume = video.volume;
            video.muted = true;
        }
    });
    customVolume?.addEventListener('input', () => {
        video.volume = Math.max(0, Math.min(1, Number(customVolume.value)));
        video.muted = video.volume === 0;
    });
    customSeek?.addEventListener('pointerdown', () => { customSeekDragging = true; });
    customSeek?.addEventListener('input', () => {
        customSeekDragging = true;
        customElapsed.textContent = formatMediaTime(Number(customSeek.value));
        const duration = Number(customSeek.max);
        customSeek.style.setProperty('--range-progress', `${duration ? Number(customSeek.value) / duration * 100 : 0}%`);
    });
    customSeek?.addEventListener('change', () => {
        const targetTime = Number(customSeek.value);
        if (Number.isFinite(targetTime) && Number.isFinite(video.duration)) video.currentTime = targetTime;
        customSeekDragging = false;
        renderCustomControls();
    });
    customSeek?.addEventListener('pointercancel', () => {
        customSeekDragging = false;
        renderCustomControls();
    });
    customFullscreen?.addEventListener('click', event => {
        pointerOverCustomControls = false;
        event.currentTarget.blur();
        const change = document.fullscreenElement === fullscreenTarget
            ? document.exitFullscreen?.()
            : fullscreenTarget.requestFullscreen?.();
        change?.catch(error => report(`Could not toggle fullscreen: ${error.message}`, true));
    });

    function closeFullscreenPanels(except = null) {
        const closedDevicePanel = fullscreenDevicesPanel?.hidden === false
            && except !== fullscreenDevicesPanel;
        [fullscreenPlaylistPanel, fullscreenDevicesPanel].forEach(panel => {
            if (!panel || panel === except) return;
            panel.hidden = true;
        });
        fullscreenPlaylistToggle?.setAttribute(
            'aria-expanded',
            String(fullscreenPlaylistPanel && !fullscreenPlaylistPanel.hidden));
        fullscreenDevicesToggle?.setAttribute(
            'aria-expanded',
            String(fullscreenDevicesPanel && !fullscreenDevicesPanel.hidden));
        if (closedDevicePanel
            && document.fullscreenElement === fullscreenTarget
            && fullscreenDeviceControlsVisible) {
            restoreDeviceControls();
            fullscreenSummaryVisible = true;
            showPlayerSummary();
        }
    }

    function toggleFullscreenPanel(panel, button) {
        const open = panel.hidden;
        closeFullscreenPanels(panel);
        panel.hidden = !open;
        button.setAttribute('aria-expanded', String(open));
        showControlsTemporarily(open ? null : 1200);
    }

    fullscreenPlaylistToggle?.addEventListener('click', event => {
        event.stopPropagation();
        toggleFullscreenPanel(fullscreenPlaylistPanel, fullscreenPlaylistToggle);
    });
    fullscreenDevicesToggle?.addEventListener('click', event => {
        event.stopPropagation();
        toggleFullscreenPanel(fullscreenDevicesPanel, fullscreenDevicesToggle);
    });
    fullscreenPlaybackOverlay?.addEventListener('pointerenter', () => {
        pointerOverFullscreenPlayback = true;
        if (intensityHideTimer) clearTimeout(intensityHideTimer);
        fullscreenPlaybackOverlay.classList.add('visible');
        showControlsTemporarily(null);
    });
    fullscreenPlaybackOverlay?.addEventListener('pointerleave', () => {
        pointerOverFullscreenPlayback = false;
        if (fullscreenPlaylistPanel?.hidden !== false)
            intensityHideTimer = setTimeout(hidePlayerOverlay, 650);
        scheduleAutoHide(900);
    });
    document.addEventListener('pointerdown', event => {
        const insideFullscreenPanel = [
            fullscreenPlaylistPanel,
            fullscreenDevicesPanel,
            fullscreenPlaylistToggle,
            fullscreenDevicesToggle
        ].some(element => element?.contains(event.target));
        if (!insideFullscreenPanel) {
            closeFullscreenPanels();
            if (!pointerOverFullscreenPlayback) {
                if (intensityHideTimer) clearTimeout(intensityHideTimer);
                intensityHideTimer = setTimeout(hidePlayerOverlay, 650);
            }
        }
    });
    customVideoControls?.addEventListener('pointerenter', () => {
        pointerOverCustomControls = true;
        showControlsTemporarily(null);
    });
    customVideoControls?.addEventListener('pointerleave', () => {
        pointerOverCustomControls = false;
        if (customVideoControls.contains(document.activeElement)) document.activeElement.blur();
        scheduleAutoHide(650);
    });
    customVideoControls?.addEventListener('focusout', () => window.setTimeout(() => {
        if (!customVideoControls.contains(document.activeElement)) scheduleAutoHide();
    }, 0));
    renderCustomControls();

    // Intensity overlay element: create once
    const intensityOverlay = document.createElement('div');
    intensityOverlay.className = 'edi-intensity-overlay';
    intensityOverlay.style.position = 'fixed';
    intensityOverlay.style.top = '0';
    intensityOverlay.style.left = '0';
    intensityOverlay.style.padding = '0.7rem 1rem';
    intensityOverlay.style.background = 'rgba(0,0,0,0.65)';
    intensityOverlay.style.color = 'white';
    intensityOverlay.style.borderRadius = '0.45rem';
    intensityOverlay.style.fontSize = '1.5rem';
    intensityOverlay.style.opacity = '0';
    intensityOverlay.style.pointerEvents = 'none';
    intensityOverlay.style.transition = 'opacity 180ms ease-out, transform 180ms ease-out';
    intensityOverlay.style.transform = 'scale(0.96)';
    intensityOverlay.style.zIndex = '1080';
    intensityOverlay.style.backdropFilter = 'blur(4px)';
    intensityOverlay.style.display = 'inline-block';
    intensityOverlay.style.whiteSpace = 'nowrap';
    intensityOverlay.style.maxWidth = 'calc(100vw - 24px)';
    intensityOverlay.style.overflow = 'hidden';
    intensityOverlay.style.textOverflow = 'ellipsis';
    intensityOverlay.textContent = `${currentIntensity}%`;
    // Append overlay to body and position over the video element
    try { document.body.appendChild(intensityOverlay); } catch (e) { /* ignore */ }

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

    let intensityHideTimer = null;
    function hidePlayerOverlay() {
        if (fullscreenDeviceControlsVisible
            || pointerOverFullscreenPlayback
            || fullscreenPlaylistPanel?.hidden === false)
            return;
        if (intensityHideTimer) clearTimeout(intensityHideTimer);
        intensityHideTimer = null;
        intensityOverlay.style.opacity = '0';
        intensityOverlay.style.transform = 'scale(0.96)';
        fullscreenPlaybackOverlay?.classList.remove('visible');
    }

    function showPlayerOverlay(content, duration = 1000) {
        if (fullscreenDeviceControlsVisible) return;
        intensityOverlay.replaceChildren();
        if (content?.nodeType) intensityOverlay.append(content);
        else intensityOverlay.textContent = String(content ?? '');
        positionIntensityOverlay();
        intensityOverlay.style.transform = 'scale(1)';
        intensityOverlay.style.opacity = '1';
        if (document.fullscreenElement === fullscreenTarget)
            fullscreenPlaybackOverlay?.classList.add('visible');
        if (intensityHideTimer) clearTimeout(intensityHideTimer);
        intensityHideTimer = duration == null ? null : setTimeout(hidePlayerOverlay, duration);
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

    function variantSummaryText() {
        if (!currentVariantSummary?.length) return '';
        const values = currentVariantSummary.slice(0, 3).map(state => {
            const prefix = state.side ? `${state.side}: ` : '';
            return `${prefix}${shortenOverlayText(state.variant, 18)}`;
        });
        if (currentVariantSummary.length > 3) values.push('...');
        return values.join(' · ');
    }

    function createFullscreenSummary() {
        const summary = document.createElement('span');
        summary.style.display = 'inline-flex';
        summary.style.alignItems = 'center';
        summary.style.gap = '0.65rem';
        summary.append(createStrokerStateIcon(strokerPaused || video.paused || video.ended));

        const intensity = document.createElement('span');
        intensity.textContent = `${currentIntensity}%`;
        if (currentIntensity === 0) intensity.style.color = 'var(--bs-danger, #dc3545)';
        summary.append(intensity);

        const variantText = variantSummaryText();
        if (variantText) {
            const variant = document.createElement('span');
            variant.textContent = variantText;
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
        if (fullscreenDeviceControlsVisible && deviceControls && deviceControlsHome?.parentNode) {
            deviceControlsHome.after(deviceControls);
        }
        deviceControls?.classList.remove('fullscreen-device-controls');
        fullscreenDeviceControlsVisible = false;
        intensityOverlay.style.pointerEvents = 'none';
        intensityOverlay.style.overflow = 'hidden';
    }

    function placeFullscreenPlaybackToolbar() {
        if (playbackToolbar && fullscreenPlaybackOverlay
            && playbackToolbar.parentElement !== fullscreenPlaybackOverlay) {
            fullscreenPlaybackOverlay.prepend(playbackToolbar);
        }
    }

    function restorePlaybackToolbar() {
        if (playbackToolbar && playbackToolbarHome?.parentNode)
            playbackToolbarHome.after(playbackToolbar);
        fullscreenPlaybackOverlay?.classList.remove('visible');
        pointerOverFullscreenPlayback = false;
    }

    function showFullscreenDeviceControls() {
        if (document.fullscreenElement !== fullscreenTarget || !deviceControls || fullscreenDeviceControlsVisible) return;
        fullscreenDeviceControlsVisible = true;
        if (intensityHideTimer) clearTimeout(intensityHideTimer);
        intensityHideTimer = null;
        intensityOverlay.replaceChildren(deviceControls);
        deviceControls.classList.add('fullscreen-device-controls');
        intensityOverlay.style.pointerEvents = 'auto';
        intensityOverlay.style.overflow = 'visible';
        intensityOverlay.style.transform = 'scale(1)';
        intensityOverlay.style.opacity = '1';
    }

    intensityOverlay.addEventListener('pointerenter', showFullscreenDeviceControls);
    intensityOverlay.addEventListener('pointerleave', () => {
        if (!fullscreenDeviceControlsVisible
            || !document.getElementById('variantTogglePanel')?.hidden
            || fullscreenDevicesPanel?.hidden === false)
            return;
        restoreDeviceControls();
        fullscreenSummaryVisible = true;
        showPlayerSummary(true);
    });

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
        if (fullscreenDeviceControlsVisible) return;
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

    document.addEventListener('edi-variant-state', event => {
        currentVariantSummary = Array.isArray(event.detail?.devices)
            ? event.detail.devices.filter(state => state?.variant)
            : [];
        if (fullscreenSummaryVisible && !fullscreenDeviceControlsVisible)
            showPlayerSummary(fullscreenSummaryOverControls);
    });

    document.addEventListener('edi-variant-switched', event => {
        if (!Array.isArray(event.detail?.devices)) return;
        currentVariantSummary = event.detail.devices.filter(state => state?.variant);
        const summary = variantSummaryText();
        if (!summary) return;
        showPlayerOverlay(createIconOverlayContent(
            document.getElementById('variantToggle'),
            summary));
    });

    document.addEventListener('edi-variant-panel', event => {
        if (event.detail?.open && !fullscreenDeviceControlsVisible) resetFullscreenSummary();
    });

    async function setIntensity(value) {
        const clamped = Math.max(0, Math.min(100, Math.round(value)));
        currentIntensity = clamped;
        localStorage.setItem('edi-player-intensity', String(clamped));
        renderPlaybackOptions();
        showIntensityOverlay(clamped);
        try {
            const changed = await window.ediDeviceControls?.setRange(clamped);
            if (!changed) throw new Error('No device has Range enabled.');
            intensityNeedsResync = false;
        } catch (error) {
            intensityNeedsResync = false;
            report(`Could not set device range: ${error.message}`, true);
        }
    }

    // Scroll-to-intensity: acceleration based on speed of scroll events
    let lastScrollTime = 0;
    function handleIntensityScroll(event) {
        // check target is video or we are fullscreen
        const inFullscreen = document.fullscreenElement != null;
        const overVideo = video && (video.contains(event.target) || event.target === video
            || (inFullscreen && document.fullscreenElement === fullscreenTarget));
        if (!overVideo) return;
        if (!intensityEnabled) return;

        const now = performance.now();
        const deltaTime = lastScrollTime ? Math.max(1, now - lastScrollTime) : 1000;
        lastScrollTime = now;

        // Determine base step according to spacing
        let step = 3;
        if (deltaTime > 600) step = 1; // very spaced: move 1
        else if (deltaTime > 250) step = 3; // spaced: move 3
        else {
            // fast: scale between 3 and 15
            const factor = (250 - deltaTime) / 250; // 0..~1
            step = 3 + Math.round(factor * (15 - 3));
            step = Math.min(15, Math.max(3, step));
        }

        // Wheel deltaY: positive typically means scroll down -> decrease intensity
        const direction = event.deltaY > 0 ? -1 : 1;
        // Some mice produce large deltaY; normalize by sign only
        const delta = direction * step;
        const tentative = currentIntensity + delta;

        // If we're already at the boundary and the user scrolls further in that
        // direction, allow the event to propagate to the page (do not preventDefault).
        if ((currentIntensity >= 100 && tentative > 100 && direction > 0) ||
            (currentIntensity <= 0 && tentative < 0 && direction < 0)) {
            // let the browser handle the scroll (propagate)
            return;
        }

        // Otherwise, consume the event and apply clamped intensity
        event.preventDefault();
        event.stopPropagation();

        const newIntensity = Math.max(0, Math.min(100, tentative));
        if (newIntensity === currentIntensity) return;
        void setIntensity(newIntensity);
    }

    // Attach wheel handlers to video and document (to catch fullscreen)
    try {
        video.addEventListener('wheel', handleIntensityScroll, { passive: false });
    } catch (e) { }
    document.addEventListener('wheel', handleIntensityScroll, { passive: false });

    // Reposition overlay on fullscreen change / resize / scroll
    document.addEventListener('fullscreenchange', () => positionIntensityOverlay());
    window.addEventListener('resize', () => positionIntensityOverlay());
    // capture scrolling in page so overlay follows video
    document.addEventListener('scroll', () => positionIntensityOverlay(), true);
    // reposition when video metadata changes (size may change)
    video.addEventListener('loadedmetadata', () => positionIntensityOverlay());


    function renderPlaybackOptions() {
        Object.entries(optionButtons).forEach(([name, button]) => {
            const enabled = name === 'loop' ? playbackOptions.loopMode !== 'none' : playbackOptions[name] === true;
            const paused = name === 'stroker' && strokerPaused;
            const intensityAtZero = name === 'intensity' && currentIntensity === 0;
            const zeroIntensity = intensityAtZero && intensityEnabled;
            button.setAttribute('aria-pressed', String(enabled));
            button.classList.toggle('btn-primary', enabled && !paused && !zeroIntensity);
            button.classList.toggle('btn-danger', paused || zeroIntensity);
            button.classList.remove('btn-outline-danger');
            button.classList.toggle('btn-outline-secondary', !enabled && !paused && !zeroIntensity);
            button.classList.toggle('intensity-zero-disabled', intensityAtZero && !intensityEnabled);
            button.classList.toggle('stroker-paused', paused);
            if (name === 'loop') {
                const mode = playbackOptions.loopMode;
                const label = mode === 'video' ? 'Repeat video' : mode === 'playlist' ? 'Repeat playlist' : 'Repeat off';
                button.dataset.tooltip = label;
                button.removeAttribute('title');
                button.setAttribute('aria-label', label);
                loopModeIndicator.textContent = mode === 'video' ? '1' : mode === 'playlist' ? '≡' : '';
            }
            if (name === 'intensity') {
                // intensity button reflects enabled flag
                const ie = intensityEnabled === true;
                button.setAttribute('aria-pressed', String(ie));
                button.classList.toggle('btn-primary', ie && !zeroIntensity);
                button.classList.toggle('btn-danger', zeroIntensity);
                button.classList.remove('btn-outline-danger');
                button.classList.toggle('btn-outline-secondary', !ie && !zeroIntensity);
            }
            if (name === 'stroker') {
                const label = paused
                    ? 'Click or Space: resume devices'
                    : enabled
                        ? 'Click or Space: pause devices'
                        : 'Click or Space: pause playback';
                button.dataset.tooltip = label;
                button.removeAttribute('title');
                button.setAttribute('aria-label', label);
            }
        });
    }

    function persistPositions() {
        localStorage.setItem(playbackPositionsKey, JSON.stringify(savedPositions));
    }

    function clearSavedPosition(id) {
        if (!id || !(id in savedPositions)) return;
        delete savedPositions[id];
        persistPositions();
    }

    function saveCurrentPosition() {
        if (!playbackOptions.resume || !currentId || !Number.isFinite(video.currentTime)) return;
        if (video.ended || video.currentTime <= 0.25) {
            clearSavedPosition(currentId);
            return;
        }
        savedPositions[currentId] = video.currentTime;
        persistPositions();
    }

    function restorePosition(id) {
        if (!playbackOptions.resume) return;
        const savedPosition = Number(savedPositions[id]);
        if (!(savedPosition > 0)) return;

        const applyPosition = () => {
            if (currentId !== id) return;
            if (Number.isFinite(video.duration) && savedPosition >= video.duration - 1) {
                clearSavedPosition(id);
                return;
            }
            video.currentTime = savedPosition;
        };

        if (video.readyState >= 1) applyPosition();
        else video.addEventListener('loadedmetadata', applyPosition, { once: true });
    }

    function togglePlaybackOption(name) {
        if (name === 'loop') {
            playbackOptions.loopMode = playbackOptions.loopMode === 'none'
                ? 'video'
                : playbackOptions.loopMode === 'video' ? 'playlist' : 'none';
            playbackOptions.loop = playbackOptions.loopMode === 'video';
            localStorage.setItem(playbackOptionsKey, JSON.stringify(playbackOptions));
            renderPlaybackOptions();
            return;
        }
        playbackOptions[name] = playbackOptions[name] !== true;
        localStorage.setItem(playbackOptionsKey, JSON.stringify(playbackOptions));
        if (name === 'resume') {
            if (playbackOptions.resume) saveCurrentPosition();
            else {
                savedPositions = {};
                localStorage.removeItem(playbackPositionsKey);
            }
        }
        if (name === 'intensity') {
            intensityEnabled = !intensityEnabled;
        }
        if (name === 'stroker' && !playbackOptions.stroker && strokerPaused) {
            toggleStrokerPlayback();
        }
        renderPlaybackOptions();
    }

    const extension = name => {
        const dot = name.lastIndexOf('.');
        return dot >= 0 ? name.slice(dot).toLowerCase() : '';
    };

    const fileStem = name => {
        const dot = name.lastIndexOf('.');
        return (dot >= 0 ? name.slice(0, dot) : name).toLowerCase();
    };

    const isVideo = file => file.type.startsWith('video/') || videoExtensions.includes(extension(file.name));
    const isFileDrag = event => Array.from(event.dataTransfer?.types || []).includes('Files');
    const isEdiAsset = file => {
        const name = file.name.toLowerCase();
        return name.endsWith('.funscript')
            || name.endsWith('.mp3')
            || name === 'definitions.csv'
            || name === 'definitions_auto.csv'
            || name.startsWith('bundledefinition') && name.endsWith('.txt');
    };

    async function api(path, options = {}) {
        const response = await fetch(path, options);
        if (!response.ok) {
            const detail = await response.text();
            throw new Error(detail || `${response.status} ${response.statusText}`);
        }
        return response;
    }

    async function confirmedPlaybackCommand(path) {
        const controller = new AbortController();
        const timeout = window.setTimeout(() => controller.abort(), 5000);
        try {
            return await api(path, { method: 'POST', signal: controller.signal });
        } catch (error) {
            if (controller.signal.aborted) {
                throw new Error('The EDI server did not confirm the command within 5 seconds.');
            }
            throw error;
        } finally {
            window.clearTimeout(timeout);
        }
    }

    function report(message, isError = false) {
        if (isError) console.error(message);
    }

    function enqueueCommand(command) {
        commandQueue = commandQueue
            .then(command)
            .catch(error => report(`Error EDI: ${error.message}`, true));
        return commandQueue;
    }

    function releaseVideoControlFocus() {
        window.setTimeout(() => {
            if (document.activeElement !== video) return;
            video.blur();
            playerShell.focus({ preventScroll: true });
        }, 0);
    }

    function handleVideoMouseDown(event) {
        releaseVideoControlFocus();
        if (event.button !== 0) return;

        if (!playbackOptions.stroker) {
            suppressNextPlaybackVideoClick = true;
            toggleVideoPlayback();
            event.preventDefault();
            event.stopImmediatePropagation();
            return;
        }

        event.preventDefault();
        event.stopImmediatePropagation();
        suppressNextStrokerVideoClick = true;
        handleStrokerInput('mouse', true);
    }
    function handleVideoClick(event) {
        releaseVideoControlFocus();
        if (event.button !== 0) return;

        // If stroker option is enabled, keep the existing stroker click behavior
        if (playbackOptions.stroker) {
            if (suppressNextStrokerVideoClick) {
                suppressNextStrokerVideoClick = false;
                event.preventDefault();
                event.stopImmediatePropagation();
                return;
            }
            if (video.paused) handleStrokerInput('mouse', true);
            event.preventDefault();
            event.stopImmediatePropagation();
            return;
        }

        if (suppressNextPlaybackVideoClick) {
            suppressNextPlaybackVideoClick = false;
        } else {
            toggleVideoPlayback();
        }

        event.preventDefault();
        event.stopImmediatePropagation();
    }

    function toggleVideoPlayback() {
        try {
            const change = video.paused || video.ended ? video.play() : (video.pause(), null);
            change?.catch(error => report(`Could not change playback: ${error.message}`, true));
        } catch (error) {
            report(`Could not change playback: ${error.message}`, true);
        }
    }



    function toggleVideoFullscreen(event) {
        event.preventDefault();
        event.stopImmediatePropagation();

        const fullscreenChange = document.fullscreenElement === fullscreenTarget
            ? document.exitFullscreen?.()
            : fullscreenTarget.requestFullscreen?.();
        fullscreenChange?.catch(error => report(`Could not toggle fullscreen: ${error.message}`, true));
    }

    let __controlsVisible = false;
    let __hideTimer = null;
    let __cursorHideTimer = null;
    let pointerOverCustomControls = false;
    function clearFullscreenCursor() {
        if (__cursorHideTimer) clearTimeout(__cursorHideTimer);
        __cursorHideTimer = null;
        fullscreenTarget.classList.remove('fullscreen-cursor-hidden', 'fullscreen-cursor-visible');
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
            fullscreenTarget.classList.remove('fullscreen-cursor-visible');
            fullscreenTarget.classList.add('fullscreen-cursor-hidden');
            __cursorHideTimer = null;
        }, 650);
    }
    function hideControlsNow() {
        if (pointerOverCustomControls) return;
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
            || intensityOverlay?.contains(event.target)
            || controlsBounds && event.clientX >= controlsBounds.left && event.clientX <= controlsBounds.right
                && event.clientY >= controlsBounds.top && event.clientY <= controlsBounds.bottom);
        const revealHeight = Math.max(72, (customVideoControls?.offsetHeight || 0) + 24);
        const withinRevealZone = withinStage && event.clientY >= bounds.bottom - revealHeight;

        if (fullscreen) {
            updateFullscreenCursor(overControls || withinRevealZone);
            handlePlayerSummaryMovement(overControls);
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

    function currentItem() {
        return playlist.find(item => item.id === currentId) || null;
    }

    function ediDuration(item) {
        const stem = fileStem(item.name);
        const endTimes = playlistDefinitions
            .filter(definition => fileStem(definition.fileName || '') === stem)
            .map(definition => Number(definition.endTime))
            .filter(Number.isFinite);
        return endTimes.length ? Math.max(...endTimes) : null;
    }

    function formatDuration(milliseconds) {
        if (!Number.isFinite(milliseconds)) return '—';
        const totalSeconds = Math.max(0, Math.floor(milliseconds / 1000));
        const hours = Math.floor(totalSeconds / 3600);
        const minutes = Math.floor(totalSeconds % 3600 / 60);
        const seconds = totalSeconds % 60;
        return hours
            ? `${hours}:${String(minutes).padStart(2, '0')}:${String(seconds).padStart(2, '0')}`
            : `${minutes}:${String(seconds).padStart(2, '0')}`;
    }

    function formatCompactDuration(milliseconds) {
        return formatDuration(milliseconds);
    }

    function renderTotalPlayback() {
        if (!totalPlayback) return;
        const running = playbackStartedAt === null ? 0 : performance.now() - playbackStartedAt;
        totalPlayback.textContent = formatCompactDuration(playbackMilliseconds + running);
    }

    function startPlaybackTimer() {
        if (playbackStartedAt !== null || video.paused || video.ended) return;
        playbackStartedAt = performance.now();
        renderTotalPlayback();
    }

    function stopPlaybackTimer() {
        if (playbackStartedAt !== null) {
            playbackMilliseconds += performance.now() - playbackStartedAt;
            playbackStartedAt = null;
        }
        renderTotalPlayback();
    }

    function openDatabase() {
        return new Promise((resolve, reject) => {
            const request = indexedDB.open(databaseName, 2);
            request.onupgradeneeded = () => {
                if (!request.result.objectStoreNames.contains(playlistStore)) {
                    request.result.createObjectStore(playlistStore, { keyPath: 'id' });
                }
                if (!request.result.objectStoreNames.contains(assetStore)) {
                    request.result.createObjectStore(assetStore, { keyPath: 'name' });
                }
            };
            request.onsuccess = () => resolve(request.result);
            request.onerror = () => reject(request.error);
        });
    }

    async function withStore(storeName, mode, operation) {
        const database = await openDatabase();
        return new Promise((resolve, reject) => {
            const transaction = database.transaction(storeName, mode);
            const store = transaction.objectStore(storeName);
            operation(store);
            transaction.oncomplete = () => {
                database.close();
                resolve();
            };
            transaction.onerror = () => {
                database.close();
                reject(transaction.error);
            };
        });
    }

    function clearStoredVideos() {
        return withStore(playlistStore, 'readwrite', store => {
            store.clear();
        });
    }

    async function getStoredItems(storeName) {
        const database = await openDatabase();
        return new Promise((resolve, reject) => {
            const transaction = database.transaction(storeName, 'readonly');
            const request = transaction.objectStore(storeName).getAll();
            request.onsuccess = () => resolve(request.result);
            request.onerror = () => reject(request.error);
            transaction.oncomplete = () => database.close();
        });
    }

    function saveAssets(files) {
        return withStore(assetStore, 'readwrite', store => {
            store.clear();
            files
                .filter(file => file.name.toLowerCase() !== 'definitions_auto.csv')
                .forEach(file => store.put({ name: file.name, file }));
        });
    }

    async function mergeAssets(files) {
        const records = await getStoredItems(assetStore);
        const merged = new Map(records
            .map(record => record.file)
            .filter(file => file.name.toLowerCase() !== 'definitions_auto.csv')
            .map(file => [file.name.toLowerCase(), file]));
        files
            .filter(file => file.name.toLowerCase() !== 'definitions_auto.csv')
            .forEach(file => merged.set(file.name.toLowerCase(), file));
        return [...merged.values()];
    }

    function assetsForCurrentPlaylist(files) {
        const videoStems = playlist.map(item => fileStem(item.name));
        if (!videoStems.length) return [];
        return files.filter(file => {
            const name = file.name.toLowerCase();
            if (name === 'definitions.csv' || name.startsWith('bundledefinition') && name.endsWith('.txt')) return true;
            const assetStem = fileStem(name);
            return videoStems.some(videoStem => assetStem === videoStem || assetStem.startsWith(`${videoStem}.`));
        });
    }

    function csvValue(value) {
        const text = String(value ?? '');
        return /[",\r\n]/.test(text)
            ? `"${text.replaceAll('"', '""')}"`
            : text;
    }

    function activeDefinitionsFile(item) {
        const stem = fileStem(item?.name || '');
        const active = playlistDefinitions.filter(definition =>
            fileStem(definition.fileName || '') === stem);
        const rows = active.map(definition => [
            definition.name,
            definition.fileName,
            definition.startTime,
            definition.endTime,
            definition.type,
            definition.loop,
            definition.description
        ].map(csvValue).join(','));
        const csv = [
            'Name,FileName,StartTime,EndTime,Type,Loop,Description',
            ...rows
        ].join('\r\n');
        return new File([csv], 'Definitions.csv', { type: 'text/csv' });
    }

    async function activateDefinitionsFor(item) {
        if (!hasUploadedAssets || !item) return;
        const form = new FormData();
        form.append('file', activeDefinitionsFile(item), 'Definitions.csv');
        const response = await api('/Edi/Definitions/Active', {
            method: 'POST',
            body: form
        });
        definitions = await response.json();
        renderPlaylist();
        document.dispatchEvent(new CustomEvent('edi-devices-refresh-requested'));
    }

    function currentDefinition() {
        const item = currentItem();
        if (!item) return null;
        const stem = fileStem(item.name);
        const position = Math.round(video.currentTime * 1000);
        return definitions.find(definition =>
            fileStem(definition.fileName || '') === stem
            && position >= definition.startTime
            && position <= definition.endTime);
    }

    async function syncEdi(force = false) {
        if (video.paused || !currentItem()) return false;
        const definition = currentDefinition();
        if (!definition) {
            lastGallery = null;
            report('Video is playing but no EDI definition for this time.');
            return false;
        }
        if (!force && lastGallery === definition.name) return true;

        const seek = Math.max(0, Math.round(video.currentTime * 1000) - definition.startTime);
        await confirmedPlaybackCommand(`/Edi/Play/${encodeURIComponent(definition.name)}?seek=${seek}`);
        lastGallery = definition.name;
        report(`EDI synced: ${definition.name}`);
        return true;
    }

    function startEdi() {
        if (video.paused || video.ended || video.seeking) return commandQueue;
        if (strokerPaused) {
            if (lastGallery !== currentDefinition()?.name) strokerNeedsResync = true;
            return commandQueue;
        }
        if (!ediStopped && lastGallery === currentDefinition()?.name) return commandQueue;
        const force = ediStopped;
        ediStopped = false;
        return enqueueCommand(async () => {
            try {
                if (video.paused || video.ended || video.seeking) {
                    ediStopped = true;
                    return;
                }
                const synced = await syncEdi(force);
                ediStopped = !synced;
                intensityNeedsResync = false;
            } catch (error) {
                ediStopped = true;
                throw error;
            }
        });
    }

    function resyncAfterAssetsReload() {
        ediStopped = true;
        lastGallery = null;
        intensityNeedsResync = false;
        if (strokerPaused) {
            strokerNeedsResync = true;
            renderPlaybackOptions();
            return commandQueue;
        }
        if (video.paused || video.ended || video.seeking) return commandQueue;
        return startEdi();
    }

    function resumeVideoAndStroker() {
        const restoreDevices = strokerPaused;
        strokerPaused = false;
        strokerPauseMethod = null;
        strokerNeedsResync = false;
        ediStopped = true;
        lastGallery = null;
        renderPlaybackOptions();
        showStrokerStateOverlay(false);
        if (restoreDevices) {
            window.ediDeviceControls?.setPaused(false)
                .catch(error => report(`Could not restore paused devices: ${error.message}`, true));
        }
        video.play().catch(error => report(`Could not start playback: ${error.message}`, true));
    }

    function handleStrokerInput(method, queueWhilePending = false) {
        if (video.paused) {
            resumeVideoAndStroker();
            return commandQueue;
        }
        if (strokerPaused && strokerPauseMethod && strokerPauseMethod !== method) {
            strokerPauseMethod = method;
            preserveStrokerPauseOnVideoPause = true;
            video.pause();
            report('Video paused; stroker remains paused.');
            return commandQueue;
        }
        return toggleStrokerPlayback(queueWhilePending, method);
    }

    function toggleStrokerPlayback(queueWhilePending = false, method = null) {
        if (strokerCommandsPending > 0 && !queueWhilePending) return commandQueue;

        const pauseStroker = !strokerPaused;
        const previousPauseMethod = strokerPauseMethod;
        strokerPaused = pauseStroker;
        strokerPauseMethod = pauseStroker ? method : null;
        strokerCommandsPending++;
        if (pauseStroker) strokerNeedsResync = false;
        renderPlaybackOptions();
        showStrokerStateOverlay(pauseStroker);

        return enqueueCommand(async () => {
            try {
                const changed = await window.ediDeviceControls
                    ?.setPaused(pauseStroker);
                if (!changed)
                    throw new Error('No device has Pause enabled.');

                intensityNeedsResync = false;
                strokerNeedsResync = false;
            } catch (error) {
                strokerPaused = !pauseStroker;
                strokerPauseMethod = previousPauseMethod;
                showStrokerStateOverlay(strokerPaused);
                throw error;
            } finally {
                strokerCommandsPending--;
                renderPlaybackOptions();
            }
        });
    }

    function stopEdi(reason = 'Playback stopped.') {
        const restoreDevices = strokerPaused;
        strokerPaused = false;
        strokerPauseMethod = null;
        strokerNeedsResync = false;
        renderPlaybackOptions();
        if (ediStopped) {
            report(reason);
            return restoreDevices
                ? enqueueCommand(() => window.ediDeviceControls
                    ?.setPaused(false) ?? Promise.resolve())
                : commandQueue;
        }
        ediStopped = true;
        lastGallery = null;
        return enqueueCommand(async () => {
            try {
                await confirmedPlaybackCommand('/Edi/Stop');
                if (restoreDevices)
                    await window.ediDeviceControls?.setPaused(false);
                report(reason);
            } finally {
                ediStopped = true;
                lastGallery = null;
            }
        });
    }

    async function selectVideo(id, autoplay = false) {
        const item = playlist.find(entry => entry.id === id);
        if (!item) return;
        if (currentId === id) {
            if (autoplay && video.paused) await video.play();
            return;
        }
        stopPlaybackTimer();
        saveCurrentPosition();
        await stopEdi('Previous video stopped.');
        currentId = id;
        localStorage.setItem(currentVideoKey, id);
        lastGallery = null;
        await activateDefinitionsFor(item);
        video.src = item.url;
        restorePosition(id);
        renderPlaylist();
        report(`Ready: ${item.name}`);
        if (autoplay) await video.play();
    }

    async function deleteVideo(id) {
        const index = playlist.findIndex(item => item.id === id);
        if (index < 0) return;

        const [removed] = playlist.splice(index, 1);
        clearSavedPosition(id);
        URL.revokeObjectURL(removed.url);
        if (currentId === id) {
            await stopEdi('Video deleted; EDI stopped.');
            currentId = null;
            localStorage.removeItem(currentVideoKey);
            video.removeAttribute('src');
            video.load();
            if (playlist.length) {
                await selectVideo(playlist[Math.min(index, playlist.length - 1)].id);
            }
        }

        renderPlaylist();
        report(`Deleted: ${removed.name}`);
    }

    async function clearPlaylist() {
        if (!window.confirm('Delete the playlist and all EDI assets?')) return;
        suppressPause = true;
        video.pause();
        suppressPause = false;
        await withStore(assetStore, 'readwrite', store => store.clear());
        await api('/Edi/Assets', { method: 'DELETE' });
        definitions = [];
        playlistDefinitions = [];
        hasUploadedAssets = false;
        ediStopped = true;
        lastGallery = null;
        playlist.forEach(item => URL.revokeObjectURL(item.url));
        playlist.length = 0;
        currentId = null;
        localStorage.removeItem(currentVideoKey);
        savedPositions = {};
        localStorage.removeItem(playbackPositionsKey);
        video.removeAttribute('src');
        video.load();
        renderPlaylist();
        await clearStoredVideos();
        report('Playlist and EDI assets deleted.');
    }

    function appendButtonIcon(button, pathData) {
        const svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
        svg.setAttribute('class', 'player-option-icon utility-icon');
        svg.setAttribute('viewBox', '0 0 24 24');
        svg.setAttribute('aria-hidden', 'true');
        const path = document.createElementNS('http://www.w3.org/2000/svg', 'path');
        path.setAttribute('d', pathData);
        svg.append(path);
        button.append(svg);
    }

    function renderCollapsedCurrentVideo() {
        if (!collapsedCurrentVideo) return;
        const item = currentItem();
        collapsedCurrentVideo.textContent = item ? `Current: ${item.name}` : 'Current: No video selected';
        collapsedCurrentVideo.hidden = !playlistPanelContent.hidden;
    }

    function renderPlaylist() {
        playlistElement.classList.toggle('playlist-scroll', playlist.length > 10);
        playlistCount.textContent = `${playlist.length} ${playlist.length === 1 ? 'video' : 'videos'}`;
        if (fullscreenPlaylistCount) fullscreenPlaylistCount.textContent = playlistCount.textContent;
        const durations = playlist.map(ediDuration).filter(Number.isFinite);
        playlistDurationTotal.textContent = formatCompactDuration(durations.reduce((total, duration) => total + duration, 0));
        renderCollapsedCurrentVideo();

        const renderInto = (element, compact = false) => {
            if (!element) return;
            element.replaceChildren();
            if (!playlist.length) {
                const empty = document.createElement('li');
                empty.className = 'p-3 text-muted';
                empty.textContent = 'You have not added videos yet.';
                element.append(empty);
                return;
            }

            playlist.forEach(item => {
            const row = document.createElement('li');
            row.className = `playlist-item${item.id === currentId ? ' active' : ''}`;
            row.draggable = !compact;

            const handle = document.createElement('span');
            handle.textContent = '↕';
            handle.className = 'text-muted';
            handle.title = 'Drag to reorder';

            const name = document.createElement('span');
            name.className = 'playlist-name';
            name.textContent = item.name;

            const duration = document.createElement('span');
            duration.className = 'playlist-duration';
            duration.textContent = formatDuration(ediDuration(item));

            const remove = document.createElement('button');
            remove.type = 'button';
            remove.className = 'btn btn-sm btn-outline-danger icon-button';
            appendButtonIcon(remove, 'M3.5 6.5h17M9 6.5V4h6v2.5M6.5 6.5l1 13h9l1-13M10 10v6M14 10v6');
            remove.dataset.tooltip = 'Delete video';
            remove.setAttribute('aria-label', `Delete ${item.name}`);
            remove.addEventListener('click', event => {
                event.stopPropagation();
                deleteVideo(item.id).catch(error => report(`Could not delete the video: ${error.message}`, true));
            });

            const actions = document.createElement('div');
            actions.className = 'playlist-item-actions';
            actions.append(remove);
            row.append(...(compact
                ? [name, duration]
                : [handle, name, duration, actions]));
            row.addEventListener('click', () => selectVideo(item.id, playbackOptions.autoplay)
                .then(() => compact && closeFullscreenPanels())
                .catch(error => report(`Could not start playback: ${error.message}`, true)));
            if (!compact) {
                row.addEventListener('dragstart', () => {
                    draggedId = item.id;
                    row.classList.add('dragging');
                });
                row.addEventListener('dragend', () => {
                    draggedId = null;
                    row.classList.remove('dragging');
                });
                row.addEventListener('dragover', event => event.preventDefault());
                row.addEventListener('drop', event => {
                    event.preventDefault();
                    const from = playlist.findIndex(entry => entry.id === draggedId);
                    const to = playlist.findIndex(entry => entry.id === item.id);
                    if (from < 0 || to < 0 || from === to) return;
                    const [moved] = playlist.splice(from, 1);
                    playlist.splice(to, 0, moved);
                    renderPlaylist();
                });
            }
            element.append(row);
            });
        };

        renderInto(playlistElement);
        renderInto(fullscreenPlaylistElement, true);
    }

    async function uploadAssets(files) {
        const form = new FormData();
        files.forEach(file => form.append('files', file, file.name));
        const preserveStrokerPause = strokerPaused;
        await stopEdi('Updating EDI assets...');
        const response = await api('/Edi/Assets', { method: 'POST', body: form });
        playlistDefinitions = await response.json();
        definitions = [...playlistDefinitions];
        hasUploadedAssets = true;
        await activateDefinitionsFor(currentItem());
        renderPlaylist();
        document.dispatchEvent(new CustomEvent('edi-devices-refresh-requested'));
        if (preserveStrokerPause) {
            await window.ediDeviceControls?.setPaused(true);
            strokerPaused = true;
            strokerNeedsResync = true;
            renderPlaybackOptions();
        }
        await resyncAfterAssetsReload();
    }

    document.addEventListener('edi-assets-reloaded', () => {
        void (async () => {
            try {
                definitions = await (await api('/Edi/Definitions')).json();
                renderPlaylist();
                await resyncAfterAssetsReload();
            } catch (error) {
                report(`Could not resynchronize EDI after reloading assets: ${error.message}`, true);
            }
        })();
    });

    async function recoverUploadedAssets() {
        const paths = await (await api('/Edi/Assets')).json();
        const uploadPaths = paths.filter(path => {
            if (typeof path !== 'string' || !path.toLowerCase().startsWith('/edi/upload/')) return false;
            const name = decodeURIComponent(path.split('/').pop() || '');
            return name.toLowerCase() !== 'definitions_auto.csv' && isEdiAsset({ name });
        });
        return Promise.all(uploadPaths.map(async path => {
            const response = await api(path);
            const name = decodeURIComponent(path.split('/').pop() || 'asset');
            return new File([await response.blob()], name);
        }));
    }

    async function reloadAssets() {
        try {
            report('Reloading EDI assets...');
            const assets = assetsForCurrentPlaylist(await mergeAssets(await recoverUploadedAssets()));
            if (!assets.length) {
                await loadDefinitions();
                report('No saved assets match the current playlist.', true);
                return;
            }
            await uploadAssets(assets);
            report(`${assets.length} EDI asset${assets.length === 1 ? '' : 's'} reloaded.`);
        } catch (error) {
            report(`Could not reload assets: ${error.message}`, true);
        }
    }

    async function addFiles(fileList) {
        const files = Array.from(fileList);
        const playlistWasEmpty = playlist.length === 0;
        const videos = files.filter(isVideo);
        const assets = files.filter(file => !isVideo(file) && isEdiAsset(file));
        const ignored = files.length - videos.length - assets.length;

        for (const file of videos) {
            playlist.push({ id: crypto.randomUUID(), name: file.name, file, url: URL.createObjectURL(file) });
        }
        renderPlaylist();

        if (!currentItem() && playlist.length) {
            await selectVideo(playlist[0].id, playlistWasEmpty && playbackOptions.autoplay);
        }

        let uploadError = null;
        try {
            if (assets.length) {
                const mergedAssets = await mergeAssets(assets);
                const currentAssets = assetsForCurrentPlaylist(mergedAssets);
                await uploadAssets(currentAssets);
                await saveAssets(mergedAssets);
            }
        } catch (error) {
            uploadError = error;
        }

        const parts = [];
        if (videos.length) parts.push(`${videos.length} local video${videos.length === 1 ? '' : 's'} added`);
        if (assets.length && !uploadError) parts.push(`${assets.length} EDI asset${assets.length === 1 ? '' : 's'} uploaded`);
        if (ignored) parts.push(`${ignored} file${ignored === 1 ? '' : 's'} ignored`);
        if (uploadError) parts.push(`upload error: ${uploadError.message}`);
        report(parts.length ? parts.join(', ') : 'No compatible videos or assets were found.', Boolean(uploadError || !parts.length));
    }

    async function loadDefinitions() {
        try {
            report('Loading EDI assets...');
            definitions = await (await api('/Edi/Definitions')).json();
            playlistDefinitions = [...definitions];
            renderPlaylist();
            report(playlist.length ? 'EDI assets updated.' : 'Add videos and assets to get started.');
        } catch (error) {
            report(`Could not load EDI: ${error.message}`, true);
        }
    }

    fileInput.addEventListener('change', async () => {
        await addFiles(fileInput.files);
        fileInput.value = '';
    });
    addMediaFiles.addEventListener('click', () => fileInput.click());
    const clearFileDragState = () => dropZone.classList.remove('drag-over');
    window.addEventListener('dragenter', event => {
        if (isFileDrag(event)) dropZone.classList.add('drag-over');
    }, true);
    window.addEventListener('dragover', event => {
        if (!isFileDrag(event)) return;
        event.preventDefault();
        if (event.dataTransfer) event.dataTransfer.dropEffect = 'copy';
        dropZone.classList.add('drag-over');
    }, true);
    window.addEventListener('dragleave', event => {
        if (!event.relatedTarget) clearFileDragState();
    }, true);
    window.addEventListener('drop', event => {
        if (!isFileDrag(event)) return;
        event.preventDefault();
        event.stopImmediatePropagation();
        clearFileDragState();
        if (event.dataTransfer?.files.length) {
            void addFiles(event.dataTransfer.files)
                .catch(error => report(`Could not add the dropped files: ${error.message}`, true));
        }
    }, true);
    window.addEventListener('dragend', clearFileDragState, true);

    video.addEventListener('play', () => {
        renderCustomControls();
        startEdi();
    });
    video.addEventListener('playing', () => {
        renderCustomControls();
        startPlaybackTimer();
        startEdi();
    });
    video.addEventListener('loadedmetadata', () => {
        video.controls = false;
        renderCustomControls();
    });
    video.addEventListener('durationchange', renderCustomControls);
    video.addEventListener('emptied', renderCustomControls);
    video.addEventListener('focus', releaseVideoControlFocus, true);
    video.addEventListener('focusin', releaseVideoControlFocus, true);
    video.addEventListener('pointerup', releaseVideoControlFocus, true);
    video.addEventListener('mouseup', releaseVideoControlFocus, true);
    video.addEventListener('mousedown', handleVideoMouseDown, true);
    video.addEventListener('click', handleVideoClick, true);
    video.addEventListener('dblclick', toggleVideoFullscreen, true);
    document.addEventListener('pointermove', updateControlsVisibility, true);
    function handleStagePointerLeave() {
        video.controls = false;
        pointerOverCustomControls = false;
        hideControlsNow();
        if (document.fullscreenElement === fullscreenTarget) {
            fullscreenTarget.classList.remove('fullscreen-cursor-visible');
            fullscreenTarget.classList.add('fullscreen-cursor-hidden');
            resetFullscreenSummary();
        } else resetFullscreenSummary();
    }
    videoStage.addEventListener('pointerleave', handleStagePointerLeave, true);
    let __fsEl = null;
    function onFullscreenChange() {
        video.controls = false;
        renderCustomControls();

        // Attach pointermove directly to the fullscreen element for browsers
        // that do not dispatch document pointer events while in fullscreen.
        const fs = document.fullscreenElement;
        if (fs === fullscreenTarget) {
            placeFullscreenPlaybackToolbar();
            pointerOverCustomControls = false;
            hideControlsNow();
            updateFullscreenCursor(false);
        }
        else {
            closeFullscreenPanels();
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

    document.addEventListener('fullscreenchange', onFullscreenChange);
    function fsInteractionHandler(e) {
        if (!document.fullscreenElement) return;
        showControlsTemporarily();
    }
    document.addEventListener('touchstart', fsInteractionHandler, { passive: true });
    document.addEventListener('touchmove', fsInteractionHandler, { passive: true });
    video.addEventListener('waiting', () => {
        stopPlaybackTimer();
        if (strokerPaused) {
            strokerNeedsResync = true;
            return;
        }
        if (!video.paused) stopEdi('EDI stopped while the video is buffering.');
    });
    video.addEventListener('stalled', () => {
        stopPlaybackTimer();
        if (strokerPaused) {
            strokerNeedsResync = true;
            return;
        }
        if (!video.paused) stopEdi('EDI stopped because the video stalled.');
    });
    video.addEventListener('seeking', () => {
        stopPlaybackTimer();
        if (strokerPaused) {
            strokerNeedsResync = true;
            return;
        }
        if (!video.paused) stopEdi('EDI stopped while seeking.');
    });
    video.addEventListener('seeked', () => {
        saveCurrentPosition();
        if (!video.paused) {
            if (video.readyState >= HTMLMediaElement.HAVE_FUTURE_DATA) startPlaybackTimer();
            startEdi();
        }
    });
    video.addEventListener('ratechange', () => {
        if (strokerPaused) {
            strokerNeedsResync = true;
            return;
        }
        if (!video.paused) stopEdi('Resynchronizing EDI after the playback-rate change.').then(startEdi);
    });
    video.addEventListener('timeupdate', () => {
        renderCustomControls();
        startEdi();
    });
    video.addEventListener('pause', () => {
        renderCustomControls();
        stopPlaybackTimer();
        saveCurrentPosition();
        if (preserveStrokerPauseOnVideoPause) {
            preserveStrokerPauseOnVideoPause = false;
            strokerNeedsResync = true;
            renderPlaybackOptions();
            return;
        }
        if (suppressPause || video.ended) return;
        stopEdi('Video paused; EDI stopped.');
    });
    video.addEventListener('ended', async () => {
        renderCustomControls();
        stopPlaybackTimer();
        clearSavedPosition(currentId);
        await stopEdi('Video ended; EDI stopped.');
        if (playbackOptions.loopMode === 'video') {
            video.currentTime = 0;
            await video.play();
            return;
        }
        const index = playlist.findIndex(item => item.id === currentId);
        if (playbackOptions.loopMode === 'playlist' && playlist.length) {
            await selectVideo(playlist[(index + 1) % playlist.length].id, true);
            return;
        }
        if (playbackOptions.autoplay && index >= 0 && index + 1 < playlist.length) {
            await selectVideo(playlist[index + 1].id, true);
        }
    });
    video.addEventListener('error', () => {
        stopPlaybackTimer();
        stopEdi('EDI stopped because of a video error.');
    });
    video.addEventListener('abort', () => {
        stopPlaybackTimer();
        stopEdi('EDI stopped because video loading was canceled.');
    });

    document.getElementById('stopPlayer').addEventListener('click', async () => {
        suppressPause = true;
        video.pause();
        video.currentTime = 0;
        suppressPause = false;
        await stopEdi();
    });

    document.getElementById('playFullscreen').addEventListener('click', async () => {
        if (document.fullscreenElement === fullscreenTarget) {
            await document.exitFullscreen?.();
            return;
        }
        if (!currentItem()) {
            report('Please add a video to the playlist first.', true);
            return;
        }
        try {
            await video.play();
            if (fullscreenTarget.requestFullscreen) await fullscreenTarget.requestFullscreen();
        } catch (error) {
            report(`Could not start playback: ${error.message}`, true);
        }
    });
    // document.getElementById('refreshPlayer').addEventListener('click', reloadAssets);
    document.getElementById('clearPlaylist').addEventListener('click', () => {
        clearPlaylist().catch(error => report(`Could not clear the playlist: ${error.message}`, true));
    });
    document.getElementById('togglePlaylist').addEventListener('click', event => {
        const collapsed = !playlistPanelContent.hidden;
        playlistPanelContent.hidden = collapsed;
        dropZone.classList.toggle('collapsed', collapsed);
        workspacePanels.classList.toggle('playlist-collapsed', collapsed);
        event.currentTarget.dataset.tooltip = collapsed ? 'Expand' : 'Collapse';
        event.currentTarget.setAttribute('aria-label', collapsed ? 'Expand' : 'Collapse');
        event.currentTarget.setAttribute('aria-expanded', String(!collapsed));
        playlistToggleIcon?.setAttribute('d', collapsed
            ? 'M5.5 9.5L12 16l6.5-6.5'
            : 'M5.5 14.5L12 8l6.5 6.5');
        renderCollapsedCurrentVideo();
    });
    document.getElementById('toggleDevices').addEventListener('click', event => {
        const collapsed = !devicesPanelContent.hidden;
        devicesPanelContent.hidden = collapsed;
        document.getElementById('devicesPanel')?.classList.toggle('collapsed', collapsed);
        workspacePanels.classList.toggle('devices-collapsed', collapsed);
        event.currentTarget.dataset.tooltip = collapsed ? 'Expand' : 'Collapse';
        event.currentTarget.setAttribute('aria-label', collapsed ? 'Expand' : 'Collapse');
        event.currentTarget.setAttribute('aria-expanded', String(!collapsed));
        devicesToggleIcon?.setAttribute('d', collapsed
            ? 'M5.5 9.5L12 16l6.5-6.5'
            : 'M5.5 14.5L12 8l6.5 6.5');
    });
    Object.entries(optionButtons).forEach(([name, button]) => {
        button.addEventListener('click', () => togglePlaybackOption(name));
    });
    window.addEventListener('keydown', event => {
        const target = event.target;
        const editingText = target instanceof HTMLInputElement
            || target instanceof HTMLTextAreaElement
            || target instanceof HTMLSelectElement
            || target?.isContentEditable;
        if (event.code === 'Delete' && !event.repeat && !event.altKey && !event.ctrlKey && !event.metaKey && !editingText) {
            if (!currentId) return;
            event.preventDefault();
            event.stopPropagation();
            deleteVideo(currentId).catch(error => report(`Could not delete the video: ${error.message}`, true));
            return;
        }
        if (event.code !== 'Space' || event.repeat || event.altKey || event.ctrlKey || event.metaKey) return;
        event.preventDefault();
        event.stopPropagation();
        if (!currentItem()) {
            report('Please add a video to the playlist first.', true);
            return;
        }
        if (playbackOptions.stroker) {
            handleStrokerInput('space');
            return;
        }
        if (video.paused) {
            video.play().catch(error => report(`Could not start playback: ${error.message}`, true));
        } else {
            video.pause();
        }
    }, true);

    // Global key for fullscreen: F
    window.addEventListener('keydown', event => {
        // ignore when editing text
        const target = event.target;
        const editingText = target instanceof HTMLInputElement
            || target instanceof HTMLTextAreaElement
            || target instanceof HTMLSelectElement
            || target?.isContentEditable;
        if (editingText) return;
        if (event.code === 'KeyF' && !event.altKey && !event.ctrlKey && !event.metaKey) {
            event.preventDefault();
            event.stopPropagation();
            if (!currentItem()) {
                report('Please add a video to the playlist first.', true);
                return;
            }
            const fsEl = document.fullscreenElement;
            (fsEl === fullscreenTarget ? document.exitFullscreen?.() : fullscreenTarget.requestFullscreen?.())
                ?.catch(error => report(`Could not toggle fullscreen: ${error.message}`, true));
        }
    }, true);
    window.addEventListener('pagehide', () => {
        stopPlaybackTimer();
        saveCurrentPosition();
        playlist.forEach(item => URL.revokeObjectURL(item.url));
        if (!video.paused) navigator.sendBeacon('/Edi/Stop');
    });
    setInterval(() => {
        if (!video.paused) startEdi();
    }, 30000);
    setInterval(saveCurrentPosition, 5000);
    setInterval(renderTotalPlayback, 1000);
    async function initialize() {
        renderPlaybackOptions();
        await loadDefinitions();
        try {
            await clearStoredVideos();
            renderPlaylist();
        } catch (error) {
            report(`Could not clear previously stored videos: ${error.message}`, true);
        }
    }

    initialize();
})();
