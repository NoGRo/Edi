import { readStoredObject, playbackPositionsKey } from './preferences.mjs';

export function createPositions({ state, media }) {
    let savedPositions = readStoredObject(playbackPositionsKey, {});

    function resetPositions() { savedPositions = {}; localStorage.removeItem(playbackPositionsKey); }

    function persistPositions() {
        localStorage.setItem(playbackPositionsKey, JSON.stringify(savedPositions));
    }

    function clearSavedPosition(id) {
        if (!id || !(id in savedPositions)) return;
        delete savedPositions[id];
        persistPositions();
    }

    function saveCurrentPosition() {
        if (!state.playbackOptions.resume || !state.currentId || !Number.isFinite(media.currentTime)) return;
        if (media.ended || media.currentTime <= 0.25) {
            clearSavedPosition(state.currentId);
            return;
        }
        savedPositions[state.currentId] = media.currentTime;
        persistPositions();
    }

    function restorePosition(id) {
        if (!state.playbackOptions.resume) return;
        const savedPosition = Number(savedPositions[id]);
        if (!(savedPosition > 0)) return;

        const applyPosition = () => {
            if (state.currentId !== id) return;
            if (Number.isFinite(media.duration) && savedPosition >= media.duration - 1) {
                clearSavedPosition(id);
                return;
            }
            media.currentTime = savedPosition;
        };

        if (media.readyState >= 1) applyPosition();
        else media.addEventListener('loadedmetadata', applyPosition, { once: true });
    }

    function mount() {

    }
    return { clearSavedPosition, saveCurrentPosition, restorePosition, resetPositions, mount };
}

