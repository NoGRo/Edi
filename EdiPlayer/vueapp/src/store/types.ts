export interface DefinitionGallery {
    name: string;
    type: string;
    fileName: string;
    startTime: number;
    endTime: number;
    duration?: number;
    loop: boolean;
}

export interface State {
    definitions: DefinitionGallery[];
}

