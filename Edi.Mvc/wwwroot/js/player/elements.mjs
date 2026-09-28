export function getPlayerElements() {
    const elements = {
        video: document.getElementById('videoPlayer'),
        videoStage: document.getElementById('videoStage'),
        customVideoControls: document.getElementById('customVideoControls'),
        customPlayPause: document.getElementById('customPlayPause'),
        customPlayIcon: document.getElementById('customPlayIcon'),
        customPauseIcon: document.getElementById('customPauseIcon'),
        customMute: document.getElementById('customMute'),
        customVolume: document.getElementById('customVolume'),
        customVolumeWaves: document.getElementById('customVolumeWaves'),
        customVolumeSlash: document.getElementById('customVolumeSlash'),
        customTime: document.getElementById('customTime'),
        customElapsed: document.getElementById('customElapsed'),
        customDuration: document.getElementById('customDuration'),
        customProgressMode: document.getElementById('customProgressMode'),
        customSeek: document.getElementById('customSeek'),
        customFullscreen: document.getElementById('customFullscreen'),
        discreteVideoProgress: document.getElementById('discreteVideoProgress'),
        discretePlaylistOverlap: document.getElementById('discretePlaylistOverlap'),
        discretePlaylistTail: document.getElementById('discretePlaylistTail'),
        fullscreenPlaybackOverlay: document.getElementById('fullscreenPlaybackOverlay'),
        playbackToolbar: document.getElementById('playbackToolbar'),
        enterVr: document.getElementById('enterVr'),
        vrFollowToggle: document.getElementById('vrFollowToggle'),
        vrSettingsToggle: document.getElementById('vrSettingsToggle'),
        playlistPanelToggle: document.getElementById('playlistPanelToggle'),
        devicesPanelToggle: document.getElementById('devicesPanelToggle'),
        devicesPanel: document.getElementById('devicesPanel'),
        fileInput: document.getElementById('mediaFiles'),
        addMediaFiles: document.getElementById('addMediaFiles'),
        dropZone: document.getElementById('fileDrop'),
        playlistElement: document.getElementById('videoPlaylist'),
        playlistCount: document.getElementById('playlistCount'),
        totalPlayback: document.getElementById('totalPlayback'),
        playlistDurationTotal: document.getElementById('playlistDurationTotal'),
        loopModeIndicator: document.getElementById('loopModeIndicator'),
        playerShell: document.querySelector('.player-shell'),
        deviceControls: document.querySelector('.variant-toggle-wrap'),
        intensityOverlay: document.createElement('div'),
    };
    elements.fullscreenTarget = elements.videoStage || elements.video;
    elements.playbackToolbarHome = document.createComment('playback toolbar home');
    elements.optionButtons = {
        autoplay: document.getElementById('autoplayToggle'),
        loop: document.getElementById('loopToggle'),
        resume: document.getElementById('resumeToggle'),
        stroker: document.getElementById('strokerToggle'),
        intensity: document.getElementById('intensityToggle')
    };
    return elements;
}
