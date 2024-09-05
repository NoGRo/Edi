
import Edi from './EdiClient.js'
import videoSync from './videoSync.js'
const videoExtensions = ['.mp4', '.avi', '.mov', '.wmv', '.mkv'];

var timerSave = 0;
class Player {
    constructor() {
        this.Skills = SkillManager
        this.State = stateSync
        this.Device = Edi
        this.videoSync = videoSync
    }
    async Init() {
        videoSync.Init();
        await stateSync.Init();
    }

    Metadata = {
        async GetVideos() {
            try {
                const files = await Edi.getAssets('');
                const videoFiles = files.filter(file => videoExtensions.some(ext => file.endsWith(ext)));
                return videoFiles;
            } catch (error) {
                console.error('Error fetching video files:', error);
                return [];
            }
        },
        async GetFunscripts() {
            try {
                const files = await Edi.getAssets('');
                let allFunscripts = [];
                for (const file of files.filter(f => f.endsWith('.funscript'))) {
                    const response = await fetch(`${file}?cacheBuster=${new Date().getTime()}`); // Espera a que la solicitud fetch se complete
                    if (!response.ok) {
                        throw new Error(`HTTP error! status: ${response.status}`);
                    }
                    const funscript = await response.json(); // Espera a que la promesa de .json() se resuelva
                    allFunscripts.push(funscript); // Asumiendo que cada funscript es un objeto y no un array
                }
                console.log('All funscrips:', allFunscripts);

                return allFunscripts;
            } catch (error) {
                console.error('Error:', error);
            }
        },
        async GetDefinitions() {
            return await Edi.getDefinitions()
        },
        async GetAssets(path) {
            return await Edi.getAssets(path)
        },
        getCurrentChapter() {
            return videoSync.getCurrentChapter();
        }

    }
    Video = {}

    Start() {
        if(this.Video.src != this.State.videoSrc)
            this.Video.src = this.State.videoSrc;
        if (this.State.checkPointTime) { //)&& (new Date().getTime() - this.State.lastSave) > (5 * 60 * 1000)) {
            this.Video.currentTime = this.State.checkPointTime;
        }
    }
    Reset(){
        this.State.Reset();
        this.Video.pause();
        this.Device.pause()
        this.Start();
    }
    async executeCommand(command){
        await program.parseAsync(['node', 'videoSync.js', ...bookmark.name.split(' ')]);
    }
    
    Play(Name, seek) {

    }
    Pause() {

    }
    Say(Text) {

    }

};

export default new Player;
