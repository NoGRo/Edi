(function (root, factory) {
    const tools = factory();
    if (typeof module === 'object' && module.exports) module.exports = tools;
    else root.EdiPlayerTools = tools;
}(typeof globalThis !== 'undefined' ? globalThis : this, function () {
    const progressModes = ['none', 'video', 'playlist'];

    function nextProgressMode(mode) {
        const index = progressModes.indexOf(mode);
        return progressModes[(index + 1) % progressModes.length];
    }

    function calculatePlaylistProgress(durationsMilliseconds, currentIndex, currentSeconds) {
        const durations = durationsMilliseconds.map(value =>
            Number.isFinite(value) && value > 0 ? value : 0);
        const total = durations.reduce((sum, value) => sum + value, 0);
        if (!total || currentIndex < 0 || currentIndex >= durations.length) return 0;

        const before = durations.slice(0, currentIndex).reduce((sum, value) => sum + value, 0);
        const current = Math.min(durations[currentIndex], Math.max(0,
            Number.isFinite(currentSeconds) ? currentSeconds * 1000 : 0));
        return Math.min(1, Math.max(0, (before + current) / total));
    }

    function calculateProgressLayers(videoProgress, playlistProgress) {
        const video = Math.min(1, Math.max(0, Number.isFinite(videoProgress) ? videoProgress : 0));
        const playlist = Math.min(1, Math.max(0, Number.isFinite(playlistProgress) ? playlistProgress : 0));
        return {
            overlap: Math.min(video, playlist),
            tail: Math.abs(video - playlist),
            tailType: video > playlist ? 'video' : 'playlist'
        };
    }

    return { calculatePlaylistProgress, calculateProgressLayers, nextProgressMode };
}));
