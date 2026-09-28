const videoExtensions = [".mp4",".webm",".avi",".mkv",".mov"];
export const extension = name => {
    const dot = name.lastIndexOf('.');
    return dot >= 0 ? name.slice(dot).toLowerCase() : '';
};

export const fileStem = name => {
    const dot = name.lastIndexOf('.');
    return (dot >= 0 ? name.slice(0, dot) : name).toLowerCase();
};

export const isVideo = file => file.type.startsWith('video/') || videoExtensions.includes(extension(file.name));
export const isFileDrag = event => Array.from(event.dataTransfer?.types || []).includes('Files');
export const isEdiAsset = file => {
    const name = file.name.toLowerCase();
    return name.endsWith('.funscript')
        || name.endsWith('.mp3')
        || name === 'definitions.csv'
        || name === 'definitions_auto.csv'
        || name.startsWith('bundledefinition') && name.endsWith('.txt');
};

