import { isFileDrag } from './media-files.mjs';
import { report } from './edi-api.mjs';
import { horizontalStick } from './vr/motion.mjs';

export function createInput({ state, media, elements, setIntensity, handleStrokerInput, addFiles, setSidePanelOpen, stopEdi, currentItem, clearPlaylist, togglePlaybackOption, deleteVideo }) {
    const { optionButtons } = elements;
    const { video, fullscreenTarget, playerShell, fileInput, addMediaFiles, dropZone, playlistPanelToggle } = elements;
    let suppressNextStrokerVideoClick = false;

    let suppressNextPlaybackVideoClick = false;

    let activeTooltipButton = null;
    const gamepadVariantLatch = new Map();

    function pollGamepads() {
        if (elements.enterVr?.getAttribute('aria-pressed') !== 'true') {
            for (const gamepad of navigator.getGamepads?.() || []) {
                if (!gamepad || gamepad.mapping === 'xr-standard') continue;
                const sideways = horizontalStick(gamepad.axes, gamepad.mapping);
                if (sideways && !gamepadVariantLatch.get(gamepad.index))
                    video.dispatchEvent(new MouseEvent('mousedown', { button: 2, cancelable: true }));
                gamepadVariantLatch.set(gamepad.index, sideways);
            }
        }
        window.requestAnimationFrame(pollGamepads);
    }

    function alignTooltip(button) {
        const bounds = button.getBoundingClientRect();
        const edgeThreshold = Math.min(190, window.innerWidth / 2);
        const alignLeft = bounds.left < edgeThreshold;
        const alignRight = !alignLeft && window.innerWidth - bounds.right < edgeThreshold;
        button.classList.toggle('tooltip-align-left', alignLeft);
        button.classList.toggle('tooltip-align-right', alignRight);
    }

    let lastScrollTime = 0;

    function handleIntensityScroll(event) {
        // check target is video or we are fullscreen
        const inFullscreen = document.fullscreenElement != null;
        const overVideo = video && (video.contains(event.target) || event.target === video
            || (inFullscreen && document.fullscreenElement === fullscreenTarget));
        if (!overVideo) return;
        if (!state.intensityEnabled) return;

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
        const tentative = state.currentIntensity + delta;

        // If we're already at the boundary and the user scrolls further in that
        // direction, allow the event to propagate to the page (do not preventDefault).
        if ((state.currentIntensity >= 100 && tentative > 100 && direction > 0) ||
            (state.currentIntensity <= 0 && tentative < 0 && direction < 0)) {
            // let the browser handle the scroll (propagate)
            return;
        }

        // Otherwise, consume the event and apply clamped intensity
        event.preventDefault();
        event.stopPropagation();

        const newIntensity = Math.max(0, Math.min(100, tentative));
        if (newIntensity === state.currentIntensity) return;
        void setIntensity(newIntensity);
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

        if (!state.playbackOptions.stroker) {
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
        if (state.playbackOptions.stroker) {
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

    const clearFileDragState = () => dropZone.classList.remove('drag-over');

    function mount() {
        window.requestAnimationFrame(pollGamepads);
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
        try {
            video.addEventListener('wheel', handleIntensityScroll, { passive: false });
            document.addEventListener('wheel', handleIntensityScroll, { passive: false });
        } catch (e) { }
        fileInput.addEventListener('change', async () => {
            await addFiles(fileInput.files);
            fileInput.value = '';
        });
        addMediaFiles.addEventListener('click', () => fileInput.click());
        window.addEventListener('dragenter', event => {
            if (isFileDrag(event)) {
                setSidePanelOpen(dropZone, playlistPanelToggle, true);
                dropZone.classList.add('drag-over');
            }
        }, true);
        window.addEventListener('dragover', event => {
            if (!isFileDrag(event)) return;
            event.preventDefault();
            if (event.dataTransfer) event.dataTransfer.dropEffect = 'copy';
            setSidePanelOpen(dropZone, playlistPanelToggle, true);
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
        video.addEventListener('focus', releaseVideoControlFocus, true);
        video.addEventListener('focusin', releaseVideoControlFocus, true);
        video.addEventListener('pointerup', releaseVideoControlFocus, true);
        video.addEventListener('mouseup', releaseVideoControlFocus, true);
        video.addEventListener('mousedown', handleVideoMouseDown, true);
        video.addEventListener('click', handleVideoClick, true);
        video.addEventListener('dblclick', toggleVideoFullscreen, true);
        document.getElementById('stopPlayer').addEventListener('click', async () => {
            state.suppressPause = true;
            video.pause();
            video.currentTime = 0;
            state.suppressPause = false;
            await stopEdi();
        });
        document.getElementById('playFullscreen').addEventListener('click', async () => {
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
        document.getElementById('clearPlaylist').addEventListener('click', () => {
            clearPlaylist().catch(error => report(`Could not clear the playlist: ${error.message}`, true));
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
                if (!state.currentId) return;
                event.preventDefault();
                event.stopPropagation();
                deleteVideo(state.currentId).catch(error => report(`Could not delete the video: ${error.message}`, true));
                return;
            }
            if (event.code !== 'Space' || event.repeat || event.altKey || event.ctrlKey || event.metaKey) return;
            event.preventDefault();
            event.stopPropagation();
            if (!currentItem()) {
                report('Please add a video to the playlist first.', true);
                return;
            }
            if (state.playbackOptions.stroker) {
                handleStrokerInput('space');
                return;
            }
            if (video.paused) {
                video.play().catch(error => report(`Could not start playback: ${error.message}`, true));
            } else {
                video.pause();
            }
        }, true);
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
    }
    return { toggleVideoPlayback, mount };
}

