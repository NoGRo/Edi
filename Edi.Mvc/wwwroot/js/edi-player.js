(() => {
    const videoExtensions = ['.mp4', '.webm', '.avi', '.mkv', '.mov'];
    const video = document.getElementById('videoPlayer');
    const select = document.getElementById('videoSelect');
    const status = document.getElementById('playerStatus');
    let definitions = [];
    let lastGallery = null;
    let suppressPause = false;
    let ediStopped = true;
    let commandQueue = Promise.resolve();

    const fileStem = path => {
        const name = decodeURIComponent(path.split('/').pop() || '');
        const dot = name.lastIndexOf('.');
        return (dot >= 0 ? name.slice(0, dot) : name).toLowerCase();
    };

    const normalizeAssetPath = path =>
        path.replace(/^\/Edi\/Assets\/+/i, '/Edi/Assets/');

    async function api(path, options = {}) {
        const method = options.method || 'GET';
        console.info(`[EDI] ${method} ${path}`);
        const response = await fetch(path, options);
        if (!response.ok) {
            const detail = await response.text();
            console.error(`[EDI] ${method} ${path} -> ${response.status}`, detail);
            throw new Error(detail || `${response.status} ${response.statusText}`);
        }
        console.info(`[EDI] ${method} ${path} -> ${response.status}`);
        return response;
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

    function currentDefinition() {
        const stem = fileStem(select.value);
        const position = Math.round(video.currentTime * 1000);
        return definitions.find(item =>
            fileStem(item.fileName || '') === stem &&
            position >= item.startTime && position <= item.endTime);
    }

    async function syncEdi(force = false) {
        if (video.paused || !select.value) return false;
        const definition = currentDefinition();
        if (!definition) {
            lastGallery = null;
            report('El video se reproduce, pero no hay una definición EDI para este instante.');
            return false;
        }
        if (!force && lastGallery === definition.name) return true;

        const seek = Math.max(0, Math.round(video.currentTime * 1000) - definition.startTime);
        await api(`/Edi/Play/${encodeURIComponent(definition.name)}?seek=${seek}`, { method: 'POST' });
        lastGallery = definition.name;
        report(`EDI sincronizado: ${definition.name}`);
        return true;
    }

    function startEdi() {
        return enqueueCommand(async () => {
            if (video.paused || video.ended) return;
            const started = await syncEdi(ediStopped);
            if (started) ediStopped = false;
        });
    }

    function stopEdi(reason = 'Reproducción detenida.') {
        if (ediStopped) {
            report(reason);
            return commandQueue;
        }
        ediStopped = true;
        lastGallery = null;
        return enqueueCommand(async () => {
            await api('/Edi/Stop', { method: 'POST' });
            report(reason);
        });
    }

    function selectVideo(path) {
        lastGallery = null;
        video.src = path;
        report(`Listo: ${decodeURIComponent(path.split('/').pop() || path)}`);
    }

    async function load() {
        try {
            report('Cargando galería...');
            const [assetsResponse, definitionsResponse] = await Promise.all([
                api('/Edi/Assets'),
                api('/Edi/Definitions')
            ]);
            const assets = await assetsResponse.json();
            definitions = await definitionsResponse.json();
            const videos = assets
                .map(normalizeAssetPath)
                .filter(path => videoExtensions.some(ext => path.toLowerCase().endsWith(ext)));

            select.replaceChildren();
            if (!videos.length) {
                select.append(new Option('No se encontraron videos', ''));
                select.disabled = true;
                video.removeAttribute('src');
                report('La carpeta configurada no contiene videos compatibles.', true);
                return;
            }

            for (const path of videos) {
                select.append(new Option(decodeURIComponent(path.split('/').pop() || path), path));
            }
            select.disabled = false;
            selectVideo(videos[0]);
        } catch (error) {
            report(`No se pudo cargar EDI: ${error.message}`, true);
        }
    }

    select.addEventListener('change', () => {
        stopEdi('Video anterior detenido.');
        selectVideo(select.value);
    });
    video.addEventListener('play', startEdi);
    video.addEventListener('playing', startEdi);
    video.addEventListener('waiting', () => {
        if (!video.paused) stopEdi('EDI detenido mientras el video carga.');
    });
    video.addEventListener('stalled', () => {
        if (!video.paused) stopEdi('EDI detenido por falta de datos del video.');
    });
    video.addEventListener('seeking', () => {
        if (!video.paused) stopEdi('EDI detenido durante la búsqueda.');
    });
    video.addEventListener('seeked', () => {
        if (!video.paused) startEdi();
    });
    video.addEventListener('ratechange', () => {
        if (!video.paused) stopEdi('Reajustando EDI al cambio de velocidad.').then(startEdi);
    });
    video.addEventListener('timeupdate', () => {
        if (!ediStopped) syncEdi().catch(error => report(`Error EDI: ${error.message}`, true));
    });
    video.addEventListener('pause', () => {
        if (suppressPause || video.ended) return;
        stopEdi('Video pausado; EDI detenido.');
    });
    video.addEventListener('ended', async () => {
        await stopEdi('El video terminó; EDI detenido.');
        if (!document.getElementById('autoplayCheck').checked) return;
        const next = select.selectedIndex + 1;
        if (next < select.options.length) {
            select.selectedIndex = next;
            selectVideo(select.value);
            await video.play();
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
        try {
            await video.play();
            if (video.requestFullscreen) await video.requestFullscreen();
        } catch (error) {
            report(`No se pudo iniciar la reproducción: ${error.message}`, true);
        }
    });
    document.getElementById('refreshPlayer').addEventListener('click', load);
    window.addEventListener('pagehide', () => {
        if (!video.paused) navigator.sendBeacon('/Edi/Stop');
    });
    setInterval(() => {
        if (!video.paused && !ediStopped) startEdi();
    }, 30000);
    load();
})();
