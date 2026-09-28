

export function createPlaybackEvents({ state, media, renderCustomControls, startEdi, startPlaybackTimer, stopPlaybackTimer, stopEdi, saveCurrentPosition, renderPlaybackOptions, clearSavedPosition, selectVideo }) {

    function mount() {
        media.addEventListener('play', () => {
            renderCustomControls();
            startEdi();
        });
        media.addEventListener('playing', () => {
            renderCustomControls();
            startPlaybackTimer();
            startEdi();
        });
        media.addEventListener('loadedmetadata', () => {
            renderCustomControls();
        });
        media.addEventListener('durationchange', renderCustomControls);
        media.addEventListener('emptied', renderCustomControls);
        media.addEventListener('waiting', () => {
            stopPlaybackTimer();
            if (state.strokerPaused) {
                state.strokerNeedsResync = true;
                return;
            }
            if (!media.paused) stopEdi('EDI stopped while the video is buffering.');
        });
        media.addEventListener('stalled', () => {
            stopPlaybackTimer();
            if (state.strokerPaused) {
                state.strokerNeedsResync = true;
                return;
            }
            if (!media.paused) stopEdi('EDI stopped because the video stalled.');
        });
        media.addEventListener('seeking', () => {
            stopPlaybackTimer();
            if (state.strokerPaused) {
                state.strokerNeedsResync = true;
                return;
            }
            if (!media.paused) stopEdi('EDI stopped while seeking.');
        });
        media.addEventListener('seeked', () => {
            saveCurrentPosition();
            if (!media.paused) {
                if (media.readyState >= 3) startPlaybackTimer();
                startEdi();
            }
        });
        media.addEventListener('ratechange', () => {
            if (state.strokerPaused) {
                state.strokerNeedsResync = true;
                return;
            }
            if (!media.paused) stopEdi('Resynchronizing EDI after the playback-rate change.').then(startEdi);
        });
        media.addEventListener('timeupdate', () => {
            renderCustomControls();
            startEdi();
        });
        media.addEventListener('pause', () => {
            renderCustomControls();
            stopPlaybackTimer();
            saveCurrentPosition();
            if (state.preserveStrokerPauseOnVideoPause) {
                state.preserveStrokerPauseOnVideoPause = false;
                state.strokerNeedsResync = true;
                renderPlaybackOptions();
                return;
            }
            if (state.suppressPause || media.ended || !media.paused) return;
            stopEdi('Video paused; EDI stopped.');
        });
        media.addEventListener('ended', async () => {
            renderCustomControls();
            stopPlaybackTimer();
            clearSavedPosition(state.currentId);
            await stopEdi('Video ended; EDI stopped.');
            if (state.playbackOptions.loopMode === 'video') {
                media.currentTime = 0;
                await media.play();
                return;
            }
            const index = state.playlist.findIndex(item => item.id === state.currentId);
            if (state.playbackOptions.loopMode === 'playlist' && state.playlist.length) {
                await selectVideo(state.playlist[(index + 1) % state.playlist.length].id, true);
                return;
            }
            if (state.playbackOptions.autoplay && index >= 0 && index + 1 < state.playlist.length) {
                await selectVideo(state.playlist[index + 1].id, true);
            }
        });
        media.addEventListener('error', () => {
            stopPlaybackTimer();
            stopEdi('EDI stopped because of a video error.');
        });
        media.addEventListener('abort', () => {
            if (state.suppressPause) return;
            stopPlaybackTimer();
            stopEdi('EDI stopped because video loading was canceled.');
        });
    }
    return { mount };
}

