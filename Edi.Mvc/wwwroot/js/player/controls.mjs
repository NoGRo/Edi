import { discreteProgressKey, readStoredObject, videoAudioKey } from './preferences.mjs';
import { report } from './edi-api.mjs';

export function createControls({ state, elements, presentation, ediDuration, toggleVideoPlayback }) {
    const { video, videoStage, fullscreenTarget } = elements;
    const customVideoControls = elements.customVideoControls;

    const customPlayPause = elements.customPlayPause;

    const customPlayIcon = elements.customPlayIcon;

    const customPauseIcon = elements.customPauseIcon;

    const customMute = elements.customMute;

    const customVolume = elements.customVolume;

    const customVolumeWaves = elements.customVolumeWaves;

    const customVolumeSlash = elements.customVolumeSlash;

    const customTime = elements.customTime;

    const customElapsed = elements.customElapsed;

    const customDuration = elements.customDuration;

    const customProgressMode = elements.customProgressMode;

    const customSeek = elements.customSeek;

    const customFullscreen = elements.customFullscreen;

    const discreteVideoProgress = elements.discreteVideoProgress;

    const discretePlaylistOverlap = elements.discretePlaylistOverlap;

    const discretePlaylistTail = elements.discretePlaylistTail;

    let discreteProgressMode = localStorage.getItem(discreteProgressKey);

    let showRemainingTime = false;

    function restoreVideoAudio() {
        if (!video) return;
        const savedAudio = readStoredObject(videoAudioKey, {});
        const savedVolume = Number(savedAudio.volume);
        if (Number.isFinite(savedVolume)) video.volume = Math.max(0, Math.min(1, savedVolume));
        if (typeof savedAudio.muted === 'boolean') video.muted = savedAudio.muted;
    }

    let lastAudibleVolume = video?.volume > 0 ? video.volume : 1;

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

    function renderCustomTime(position, duration) {
        const safePosition = Math.min(duration, Math.max(0, Number.isFinite(position) ? position : 0));
        customElapsed.textContent = showRemainingTime
            ? `-${formatMediaTime(Math.max(0, duration - safePosition))}`
            : formatMediaTime(safePosition);
        customDuration.textContent = formatMediaTime(duration);
        customTime.setAttribute('aria-pressed', String(showRemainingTime));
        customTime.setAttribute('aria-label', showRemainingTime ? 'Show elapsed time' : 'Show remaining time');
    }

    function renderDiscreteProgress() {
        const duration = Number.isFinite(video.duration) ? Math.max(0, video.duration) : 0;
        const videoProgress = duration ? Math.min(1, Math.max(0, video.currentTime / duration)) : 0;
        const durations = state.playlist.map(item => ediDuration(item));
        const currentIndex = state.playlist.findIndex(item => item.id === state.currentId);
        const playlistProgress = window.EdiPlayerTools.calculatePlaylistProgress(
            durations, currentIndex, video.currentTime);
        const layers = window.EdiPlayerTools.calculateProgressLayers(videoProgress, playlistProgress);

        discreteVideoProgress.style.width = `${videoProgress * 100}%`;
        discretePlaylistOverlap.style.width = `${layers.overlap * 100}%`;
        discretePlaylistTail.style.left = `${layers.overlap * 100}%`;
        discretePlaylistTail.style.width = `${layers.tail * 100}%`;
        discretePlaylistTail.style.background = layers.tailType === 'video'
            ? 'rgba(110,168,254,.58)'
            : 'rgba(254,196,110,.62)';
        videoStage.classList.toggle('progress-video', discreteProgressMode === 'video');
        videoStage.classList.toggle('progress-playlist', discreteProgressMode === 'playlist');
        customProgressMode.dataset.mode = discreteProgressMode;
        const label = discreteProgressMode === 'none'
            ? 'Persistent progress hidden'
            : discreteProgressMode === 'video'
                ? 'Persistent video progress'
                : 'Persistent video and playlist progress';
        customProgressMode.setAttribute('aria-label', label);
        customProgressMode.title = label;
    }

    function renderCustomControls() {
        if (!video || !customSeek) return;
        video.controls = false;
        const hasVideo = Boolean(video.currentSrc || video.getAttribute('src'));
        videoStage.classList.toggle('video-empty', !hasVideo);
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
        customSeek.max = String(duration);
        if (!customSeekDragging) {
            customSeek.value = String(Math.min(duration, Math.max(0, video.currentTime || 0)));
        }
        renderCustomTime(customSeekDragging ? Number(customSeek.value) : video.currentTime, duration);
        customSeek.style.setProperty('--range-progress', `${duration ? Number(customSeek.value) / duration * 100 : 0}%`);
        const fullscreen = document.fullscreenElement === fullscreenTarget;
        customFullscreen.setAttribute('aria-label', fullscreen ? 'Exit fullscreen' : 'Enter fullscreen');
        renderCustomAudio();
        renderDiscreteProgress();
    }

    function mount() {
        if (!['none', 'video', 'playlist'].includes(discreteProgressMode)) discreteProgressMode = 'none';
        restoreVideoAudio();
        lastAudibleVolume = video?.volume > 0 ? video.volume : 1;
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
        customTime?.addEventListener('click', event => {
            event.currentTarget.blur();
            showRemainingTime = !showRemainingTime;
            renderCustomControls();
        });
        customProgressMode?.addEventListener('click', event => {
            event.currentTarget.blur();
            discreteProgressMode = window.EdiPlayerTools.nextProgressMode(discreteProgressMode);
            localStorage.setItem(discreteProgressKey, discreteProgressMode);
            renderDiscreteProgress();
        });
        customSeek?.addEventListener('pointerdown', () => { customSeekDragging = true; });
        customSeek?.addEventListener('input', () => {
            customSeekDragging = true;
            const duration = Number(customSeek.max);
            renderCustomTime(Number(customSeek.value), duration);
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
            presentation.pointerOverCustomControls = false;
            event.currentTarget.blur();
            const change = document.fullscreenElement === fullscreenTarget
                ? document.exitFullscreen?.()
                : fullscreenTarget.requestFullscreen?.();
            change?.catch(error => report(`Could not toggle fullscreen: ${error.message}`, true));
        });
    }
    return { renderCustomControls, renderCustomAudio, renderDiscreteProgress, mount };
}

