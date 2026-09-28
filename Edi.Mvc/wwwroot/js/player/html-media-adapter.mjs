/**
 * Playback port consumed by synchronization, positions, playlist and playback events.
 * External players implement this same port without exposing a DOM element.
 * A Quest/SBS presentation can render this element as a texture independently of EDI.
 */
export function createHtmlMediaAdapter(element) {
    return {
        get currentTime() { return element.currentTime; },
        set currentTime(value) { element.currentTime = value; },
        get duration() { return element.duration; },
        get paused() { return element.paused; },
        get ended() { return element.ended; },
        get seeking() { return element.seeking; },
        get readyState() { return element.readyState; },
        play: () => element.play(),
        pause: () => element.pause(),
        setSource: item => { element.src = item.url; },
        clearSource: () => { element.removeAttribute('src'); element.load(); },
        addEventListener: (...args) => element.addEventListener(...args),
        removeEventListener: (...args) => element.removeEventListener(...args),
    };
}
