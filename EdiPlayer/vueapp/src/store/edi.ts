import { createStore, StoreOptions } from 'vuex';
import axios from 'axios';
import { State, DefinitionGallery } from './types';

const store: StoreOptions<State> = {
    state: {
        definitions: [],
    },
    mutations: {
        SET_DEFINITIONS(state, definitions: DefinitionGallery[]) {
            state.definitions = definitions;
        },
    },
    actions: {
        async play({ commit }, { name, seek = 0 }: { name: string; seek?: number }) {
            try {
                await axios.post(`/Edi/Play/${name}`, null, { params: { seek } });
            } catch (error) {
                console.error('Error playing video:', error);
            }
        },
        async stop() {
            try {
                await axios.post('/Edi/Stop');
            } catch (error) {
                console.error('Error stopping video:', error);
            }
        },
        async pause() {
            try {
                await axios.post('/Edi/Pause');
            } catch (error) {
                console.error('Error pausing video:', error);
            }
        },
        async resume({ commit }, atCurrentTime = false) {
            try {
                await axios.post('/Edi/Resume', null, { params: { AtCurrentTime: atCurrentTime } });
            } catch (error) {
                console.error('Error resuming video:', error);
            }
        },
        async loadFile({ commit }, { path, SeachForAssets = true }: { path: string; SeachForAssets?: boolean }) {
            try {
                var response = await axios.post('/Edi/LoadFile', null, { params: { path, SeachForAssets } });
                commit('SET_DEFINITIONS', response.data);
            } catch (error) {
                console.error('Error loading file:', error);
            }
        },
        async fetchDefinitions({ commit }) {
            try {
                const response = await axios.get('/Edi/Definitions');
                commit('SET_DEFINITIONS', response.data);
            } catch (error) {
                console.error('Error fetching definitions:', error);
            }
        }
    },
    getters: {
        definitions: (state) => state.definitions,
    },
};

export default createStore(store);
