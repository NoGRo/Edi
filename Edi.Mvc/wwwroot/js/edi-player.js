import { createControls } from './player/controls.mjs';
import { createOverlays } from './player/overlays.mjs';
import { createFullscreen } from './player/fullscreen.mjs';
import { createOptions } from './player/options.mjs';
import { createPositions } from './player/positions.mjs';
import { createSync } from './player/synchronization.mjs';
import { createPlaylist } from './player/playlist.mjs';
import { createAssetWorkflow } from './player/asset-workflow.mjs';
import { createInput } from './player/input.mjs';
import { createPlaybackEvents } from './player/playback-events.mjs';
import { createPlayerState } from './player/preferences.mjs';
import { getPlayerElements } from './player/elements.mjs';
import { createHtmlMediaAdapter } from './player/html-media-adapter.mjs';
import { createImmersive } from './player/vr/immersive.mjs';
import { assets } from './player/assets.mjs';
import { report } from './player/edi-api.mjs';

const elements = getPlayerElements();
elements.video.controls = false;
const state = createPlayerState();
const media = createHtmlMediaAdapter(elements.video);
const controllers = {};
const presentation = {
    fullscreenDeviceControlsVisible: false, pointerOverFullscreenPlayback: false,
    pointerOverCustomControls: false, intensityHideTimer: null
};

controllers.controls = createControls({
    state, elements, presentation,
    ediDuration: (...args) => controllers.playlist.ediDuration(...args),
    toggleVideoPlayback: (...args) => controllers.input.toggleVideoPlayback(...args),
});

controllers.overlays = createOverlays({
    state, elements, presentation,
    playerUiActive: (...args) => controllers.fullscreen.playerUiActive(...args),
    hideFullscreenToolbar: (...args) => controllers.fullscreen.hideFullscreenToolbar(...args),
});

controllers.fullscreen = createFullscreen({
    elements, presentation,
    hidePlayerOverlay: (...args) => controllers.overlays.hidePlayerOverlay(...args),
    renderCustomControls: (...args) => controllers.controls.renderCustomControls(...args),
    restoreDeviceControls: (...args) => controllers.overlays.restoreDeviceControls(...args),
    handlePlayerSummaryMovement: (...args) => controllers.overlays.handlePlayerSummaryMovement(...args),
    resetFullscreenSummary: (...args) => controllers.overlays.resetFullscreenSummary(...args),
    placeFullscreenPlaybackToolbar: (...args) => controllers.overlays.placeFullscreenPlaybackToolbar(...args),
    restorePlaybackToolbar: (...args) => controllers.overlays.restorePlaybackToolbar(...args),
});

controllers.options = createOptions({
    state, elements,
    showIntensityOverlay: (...args) => controllers.overlays.showIntensityOverlay(...args),
    saveCurrentPosition: (...args) => controllers.positions.saveCurrentPosition(...args),
    resetPositions: (...args) => controllers.positions.resetPositions(...args),
    toggleStrokerPlayback: (...args) => controllers.sync.toggleStrokerPlayback(...args),
});

controllers.positions = createPositions({
    state, media,
});

controllers.sync = createSync({
    state, media,
    currentItem: (...args) => controllers.playlist.currentItem(...args),
    renderPlaybackOptions: (...args) => controllers.options.renderPlaybackOptions(...args),
    showStrokerStateOverlay: (...args) => controllers.overlays.showStrokerStateOverlay(...args),
});

controllers.playlist = createPlaylist({
    state, media, elements, assetManager: assets,
    saveCurrentPosition: (...args) => controllers.positions.saveCurrentPosition(...args),
    stopEdi: (...args) => controllers.sync.stopEdi(...args),
    restorePosition: (...args) => controllers.positions.restorePosition(...args),
    clearSavedPosition: (...args) => controllers.positions.clearSavedPosition(...args),
    resetPositions: (...args) => controllers.positions.resetPositions(...args),
    renderDiscreteProgress: (...args) => controllers.controls.renderDiscreteProgress(...args),
    uploadAssets: (...args) => controllers.assetWorkflow.uploadAssets(...args),
});

controllers.assetWorkflow = createAssetWorkflow({
    state, assetManager: assets,
    stopEdi: (...args) => controllers.sync.stopEdi(...args),
    renderPlaylist: (...args) => controllers.playlist.renderPlaylist(...args),
    renderPlaybackOptions: (...args) => controllers.options.renderPlaybackOptions(...args),
    resyncAfterAssetsReload: (...args) => controllers.sync.resyncAfterAssetsReload(...args),
});

controllers.input = createInput({
    state, media, elements,
    setIntensity: (...args) => controllers.options.setIntensity(...args),
    handleStrokerInput: (...args) => controllers.sync.handleStrokerInput(...args),
    addFiles: (...args) => controllers.playlist.addFiles(...args),
    setSidePanelOpen: (...args) => controllers.fullscreen.setSidePanelOpen(...args),
    stopEdi: (...args) => controllers.sync.stopEdi(...args),
    currentItem: (...args) => controllers.playlist.currentItem(...args),
    clearPlaylist: (...args) => controllers.playlist.clearPlaylist(...args),
    togglePlaybackOption: (...args) => controllers.options.togglePlaybackOption(...args),
    deleteVideo: (...args) => controllers.playlist.deleteVideo(...args),
});

controllers.playbackEvents = createPlaybackEvents({
    state, media,
    renderCustomControls: (...args) => controllers.controls.renderCustomControls(...args),
    startEdi: (...args) => controllers.sync.startEdi(...args),
    startPlaybackTimer: (...args) => controllers.playlist.startPlaybackTimer(...args),
    stopPlaybackTimer: (...args) => controllers.playlist.stopPlaybackTimer(...args),
    stopEdi: (...args) => controllers.sync.stopEdi(...args),
    saveCurrentPosition: (...args) => controllers.positions.saveCurrentPosition(...args),
    renderPlaybackOptions: (...args) => controllers.options.renderPlaybackOptions(...args),
    clearSavedPosition: (...args) => controllers.positions.clearSavedPosition(...args),
    selectVideo: (...args) => controllers.playlist.selectVideo(...args),
});

controllers.immersive = createImmersive({
    state, elements,
    currentItem: (...args) => controllers.playlist.currentItem(...args),
    setIntensity: (...args) => controllers.options.setIntensity(...args),
});

assets.bindPersistence(report);
// Mount only after constructing all controllers and their callback ports.
Object.values(controllers).forEach(controller => controller.mount());

window.addEventListener('pagehide', () => {
    controllers.playlist.stopPlaybackTimer();
    controllers.positions.saveCurrentPosition();
    state.playlist.forEach(item => URL.revokeObjectURL(item.url));
    if (!media.paused) navigator.sendBeacon('/Edi/Stop');
});
setInterval(() => {
    if (!media.paused) controllers.sync.startEdi();
}, 30000);
setInterval(controllers.positions.saveCurrentPosition, 5000);
setInterval(controllers.playlist.renderTotalPlayback, 1000);
async function initialize() {
    controllers.options.renderPlaybackOptions();
    try {
        await assets.restore();
    } catch (error) {
        report(`Could not restore the asset cache: ${error.message}`, true);
    }
    await controllers.assetWorkflow.loadDefinitions();
    try {
        await assets.clearStoredVideos();
        controllers.playlist.renderPlaylist();
    } catch (error) {
        report(`Could not clear previously stored videos: ${error.message}`, true);
    }
}

initialize();
