(() => {
    const videoExtensions = ['.mp4', '.webm', '.avi', '.mkv', '.mov'];
    const playerShell = document.querySelector('.player-shell');
    const video = document.getElementById('videoPlayer');
    const fileInput = document.getElementById('mediaFiles');
    const addMediaFiles = document.getElementById('addMediaFiles');
    const dropZone = document.getElementById('fileDrop');
    const playlistElement = document.getElementById('videoPlaylist');
    const playlistCount = document.getElementById('playlistCount');
    const playlistDurationTotal = document.getElementById('playlistDurationTotal');
    const optionButtons = {
        autoplay: document.getElementById('autoplayToggle'),
        loop: document.getElementById('loopToggle'),
        resume: document.getElementById('resumeToggle'),
        stroker: document.getElementById('strokerToggle')
    };
    const status = document.getElementById('playerStatus');
    const playlist = [];
    const databaseName = 'edi-player';
    const playlistStore = 'playlist';
    const assetStore = 'assets';
    const currentVideoKey = 'edi-player-current-video';
    const playbackOptionsKey = 'edi-player-options';
    const playbackPositionsKey = 'edi-player-positions';
    let playbackOptions = readStoredObject(playbackOptionsKey, { autoplay: false, loop: false, resume: false, stroker: false });
    let savedPositions = readStoredObject(playbackPositionsKey, {});
    let definitions = [];
    let currentId = null;
    let draggedId = null;
    let lastGallery = null;
    let suppressPause = false;
    let ediStopped = true;
    let strokerPaused = false;
    let strokerNeedsResync = false;
    let strokerCommandPending = false;
    let videoClickTimer = null;
    let commandQueue = Promise.resolve();

    function readStoredObject(key, fallback) {
        try {
            return { ...fallback, ...JSON.parse(localStorage.getItem(key) || '{}') };
        } catch {
            return { ...fallback };
        }
    }

    function renderPlaybackOptions() {
        Object.entries(optionButtons).forEach(([name, button]) => {
            const enabled = playbackOptions[name] === true;
            const paused = name === 'stroker' && strokerPaused;
            button.setAttribute('aria-pressed', String(enabled));
            button.classList.toggle('btn-primary', enabled && !paused);
            button.classList.toggle('btn-danger', paused);
            button.classList.toggle('btn-outline-secondary', !enabled);
            button.classList.toggle('stroker-paused', paused);
            if (name === 'stroker') {
                const label = paused
                    ? 'Stroker pausado; espacio reanuda'
                    : enabled
                        ? 'Espacio o clic en el video pausa o reanuda el stroker'
                        : 'Espacio pausa o reproduce el video';
                button.title = label;
                button.setAttribute('aria-label', label);
            }
        });
    }

    function persistPositions() {
        localStorage.setItem(playbackPositionsKey, JSON.stringify(savedPositions));
    }

    function clearSavedPosition(id) {
        if (!id || !(id in savedPositions)) return;
        delete savedPositions[id];
        persistPositions();
    }

    function saveCurrentPosition() {
        if (!playbackOptions.resume || !currentId || !Number.isFinite(video.currentTime)) return;
        if (video.ended || video.currentTime <= 0.25) {
            clearSavedPosition(currentId);
            return;
        }
        savedPositions[currentId] = video.currentTime;
        persistPositions();
    }

    function restorePosition(id) {
        if (!playbackOptions.resume) return;
        const savedPosition = Number(savedPositions[id]);
        if (!(savedPosition > 0)) return;

        const applyPosition = () => {
            if (currentId !== id) return;
            if (Number.isFinite(video.duration) && savedPosition >= video.duration - 1) {
                clearSavedPosition(id);
                return;
            }
            video.currentTime = savedPosition;
        };

        if (video.readyState >= 1) applyPosition();
        else video.addEventListener('loadedmetadata', applyPosition, { once: true });
    }

    function togglePlaybackOption(name) {
        playbackOptions[name] = playbackOptions[name] !== true;
        localStorage.setItem(playbackOptionsKey, JSON.stringify(playbackOptions));
        if (name === 'resume') {
            if (playbackOptions.resume) saveCurrentPosition();
            else {
                savedPositions = {};
                localStorage.removeItem(playbackPositionsKey);
            }
        }
        if (name === 'stroker' && !playbackOptions.stroker && strokerPaused) {
            toggleStrokerPlayback();
        }
        renderPlaybackOptions();
    }

    const extension = name => {
        const dot = name.lastIndexOf('.');
        return dot >= 0 ? name.slice(dot).toLowerCase() : '';
    };

    const fileStem = name => {
        const dot = name.lastIndexOf('.');
        return (dot >= 0 ? name.slice(0, dot) : name).toLowerCase();
    };

    const isVideo = file => file.type.startsWith('video/') || videoExtensions.includes(extension(file.name));
    const isFileDrag = event => Array.from(event.dataTransfer?.types || []).includes('Files');
    const isEdiAsset = file => {
        const name = file.name.toLowerCase();
        return name.endsWith('.funscript')
            || name.endsWith('.mp3')
            || name === 'definitions.csv'
            || name === 'definitions_auto.csv'
            || name.startsWith('bundledefinition') && name.endsWith('.txt');
    };

    async function api(path, options = {}) {
        const response = await fetch(path, options);
        if (!response.ok) {
            const detail = await response.text();
            throw new Error(detail || `${response.status} ${response.statusText}`);
        }
        return response;
    }

    async function confirmedPlaybackCommand(path) {
        const controller = new AbortController();
        const timeout = window.setTimeout(() => controller.abort(), 5000);
        try {
            return await api(path, { method: 'POST', signal: controller.signal });
        } catch (error) {
            if (controller.signal.aborted) {
                throw new Error('El servidor EDI no confirmó el comando en 5 segundos.');
            }
            throw error;
        } finally {
            window.clearTimeout(timeout);
        }
    }

    function report(message, isError = false) {
        status.textContent = message;
        status.className = `player-status ${isError ? 'text-danger' : 'text-muted'}`;
    }

    function enqueueCommand(command) {
        commandQueue = commandQueue
            .then(command)
            .catch(error => report(`Error EDI: ${error.message}`, true));
        return commandQueue;
    }

    function releaseVideoControlFocus() {
        window.setTimeout(() => {
            if (document.activeElement !== video) return;
            video.blur();
            playerShell.focus({ preventScroll: true });
        }, 0);
    }

    function clickedNativeVideoControls(event) {
        if (!video.controls || event.detail === 0) return event.detail === 0;
        const bounds = video.getBoundingClientRect();
        const controlsHeight = Math.min(56, Math.max(36, bounds.height * 0.12));
        return event.clientY >= bounds.bottom - controlsHeight;
    }

    function handleVideoClick(event) {
        releaseVideoControlFocus();
        if (!playbackOptions.stroker || video.paused || clickedNativeVideoControls(event)) return;

        event.preventDefault();
        event.stopImmediatePropagation();
        window.clearTimeout(videoClickTimer);
        videoClickTimer = null;
        if (event.detail > 1) return;

        videoClickTimer = window.setTimeout(() => {
            videoClickTimer = null;
            toggleStrokerPlayback();
        }, 250);
    }

    function toggleVideoFullscreen(event) {
        if (clickedNativeVideoControls(event)) return;

        event.preventDefault();
        event.stopImmediatePropagation();
        window.clearTimeout(videoClickTimer);
        videoClickTimer = null;

        const fullscreenChange = document.fullscreenElement === video
            ? document.exitFullscreen?.()
            : video.requestFullscreen?.();
        fullscreenChange?.catch(error => report(`No se pudo cambiar la pantalla completa: ${error.message}`, true));
    }

    function updateFullscreenControls(event) {
        if (document.fullscreenElement !== video) {
            video.controls = true;
            return;
        }

        if (!event) {
            video.controls = false;
            return;
        }

        const bounds = video.getBoundingClientRect();
        const revealHeight = Math.min(96, Math.max(56, bounds.height * 0.12));
        video.controls = event.clientY >= bounds.bottom - revealHeight;
    }

    function currentItem() {
        return playlist.find(item => item.id === currentId) || null;
    }

    function ediDuration(item) {
        const stem = fileStem(item.name);
        const endTimes = definitions
            .filter(definition => fileStem(definition.fileName || '') === stem)
            .map(definition => Number(definition.endTime))
            .filter(Number.isFinite);
        return endTimes.length ? Math.max(...endTimes) : null;
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

    function openDatabase() {
        return new Promise((resolve, reject) => {
            const request = indexedDB.open(databaseName, 2);
            request.onupgradeneeded = () => {
                if (!request.result.objectStoreNames.contains(playlistStore)) {
                    request.result.createObjectStore(playlistStore, { keyPath: 'id' });
                }
                if (!request.result.objectStoreNames.contains(assetStore)) {
                    request.result.createObjectStore(assetStore, { keyPath: 'name' });
                }
            };
            request.onsuccess = () => resolve(request.result);
            request.onerror = () => reject(request.error);
        });
    }

    async function withStore(storeName, mode, operation) {
        const database = await openDatabase();
        return new Promise((resolve, reject) => {
            const transaction = database.transaction(storeName, mode);
            const store = transaction.objectStore(storeName);
            operation(store);
            transaction.oncomplete = () => {
                database.close();
                resolve();
            };
            transaction.onerror = () => {
                database.close();
                reject(transaction.error);
            };
        });
    }

    function clearStoredVideos() {
        return withStore(playlistStore, 'readwrite', store => {
            store.clear();
        });
    }

    async function getStoredItems(storeName) {
        const database = await openDatabase();
        return new Promise((resolve, reject) => {
            const transaction = database.transaction(storeName, 'readonly');
            const request = transaction.objectStore(storeName).getAll();
            request.onsuccess = () => resolve(request.result);
            request.onerror = () => reject(request.error);
            transaction.oncomplete = () => database.close();
        });
    }

    function saveAssets(files) {
        return withStore(assetStore, 'readwrite', store => {
            store.clear();
            files
                .filter(file => file.name.toLowerCase() !== 'definitions_auto.csv')
                .forEach(file => store.put({ name: file.name, file }));
        });
    }

    async function mergeAssets(files) {
        const records = await getStoredItems(assetStore);
        const merged = new Map(records
            .map(record => record.file)
            .filter(file => file.name.toLowerCase() !== 'definitions_auto.csv')
            .map(file => [file.name.toLowerCase(), file]));
        files
            .filter(file => file.name.toLowerCase() !== 'definitions_auto.csv')
            .forEach(file => merged.set(file.name.toLowerCase(), file));
        return [...merged.values()];
    }

    function currentDefinition() {
        const item = currentItem();
        if (!item) return null;
        const stem = fileStem(item.name);
        const position = Math.round(video.currentTime * 1000);
        return definitions.find(definition =>
            fileStem(definition.fileName || '') === stem
            && position >= definition.startTime
            && position <= definition.endTime);
    }

    async function syncEdi(force = false) {
        if (video.paused || !currentItem()) return false;
        const definition = currentDefinition();
        if (!definition) {
            lastGallery = null;
            report('El video se reproduce, pero no hay una definición EDI para este instante.');
            return false;
        }
        if (!force && lastGallery === definition.name) return true;

        const seek = Math.max(0, Math.round(video.currentTime * 1000) - definition.startTime);
        await confirmedPlaybackCommand(`/Edi/Play/${encodeURIComponent(definition.name)}?seek=${seek}`);
        lastGallery = definition.name;
        report(`EDI sincronizado: ${definition.name}`);
        return true;
    }

    function startEdi() {
        if (video.paused || video.ended || video.seeking) return commandQueue;
        if (strokerPaused) {
            if (lastGallery !== currentDefinition()?.name) strokerNeedsResync = true;
            return commandQueue;
        }
        if (!ediStopped && lastGallery === currentDefinition()?.name) return commandQueue;
        const force = ediStopped;
        ediStopped = false;
        return enqueueCommand(async () => {
            try {
                if (video.paused || video.ended || video.seeking) {
                    ediStopped = true;
                    return;
                }
                ediStopped = !await syncEdi(force);
            } catch (error) {
                ediStopped = true;
                throw error;
            }
        });
    }

    function toggleStrokerPlayback() {
        if (strokerCommandPending) return commandQueue;

        const pauseStroker = !strokerPaused;
        strokerPaused = pauseStroker;
        strokerCommandPending = true;
        if (pauseStroker) strokerNeedsResync = false;
        renderPlaybackOptions();

        return enqueueCommand(async () => {
            try {
                if (pauseStroker) {
                    await confirmedPlaybackCommand('/Edi/Pause?untilResume=false');
                    report('El video continúa; stroker pausado.');
                    return;
                }

                if (strokerNeedsResync || lastGallery !== currentDefinition()?.name) {
                    ediStopped = !await syncEdi(true);
                } else {
                    await confirmedPlaybackCommand('/Edi/Resume?AtCurrentTime=true');
                    ediStopped = false;
                    report('Stroker reanudado al tiempo actual.');
                }
                strokerNeedsResync = false;
            } catch (error) {
                strokerPaused = !pauseStroker;
                throw error;
            } finally {
                strokerCommandPending = false;
                renderPlaybackOptions();
            }
        });
    }

    function stopEdi(reason = 'Reproducción detenida.') {
        strokerPaused = false;
        strokerNeedsResync = false;
        renderPlaybackOptions();
        if (ediStopped) {
            report(reason);
            return commandQueue;
        }
        ediStopped = true;
        lastGallery = null;
        return enqueueCommand(async () => {
            try {
                await confirmedPlaybackCommand('/Edi/Stop');
                report(reason);
            } finally {
                ediStopped = true;
                lastGallery = null;
            }
        });
    }

    async function selectVideo(id, autoplay = false) {
        const item = playlist.find(entry => entry.id === id);
        if (!item) return;
        if (currentId === id) {
            if (autoplay && video.paused) await video.play();
            return;
        }
        saveCurrentPosition();
        await stopEdi('Video anterior detenido.');
        currentId = id;
        localStorage.setItem(currentVideoKey, id);
        lastGallery = null;
        video.src = item.url;
        restorePosition(id);
        renderPlaylist();
        report(`Listo: ${item.name}`);
        if (autoplay) await video.play();
    }

    function moveVideo(id, offset) {
        const from = playlist.findIndex(item => item.id === id);
        const to = from + offset;
        if (from < 0 || to < 0 || to >= playlist.length) return;
        [playlist[from], playlist[to]] = [playlist[to], playlist[from]];
        renderPlaylist();
    }

    async function deleteVideo(id) {
        const index = playlist.findIndex(item => item.id === id);
        if (index < 0) return;

        const [removed] = playlist.splice(index, 1);
        clearSavedPosition(id);
        URL.revokeObjectURL(removed.url);
        if (currentId === id) {
            await stopEdi('Video eliminado; EDI detenido.');
            currentId = null;
            localStorage.removeItem(currentVideoKey);
            video.removeAttribute('src');
            video.load();
            if (playlist.length) {
                await selectVideo(playlist[Math.min(index, playlist.length - 1)].id);
            }
        }

        renderPlaylist();
        report(`Eliminado: ${removed.name}`);
    }

    async function clearPlaylist() {
        if (!window.confirm('¿Borrar la playlist y todos los assets EDI?')) return;
        suppressPause = true;
        video.pause();
        suppressPause = false;
        await withStore(assetStore, 'readwrite', store => store.clear());
        await api('/Edi/Assets', { method: 'DELETE' });
        definitions = [];
        ediStopped = true;
        lastGallery = null;
        playlist.forEach(item => URL.revokeObjectURL(item.url));
        playlist.length = 0;
        currentId = null;
        localStorage.removeItem(currentVideoKey);
        savedPositions = {};
        localStorage.removeItem(playbackPositionsKey);
        video.removeAttribute('src');
        video.load();
        renderPlaylist();
        await clearStoredVideos();
        report('Playlist y assets EDI eliminados.');
    }

    function renderPlaylist() {
        playlistElement.replaceChildren();
        playlistElement.classList.toggle('playlist-scroll', playlist.length > 10);
        playlistCount.textContent = `${playlist.length} ${playlist.length === 1 ? 'video' : 'videos'}`;
        const durations = playlist.map(ediDuration).filter(Number.isFinite);
        playlistDurationTotal.textContent = formatDuration(durations.reduce((total, duration) => total + duration, 0));
        if (!playlist.length) {
            const empty = document.createElement('li');
            empty.className = 'p-3 text-muted';
            empty.textContent = 'Todavía no agregaste videos.';
            playlistElement.append(empty);
            return;
        }

        playlist.forEach((item, index) => {
            const row = document.createElement('li');
            row.className = `playlist-item${item.id === currentId ? ' active' : ''}`;
            row.draggable = true;

            const handle = document.createElement('span');
            handle.textContent = '↕';
            handle.className = 'text-muted';
            handle.title = 'Arrastrar para ordenar';

            const name = document.createElement('span');
            name.className = 'playlist-name';
            name.textContent = item.name;

            const duration = document.createElement('span');
            duration.className = 'playlist-duration';
            duration.textContent = formatDuration(ediDuration(item));

            const up = document.createElement('button');
            up.type = 'button';
            up.className = 'btn btn-sm btn-outline-secondary';
            up.textContent = '↑';
            up.title = 'Subir';
            up.disabled = index === 0;
            up.addEventListener('click', event => {
                event.stopPropagation();
                moveVideo(item.id, -1);
            });

            const down = document.createElement('button');
            down.type = 'button';
            down.className = 'btn btn-sm btn-outline-secondary';
            down.textContent = '↓';
            down.title = 'Bajar';
            down.disabled = index === playlist.length - 1;
            down.addEventListener('click', event => {
                event.stopPropagation();
                moveVideo(item.id, 1);
            });

            const remove = document.createElement('button');
            remove.type = 'button';
            remove.className = 'btn btn-sm btn-outline-danger';
            remove.textContent = 'Eliminar';
            remove.title = `Eliminar ${item.name}`;
            remove.addEventListener('click', event => {
                event.stopPropagation();
                deleteVideo(item.id).catch(error => report(`No se pudo eliminar el video: ${error.message}`, true));
            });

            row.append(handle, name, duration, up, down, remove);
            row.addEventListener('click', () => selectVideo(item.id, playbackOptions.autoplay)
                .catch(error => report(`No se pudo iniciar la reproducción: ${error.message}`, true)));
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
                const from = playlist.findIndex(entry => entry.id === draggedId);
                const to = playlist.findIndex(entry => entry.id === item.id);
                if (from < 0 || to < 0 || from === to) return;
                const [moved] = playlist.splice(from, 1);
                playlist.splice(to, 0, moved);
                renderPlaylist();
            });
            playlistElement.append(row);
        });
    }

    async function uploadAssets(files) {
        const form = new FormData();
        files.forEach(file => form.append('files', file, file.name));
        await stopEdi('Actualizando assets EDI...');
        const response = await api('/Edi/Assets', { method: 'POST', body: form });
        definitions = await response.json();
        renderPlaylist();
    }

    async function recoverUploadedAssets() {
        const paths = await (await api('/Edi/Assets')).json();
        const uploadPaths = paths.filter(path => {
            if (typeof path !== 'string' || !path.toLowerCase().startsWith('/edi/upload/')) return false;
            const name = decodeURIComponent(path.split('/').pop() || '');
            return name.toLowerCase() !== 'definitions_auto.csv' && isEdiAsset({ name });
        });
        return Promise.all(uploadPaths.map(async path => {
            const response = await api(path);
            const name = decodeURIComponent(path.split('/').pop() || 'asset');
            return new File([await response.blob()], name);
        }));
    }

    async function reloadAssets() {
        try {
            report('Recargando assets EDI...');
            const assets = await mergeAssets(await recoverUploadedAssets());
            if (!assets.length) {
                await loadDefinitions();
                report('No hay assets guardados para volver a subir.', true);
                return;
            }
            await uploadAssets(assets);
            report(`${assets.length} asset${assets.length === 1 ? '' : 's'} EDI recargado${assets.length === 1 ? '' : 's'}.`);
        } catch (error) {
            report(`No se pudieron recargar los assets: ${error.message}`, true);
        }
    }

    async function addFiles(fileList) {
        const files = Array.from(fileList);
        const videos = files.filter(isVideo);
        const assets = files.filter(file => !isVideo(file) && isEdiAsset(file));
        const ignored = files.length - videos.length - assets.length;

        for (const file of videos) {
            playlist.push({ id: crypto.randomUUID(), name: file.name, file, url: URL.createObjectURL(file) });
        }
        renderPlaylist();

        if (!currentItem() && playlist.length) await selectVideo(playlist[0].id);

        let uploadError = null;
        try {
            if (assets.length) {
                const mergedAssets = await mergeAssets(assets);
                await uploadAssets(mergedAssets);
                await saveAssets(mergedAssets);
            }
        } catch (error) {
            uploadError = error;
        }

        const parts = [];
        if (videos.length) parts.push(`${videos.length} video${videos.length === 1 ? '' : 's'} local${videos.length === 1 ? '' : 'es'} agregado${videos.length === 1 ? '' : 's'}`);
        if (assets.length && !uploadError) parts.push(`${assets.length} asset${assets.length === 1 ? '' : 's'} EDI subido${assets.length === 1 ? '' : 's'}`);
        if (ignored) parts.push(`${ignored} archivo${ignored === 1 ? '' : 's'} ignorado${ignored === 1 ? '' : 's'}`);
        if (uploadError) parts.push(`error de subida: ${uploadError.message}`);
        report(parts.length ? parts.join(', ') : 'No se encontraron videos ni assets compatibles.', Boolean(uploadError || !parts.length));
    }

    async function loadDefinitions() {
        try {
            report('Cargando assets EDI...');
            definitions = await (await api('/Edi/Definitions')).json();
            renderPlaylist();
            report(playlist.length ? 'Assets EDI actualizados.' : 'Agregá videos y assets para empezar.');
        } catch (error) {
            report(`No se pudo cargar EDI: ${error.message}`, true);
        }
    }

    fileInput.addEventListener('change', async () => {
        await addFiles(fileInput.files);
        fileInput.value = '';
    });
    addMediaFiles.addEventListener('click', () => fileInput.click());
    window.addEventListener('dragover', event => {
        if (isFileDrag(event)) event.preventDefault();
    }, true);
    window.addEventListener('drop', event => {
        if (!isFileDrag(event)) return;
        event.preventDefault();
        dropZone.classList.remove('drag-over');
    }, true);
    dropZone.addEventListener('dragover', event => {
        if (!isFileDrag(event)) return;
        event.preventDefault();
        dropZone.classList.add('drag-over');
    });
    dropZone.addEventListener('dragleave', event => {
        if (!dropZone.contains(event.relatedTarget)) dropZone.classList.remove('drag-over');
    });
    dropZone.addEventListener('drop', async event => {
        if (!event.dataTransfer.files.length) return;
        event.preventDefault();
        event.stopPropagation();
        dropZone.classList.remove('drag-over');
        await addFiles(event.dataTransfer.files);
    });

    video.addEventListener('play', () => startEdi());
    video.addEventListener('playing', () => startEdi());
    video.addEventListener('focus', releaseVideoControlFocus, true);
    video.addEventListener('focusin', releaseVideoControlFocus, true);
    video.addEventListener('pointerup', releaseVideoControlFocus, true);
    video.addEventListener('mouseup', releaseVideoControlFocus, true);
    video.addEventListener('click', handleVideoClick, true);
    video.addEventListener('dblclick', toggleVideoFullscreen, true);
    video.addEventListener('mousemove', updateFullscreenControls, true);
    document.addEventListener('fullscreenchange', () => updateFullscreenControls());
    video.addEventListener('waiting', () => {
        if (strokerPaused) {
            strokerNeedsResync = true;
            return;
        }
        if (!video.paused) stopEdi('EDI detenido mientras el video carga.');
    });
    video.addEventListener('stalled', () => {
        if (strokerPaused) {
            strokerNeedsResync = true;
            return;
        }
        if (!video.paused) stopEdi('EDI detenido por falta de datos del video.');
    });
    video.addEventListener('seeking', () => {
        if (strokerPaused) {
            strokerNeedsResync = true;
            return;
        }
        if (!video.paused) stopEdi('EDI detenido durante la búsqueda.');
    });
    video.addEventListener('seeked', () => {
        saveCurrentPosition();
        if (!video.paused) startEdi();
    });
    video.addEventListener('ratechange', () => {
        if (strokerPaused) {
            strokerNeedsResync = true;
            return;
        }
        if (!video.paused) stopEdi('Reajustando EDI al cambio de velocidad.').then(startEdi);
    });
    video.addEventListener('timeupdate', () => startEdi());
    video.addEventListener('pause', () => {
        saveCurrentPosition();
        if (suppressPause || video.ended) return;
        stopEdi('Video pausado; EDI detenido.');
    });
    video.addEventListener('ended', async () => {
        clearSavedPosition(currentId);
        await stopEdi('El video terminó; EDI detenido.');
        if (playbackOptions.loop) {
            video.currentTime = 0;
            await video.play();
            return;
        }
        if (!playbackOptions.autoplay) return;
        const index = playlist.findIndex(item => item.id === currentId);
        if (index >= 0 && index + 1 < playlist.length) {
            await selectVideo(playlist[index + 1].id, true);
        }
    });
    video.addEventListener('error', () => stopEdi('EDI detenido por un error del video.'));
    video.addEventListener('abort', () => stopEdi('EDI detenido porque se canceló la carga del video.'));

    document.getElementById('stopPlayer').addEventListener('click', async () => {
        suppressPause = true;
        video.pause();
        video.currentTime = 0;
        suppressPause = false;
        await stopEdi();
    });

    document.getElementById('playFullscreen').addEventListener('click', async () => {
        if (!currentItem()) {
            report('Primero agregá un video a la playlist.', true);
            return;
        }
        try {
            await video.play();
            if (video.requestFullscreen) await video.requestFullscreen();
        } catch (error) {
            report(`No se pudo iniciar la reproducción: ${error.message}`, true);
        }
    });
    document.getElementById('refreshPlayer').addEventListener('click', reloadAssets);
    document.getElementById('clearPlaylist').addEventListener('click', () => {
        clearPlaylist().catch(error => report(`No se pudo borrar la playlist: ${error.message}`, true));
    });
    document.getElementById('togglePlaylist').addEventListener('click', event => {
        const collapsed = !playlistElement.hidden;
        playlistElement.hidden = collapsed;
        event.currentTarget.textContent = collapsed ? 'Expandir' : 'Contraer';
        event.currentTarget.setAttribute('aria-expanded', String(!collapsed));
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
            if (!currentId) return;
            event.preventDefault();
            event.stopPropagation();
            deleteVideo(currentId).catch(error => report(`No se pudo eliminar el video: ${error.message}`, true));
            return;
        }
        if (event.code !== 'Space' || event.repeat || event.altKey || event.ctrlKey || event.metaKey) return;
        event.preventDefault();
        event.stopPropagation();
        if (!currentItem()) {
            report('Primero agregá un video a la playlist.', true);
            return;
        }
        if (playbackOptions.stroker) {
            if (video.paused) {
                strokerPaused = false;
                strokerNeedsResync = false;
                ediStopped = true;
                lastGallery = null;
                renderPlaybackOptions();
                video.play().catch(error => report(`No se pudo iniciar la reproducción: ${error.message}`, true));
                return;
            }
            toggleStrokerPlayback();
            return;
        }
        if (video.paused) {
            video.play().catch(error => report(`No se pudo iniciar la reproducción: ${error.message}`, true));
        } else {
            video.pause();
        }
    }, true);
    window.addEventListener('pagehide', () => {
        saveCurrentPosition();
        playlist.forEach(item => URL.revokeObjectURL(item.url));
        if (!video.paused) navigator.sendBeacon('/Edi/Stop');
    });
    setInterval(() => {
        if (!video.paused) startEdi();
    }, 30000);
    setInterval(saveCurrentPosition, 5000);
    async function initialize() {
        renderPlaybackOptions();
        await loadDefinitions();
        try {
            await clearStoredVideos();
            renderPlaylist();
        } catch (error) {
            report(`No se pudieron limpiar los videos guardados anteriormente: ${error.message}`, true);
        }
    }

    initialize();
})();
