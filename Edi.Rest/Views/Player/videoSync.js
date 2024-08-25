import player from './player.js'
import $ from 'jquery'
var intervalTimer = 0
var lastSync = 0
const currentChapter = Symbol('currentChapter');
class videoSync {
    funscript = {}
    definitions = {}
    [currentChapter] 
    async Init() {

        this.definitions = await player.Metadata.GetDefinitions()
        this.funscript = (await player.Metadata.GetFunscripts())[0];

        var $video = $(player.Video);
        $video.on('play', () => this.playEdiFromVideo())
        $video.on('playing', () => this.playEdiFromVideo())
        $video.on('seeked', () => this.playEdiFromVideo())

        const pause = player.Device.pause;
        $video.on('pause', pause)
        $video.on('ended', pause)
        $video.on('waiting', pause)

    }

    // Convertir tiempos a milisegundos
    timeToMs(timeStr) {
        const [hours, minutes, seconds] = timeStr.split(':').map(parseFloat);
        return ((hours * 60 + minutes) * 60 + seconds);
    }
    File() { return decodeURIComponent(new URL(player.Video.src).pathname.split('/').pop().split('.')[0]) }
    Seek() { return parseInt(player.Video.currentTime * 1000) }
    async playEdiFromVideo() {
        //only file name 
        if (player.Video.paused)
            return;

        var file = decodeURIComponent(new URL(player.Video.src).pathname.split('/').pop().split('.')[0]);
        var seek = parseInt(player.Video.currentTime * 1000)
        // Filtra los elementos por el nombre del archivo
        const filteredGallerys = this.definitions.filter(element => element.fileName === file);

        // Encuentra el elemento cuyo rango de tiempo incluye el seek
        const foundGallery = filteredGallerys.find(element => seek >= element.startTime && seek <= element.endTime);
        if (intervalTimer) {
            clearInterval(intervalTimer);
        }

        intervalTimer = setInterval(this.executeChaptersAndBookmarks.bind(this), 1000);

        if (!foundGallery) {
            console.log("No se encontró un elemento que coincida con los criterios.");ese
            return null;
        }

        const relativeSeek = seek - foundGallery.startTime;
        lastSync = foundGallery.name
        await player.Device.play(foundGallery.name, relativeSeek)
        this.executeChaptersAndBookmarks();
    }
    // Función para actualizar capítulos y marcadores
    executeChaptersAndBookmarks() {
        if (player.Video.paused) {
            if (intervalTimer)
                clearInterval(intervalTimer);
            return
        }

        let currentChapters = [];
        let currentBookmarks = [];

        const currentTimeMs = player.Video.currentTime;
        const oneSecondFutureMs = currentTimeMs + 1; // Calcula un segundo en el futuro
        // Chapters event in this current second
        var file = this.File();
        var seek = this.Seek();

        const foundGallery = this.definitions.find(element => element.fileName === file && seek <= element.startTime && (seek + 1000) > element.startTime);

        currentChapters = this.funscript.metadata.chapters.reduce((acc, chapter) => {
            chapter.startMs = this.timeToMs(chapter.startTime);
            chapter.endMs = this.timeToMs(chapter.endTime);


            // Incluye capítulos que están activos en el segundo actual o que comenzarán en el próximo segundo
            if (currentTimeMs <= chapter.startMs && chapter.startMs < oneSecondFutureMs) {
                // Capítulo activo en el segundo actual
                acc.push({ ...chapter, state: 'start', timeRemaining: chapter.endMs - currentTimeMs });
            } else if (currentTimeMs <= chapter.endMs && chapter.endMs < oneSecondFutureMs) {
                // Capítulo que comenzará en el próximo segundo
                acc.push({ ...chapter, state: 'end', timeRemaining: chapter.endMs - currentTimeMs });
            }

            return acc;
        }, []);
        // bookmarks event in this current second
        this.funscript.metadata.bookmarks.forEach(bookmark => {
            const bookmarkTimeMs = this.timeToMs(bookmark.time);
            if (currentTimeMs <= bookmarkTimeMs && bookmarkTimeMs < oneSecondFutureMs) { // ventana de 1 segundo para coincidencias
                currentBookmarks.push({ ...bookmark, timeRemaining: bookmarkTimeMs - currentTimeMs });
            }
        });

        // Procesar el próximo evento de capítulo si existe
        if (foundGallery && foundGallery.name !== lastSync) {
            setTimeout((() => {
                console.log('chaptersync ' + foundGallery.name)
                this.playEdiFromVideo()
                player.State.checkPointTime = foundGallery.startTime / 1000;
                player.State.Save();
            }).bind(this), foundGallery.startTime - seek)
        }

        currentBookmarks
            .forEach(bookmark => {
                console.log(`execute bookmark ${bookmark.name}`)
                setTimeout(async () => {
                    player.executeCommand(bookmark);
                }, parseInt(bookmark.timeRemaining));
            });
    }
  
    getCurrentChapter() {
        const currentTime = this.Seek(); // Obtén el tiempo actual en milisegundos
        const file = this.File()
        // Actualiza el capítulo actual solo si es necesario
        if (!this[currentChapter] || currentTime < this[currentChapter].startTime || currentTime > this[currentChapter].endTime) {
            this[currentChapter] = this.definitions.find(({startTime, endTime,fileName}) => fileName == file && currentTime >= startTime && currentTime <= endTime) || null;
        }
        return this[currentChapter];
    }
}
const videosync = new videoSync();
export default videosync;