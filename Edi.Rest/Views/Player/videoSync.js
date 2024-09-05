import Edi from './EdiClient.js'

import $ from 'jquery'
var intervalTimer = 0
var lastSync = 0
var Video= { }
const currentChapter = Symbol('currentChapter');
class videoSync {
    funscript = {}
    definitions = {}
    [currentChapter] 
    async Init() {

        this.definitions = await Edi.GetDefinitions()
        this.funscript = (await player.Metadata.GetFunscripts())[0];

        var $video = $('video']);

        $video.onSingleEvent('play playing seeked', () => {
            this.playEdiFromVideo();
        });

        $video.onSingleEvent('pause ended waiting', Edi.pause);
        Video = $video[0]

    }

    // Convertir tiempos a milisegundos
    timeToMs(timeStr) {
        const [hours, minutes, seconds] = timeStr.split(':').map(parseFloat);
        return ((hours * 60 + minutes) * 60 + seconds);
    }
    File() { return decodeURIComponent(new URL(Video.src).pathname.split('/').pop().split('.')[0]) }
    Seek() { return parseInt(Video.currentTime * 1000) }
    async playEdiFromVideo() {
        //only file name 
        if (Video.paused)
            return;

        var file = decodeURIComponent(new URL(Video.src).pathname.split('/').pop().split('.')[0]);
        var seek = parseInt(Video.currentTime * 1000)
        // Filtra los elementos por el nombre del archivo
        const filteredGallerys = this.definitions.filter(element => element.fileName === file);

        // Encuentra el elemento cuyo rango de tiempo incluye el seek
        const foundGallery = filteredGallerys.find(element => seek >= element.startTime && seek <= element.endTime);


        if (!foundGallery) {
            console.log("No se encontró un elemento que coincida con los criterios.");ese
            return null;
        }

        const relativeSeek = seek - foundGallery.startTime;
        lastSync = foundGallery.name

        // Trigger startGallery event
        $(document).trigger('startGallery', { galleryName: foundGallery.name, relativeSeek: relativeSeek });

        await Edi.play(foundGallery.name, relativeSeek)

        if (intervalTimer) {
            clearInterval(intervalTimer);
        }

        intervalTimer = setInterval(this.executeChaptersAndBookmarks.bind(this), 1000);

        this.executeChaptersAndBookmarks();
    }
    // Función para actualizar capítulos y marcadores
    executeChaptersAndBookmarks() {
        if (Video.paused) {
            if (intervalTimer)
                clearInterval(intervalTimer);
            return
        }

        let currentBookmarks = [];

        const currentTimeMs = Video.currentTime;
        const oneSecondFutureMs = currentTimeMs + 1; // Calcula un segundo en el futuro
        // Chapters event in this current second
        var file = this.File();
        var seek = this.Seek();

        const foundGallery = this.definitions.find(element => element.fileName === file && seek <= element.startTime && (seek + 1000) > element.startTime);


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

            }).bind(this), foundGallery.startTime - seek)
        }

        currentBookmarks.forEach(bookmark => {
            console.log(`execute bookmark ${bookmark.name}`);
            setTimeout(async () => {
                $(document).trigger('bookmarkFound', { bookmarkName: bookmark.name, timeRemaining: bookmark.timeRemaining });
            }, parseInt(bookmark.timeRemaining));
        });

        if (foundGallery && (seek + 1000) >= foundGallery.endTime) {
            const relativeSeek = seek + 1000 - foundGallery.startTime;
            setTimeout(() => {
                $(document).trigger('endGallery', { galleryName: foundGallery.name, relativeSeek: relativeSeek });
            }, foundGallery.endTime - seek);
        }
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
(function($) {
    $.fn.onSingleEvent = function(eventTypes, callback, delay = 50) {
        let timer = null;

        this.on(eventTypes, function() {
            clearTimeout(timer);
            timer = setTimeout(() => {
                callback.apply(this, arguments);
            }, delay);
        });

        return this;
    };
})(jQuery);
const videosync = new videoSync();
export default videosync;