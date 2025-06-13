import axios from 'axios';
const baseURL = 'http://localhost:5000';
// Axios client class for the Edi REST API
const apiClient = axios.create({
    baseURL: baseURL, // Set the base URL for the API
    timeout: 1000,
});
class Edi {
    constructor() {

    }

    // Method to play content by name
    async play(name, seek = 0) {
        try {
            const response = await apiClient.post(`/Edi/Play/${name}?seek=${seek}`);
            console.log(`/Edi/Play/${name}?seek=${seek}`);
        } catch (error) {
            console.error('Error executing play:', error);
        }
    }

    // Method to stop playback
    async stop() {
        try {
            const response = await apiClient.post('/Edi/Stop');
            console.log(`/Edi/Stop`);
        } catch (error) {
            console.error('Error executing stop:', error);
        }
    }

    // Method to pause playback
    async pause() {
        try {
            const response = await apiClient.post('/Edi/Pause');

            console.log(`/Edi/Pause`);

        } catch (error) {
            console.error('Error executing pause:', error);
        }
    }

    // Method to resume playback
    async resume(AtCurrentTime = false) {
        try {
            const response = await apiClient.post(`/Edi/Resume?AtCurrentTime=${AtCurrentTime}`);
            console.log(`/Edi/Resume?AtCurrentTime=${AtCurrentTime}`);
        } catch (error) {
            console.error('Error executing resume:', error);
        }
    }

    // Method to get definitions
    async getDefinitions() {
        try {
            const response = await apiClient.get(`/Edi/Definitions?cacheBuster=${new Date().getTime()}`);
            console.log(response.data);
            return response.data;
        } catch (error) {
            console.error('Error fetching definitions:', error);
        }
    }

    // Method to get assets by path
    async getAssets(path) {
        try {
            const response = await apiClient.get(`/Edi/Assets?cacheBuster=${new Date().getTime()}`);
            console.log('/Edi/Assets');
            return response.data.map(x=> baseURL + x );
        } catch (error) {
            console.error('Error fetching assets:', error);
        }
    }
}
export default new Edi();
// Example usage
// edi.play('exampleName', 10);
