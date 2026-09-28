import { deviceRouting, routedPlaybackCommand } from './device-routing.mjs';
import { fileStem } from './media-files.mjs';
import { report } from './edi-api.mjs';

export function createSync({ state, media, currentItem, renderPlaybackOptions, showStrokerStateOverlay,
    command = routedPlaybackCommand, allPause = () => deviceRouting.allPause() }) {
    let strokerCommandsPending = 0;

    let commandQueue = Promise.resolve();

    function enqueueCommand(command) {
        commandQueue = commandQueue
            .then(command)
            .catch(error => report(`Error EDI: ${error.message}`, true));
        return commandQueue;
    }

    function currentDefinition() {
        const item = currentItem();
        if (!item) return null;
        const stem = fileStem(item.name);
        const position = Math.round(media.currentTime * 1000);
        return state.definitions.find(definition =>
            fileStem(definition.fileName || '') === stem
            && position >= definition.startTime
            && position <= definition.endTime);
    }

    async function syncEdi(force = false, resumePaused = false) {
        if (media.paused || !currentItem()) return false;
        const definition = currentDefinition();
        if (!definition) {
            state.lastGallery = null;
            report('Video is playing but no EDI definition for this time.');
            return false;
        }
        if (!force && state.lastGallery === definition.name) return true;

        const seek = Math.max(0, Math.round(media.currentTime * 1000) - definition.startTime);
        await command(`/Edi/Play/${encodeURIComponent(definition.name)}?seek=${seek}`, { resumePaused });
        state.lastGallery = definition.name;
        report(`EDI synced: ${definition.name}`);
        return true;
    }

    function startEdi() {
        if (media.paused || media.ended || media.seeking) return commandQueue;
        if (state.strokerPaused && allPause()) {
            if (state.lastGallery !== currentDefinition()?.name) state.strokerNeedsResync = true;
            return commandQueue;
        }
        if (!state.ediStopped && state.lastGallery === currentDefinition()?.name) return commandQueue;
        const force = state.ediStopped;
        state.ediStopped = false;
        return enqueueCommand(async () => {
            try {
                if (media.paused || media.ended || media.seeking) {
                    state.ediStopped = true;
                    return;
                }
                const synced = await syncEdi(force, state.strokerNeedsResync);
                if (synced) state.strokerNeedsResync = false;
                state.ediStopped = !synced;
                if (synced && state.intensityNeedsResync) {
                    await command(`/Edi/Intensity/${state.currentIntensity}`);
                    state.intensityNeedsResync = false;
                }
            } catch (error) {
                state.ediStopped = true;
                throw error;
            }
        });
    }

    function resyncAfterAssetsReload() {
        state.ediStopped = true;
        state.lastGallery = null;
        state.intensityNeedsResync = true;
        if (state.strokerPaused && allPause()) {
            state.strokerNeedsResync = true;
            renderPlaybackOptions();
            return commandQueue;
        }
        if (media.paused || media.ended || media.seeking) return commandQueue;
        return startEdi();
    }

    function resumeVideoAndStroker() {
        state.strokerPaused = false;
        state.strokerPauseMethod = null;
        state.ediStopped = true;
        state.lastGallery = null;
        renderPlaybackOptions();
        showStrokerStateOverlay(false);
        media.play().catch(error => report(`Could not start playback: ${error.message}`, true));
    }

    function handleStrokerInput(method, queueWhilePending = false) {
        if (media.paused) {
            if (state.strokerPaused) return enqueueCommand(async () => {
                if (!state.strokerNeedsResync) await command('/Edi/Resume?AtCurrentTime=true');
                resumeVideoAndStroker();
            });
            resumeVideoAndStroker();
            return commandQueue;
        }
        if (state.strokerPaused && state.strokerPauseMethod && state.strokerPauseMethod !== method) {
            state.strokerPauseMethod = method;
            state.preserveStrokerPauseOnVideoPause = true;
            media.pause();
            report('Video paused; stroker remains paused.');
            return commandQueue;
        }
        return toggleStrokerPlayback(queueWhilePending, method);
    }

    function toggleStrokerPlayback(queueWhilePending = false, method = null) {
        if (strokerCommandsPending > 0 && !queueWhilePending) return commandQueue;

        const pauseStroker = !state.strokerPaused;
        const previousPauseMethod = state.strokerPauseMethod;
        state.strokerPaused = pauseStroker;
        state.strokerPauseMethod = pauseStroker ? method : null;
        strokerCommandsPending++;
        if (pauseStroker) state.strokerNeedsResync = false;
        renderPlaybackOptions();
        showStrokerStateOverlay(pauseStroker);

        return enqueueCommand(async () => {
            try {
                if (pauseStroker) {
                    await command('/Edi/Pause?untilResume=false');
                    return;
                }

                if (!allPause()) {
                    await command('/Edi/Resume?AtCurrentTime=true');
                } else if (state.strokerNeedsResync || state.lastGallery !== currentDefinition()?.name) {
                    state.ediStopped = !await syncEdi(true, true);
                } else {
                    await command('/Edi/Resume?AtCurrentTime=true');
                    state.ediStopped = false;
                    report('Stroker resumed at the current time.');
                }
                if (!state.ediStopped && state.intensityNeedsResync) {
                    await command(`/Edi/Intensity/${state.currentIntensity}`);
                    state.intensityNeedsResync = false;
                }
                state.strokerNeedsResync = false;
            } catch (error) {
                state.strokerPaused = !pauseStroker;
                state.strokerPauseMethod = previousPauseMethod;
                showStrokerStateOverlay(state.strokerPaused);
                throw error;
            } finally {
                strokerCommandsPending--;
                renderPlaybackOptions();
            }
        });
    }

    function stopEdi(reason = 'Playback stopped.') {
        if (state.strokerPaused) state.strokerNeedsResync = true;
        renderPlaybackOptions();
        if (state.ediStopped) {
            report(reason);
            return commandQueue;
        }
        state.ediStopped = true;
        state.lastGallery = null;
        return enqueueCommand(async () => {
            try {
                await command('/Edi/Stop');
                report(reason);
            } finally {
                state.ediStopped = true;
                state.lastGallery = null;
            }
        });
    }

    function mount() {

    }
    return { startEdi, stopEdi, handleStrokerInput, toggleStrokerPlayback, resyncAfterAssetsReload, mount };
}

