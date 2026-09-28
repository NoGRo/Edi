export function readStoredObject(key, fallback) {
    try {
        return { ...fallback, ...JSON.parse(localStorage.getItem(key) || '{}') };
    } catch {
        return { ...fallback };
    }
}

export function createPlayerState() {
    const state = {};
    state.playlist = [];

    state.playbackOptions = readStoredObject(playbackOptionsKey, { autoplay: false, loop: false, resume: false, stroker: false });
    if (!['none', 'video', 'playlist'].includes(state.playbackOptions.loopMode)) {
        state.playbackOptions.loopMode = state.playbackOptions.loop ? 'video' : 'none';
    }

    state.currentIntensity = Number(localStorage.getItem('edi-player-intensity'));
    if (!Number.isFinite(state.currentIntensity)) state.currentIntensity = 50;
    // intensity enabled flag
    state.intensityEnabled = readStoredObject(playbackOptionsKey, {}).intensity ?? true;
    state.intensityNeedsResync = false;
    state.definitions = [];
    state.currentId = null;
    state.lastGallery = null;
    state.suppressPause = false;
    state.ediStopped = true;
    state.strokerPaused = false;
    state.strokerPauseMethod = null;
    state.preserveStrokerPauseOnVideoPause = false;

    state.strokerNeedsResync = false;
    return state;
}

export const currentVideoKey = 'edi-player-current-video';
export const playbackOptionsKey = 'edi-player-options';
export const playbackPositionsKey = 'edi-player-positions';
export const videoAudioKey = 'edi-player-video-audio';
export const discreteProgressKey = 'edi-player-discrete-progress';
