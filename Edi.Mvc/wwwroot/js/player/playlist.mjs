import { currentVideoKey } from './preferences.mjs';
import { fileStem, isVideo, isEdiAsset } from './media-files.mjs';
import { report } from './edi-api.mjs';
import { deviceRouting } from './device-routing.mjs';

export function createPlaylist({ state, media, elements, assetManager, saveCurrentPosition, stopEdi, restorePosition, clearSavedPosition, resetPositions, renderDiscreteProgress, uploadAssets }) {
    const { totalPlayback, playlistElement, playlistCount, playlistDurationTotal } = elements;
    let draggedId = null;

    let playbackMilliseconds = 0;

    let playbackStartedAt = null;

    function currentItem() {
        return state.playlist.find(item => item.id === state.currentId) || null;
    }

    function ediDuration(item) {
        const stem = fileStem(item.name);
        const endTimes = state.definitions
            .filter(definition => fileStem(definition.fileName || '') === stem)
            .map(definition => Number(definition.endTime))
            .filter(Number.isFinite);
        return endTimes.length ? Math.max(...endTimes) : item.duration ?? null;
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
        if (playbackStartedAt !== null || media.paused || media.ended) return;
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

    async function selectVideo(id, autoplay = false) {
        const item = state.playlist.find(entry => entry.id === id);
        if (!item) return;
        if (state.currentId === id) {
            if (autoplay && media.paused) await media.play();
            return;
        }
        stopPlaybackTimer();
        saveCurrentPosition();
        state.suppressPause = true;
        try {
            media.pause();
            if (!autoplay) await stopEdi('Previous video stopped.');
            state.currentId = id;
            localStorage.setItem(currentVideoKey, id);
            if (state.strokerPaused) state.strokerNeedsResync = true;
            state.lastGallery = null;
            media.setSource(item);
            restorePosition(id);
            renderPlaylist();
            await deviceRouting.setVideoContext({ name: item.name, definitions: state.definitions })
                .catch(error => report(`Could not prepare device variants: ${error.message}`, true));
            if (state.currentId !== id) return;
            report(`Ready: ${item.name}`);
            if (autoplay) await media.play();
        } catch (error) {
            await stopEdi('Could not start the next video; EDI stopped.');
            throw error;
        } finally {
            state.suppressPause = false;
        }
    }

    async function deleteVideo(id) {
        const index = state.playlist.findIndex(item => item.id === id);
        if (index < 0) return;

        const [removed] = state.playlist.splice(index, 1);
        clearSavedPosition(id);
        URL.revokeObjectURL(removed.url);
        if (state.currentId === id) {
            await stopEdi('Video deleted; EDI stopped.');
            state.currentId = null;
            await deviceRouting.setVideoContext(null);
            localStorage.removeItem(currentVideoKey);
            media.clearSource();
            if (state.playlist.length) {
                await selectVideo(state.playlist[Math.min(index, state.playlist.length - 1)].id);
            }
        }

        renderPlaylist();
        report(`Deleted: ${removed.name}`);
    }

    async function clearPlaylist() {
        if (!window.confirm('Delete the playlist and all EDI assets?')) return;
        state.suppressPause = true;
        media.pause();
        state.suppressPause = false;
        await assetManager.clear();
        state.definitions = [];
        state.ediStopped = true;
        state.lastGallery = null;
        state.playlist.forEach(item => URL.revokeObjectURL(item.url));
        state.playlist.length = 0;
        state.currentId = null;
        await deviceRouting.setVideoContext(null);
        localStorage.removeItem(currentVideoKey);
        resetPositions();
        media.clearSource();
        renderPlaylist();
        await assetManager.clearStoredVideos();
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

    function renderPlaylist() {
        playlistElement.classList.toggle('playlist-scroll', state.playlist.length > 10);
        playlistCount.textContent = `${state.playlist.length} ${state.playlist.length === 1 ? 'video' : 'videos'}`;
        const durations = state.playlist.map(ediDuration).filter(Number.isFinite);
        playlistDurationTotal.textContent = formatCompactDuration(durations.reduce((total, duration) => total + duration, 0));
        renderDiscreteProgress();

        playlistElement.replaceChildren();
        if (!state.playlist.length) {
            const empty = document.createElement('li');
            empty.className = 'p-3 text-muted';
            empty.textContent = 'You have not added videos yet.';
            playlistElement.append(empty);
            return;
        }

        state.playlist.forEach(item => {
                const row = document.createElement('li');
                row.className = `playlist-item${item.id === state.currentId ? ' active' : ''}`;
                row.draggable = true;

                const handle = document.createElement('span');
                handle.textContent = '↕';
                handle.className = 'text-muted';
                handle.title = 'Drag to reorder';
                row.append(handle);

                const name = document.createElement('span');
                name.className = 'playlist-name';
                name.textContent = item.name;
                const duration = document.createElement('span');
                duration.className = 'playlist-duration';
                duration.textContent = formatDuration(ediDuration(item));
                row.append(name, duration);

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
                row.append(actions);

                row.addEventListener('click', () => selectVideo(item.id, state.playbackOptions.autoplay)
                    .catch(error => report(`Could not start playback: ${error.message}`, true)));
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
                    const from = state.playlist.findIndex(entry => entry.id === draggedId);
                    const to = state.playlist.findIndex(entry => entry.id === item.id);
                    if (from < 0 || to < 0 || from === to) return;
                    const [moved] = state.playlist.splice(from, 1);
                    state.playlist.splice(to, 0, moved);
                    renderPlaylist();
                });
                playlistElement.append(row);
        });
    }

    async function addFiles(fileList) {
        const files = Array.from(fileList);
        const playlistWasEmpty = state.playlist.length === 0;
        const videos = files.filter(isVideo);
        const assets = files.filter(file => !isVideo(file) && isEdiAsset(file));
        const ignored = files.length - videos.length - assets.length;

        for (const file of videos) {
            state.playlist.push({ id: crypto.randomUUID(), name: file.name, file, url: URL.createObjectURL(file) });
        }
        renderPlaylist();

        let uploadError = null;
        try {
            if (assets.length) {
                const mergedAssets = await assetManager.merge(assets);
                const currentAssets = assetManager.forPlaylist(mergedAssets, state.playlist);
                await uploadAssets(currentAssets);
                await assetManager.save(mergedAssets);
            }
        } catch (error) {
            uploadError = error;
        }

        if (!currentItem() && state.playlist.length) {
            await selectVideo(state.playlist[0].id, playlistWasEmpty && state.playbackOptions.autoplay);
        }

        const parts = [];
        if (videos.length) parts.push(`${videos.length} local video${videos.length === 1 ? '' : 's'} added`);
        if (assets.length && !uploadError) parts.push(`${assets.length} EDI asset${assets.length === 1 ? '' : 's'} uploaded`);
        if (ignored) parts.push(`${ignored} file${ignored === 1 ? '' : 's'} ignored`);
        if (uploadError) parts.push(`upload error: ${uploadError.message}`);
        report(parts.length ? parts.join(', ') : 'No compatible videos or assets were found.', Boolean(uploadError || !parts.length));
    }

    function mount() {
        const rememberDuration = () => {
            const item = currentItem();
            if (!item || !Number.isFinite(media.duration)) return;
            item.duration = Math.round(media.duration * 1000);
            renderPlaylist();
        };
        media.addEventListener('loadedmetadata', rememberDuration);
        media.addEventListener('durationchange', rememberDuration);
    }
    return { currentItem, ediDuration, renderTotalPlayback, startPlaybackTimer, stopPlaybackTimer, selectVideo, deleteVideo, clearPlaylist, renderPlaylist, addFiles, mount };
}

