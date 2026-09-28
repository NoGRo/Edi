import { api } from './edi-api.mjs';
import * as storage from './asset-storage.mjs';
import { fileStem, isEdiAsset } from './media-files.mjs';

const isSource = file => file.name.toLowerCase() !== 'definitions_auto.csv';
const byName = files => new Map(files.filter(isSource).map(file => [file.name.toLowerCase(), file]));

/** Owns the persistent library and the assets known to be loaded on the server. */
export function createAssetManager({ request = api, repository = storage,
    notifyPersistence = files => document.dispatchEvent(new CustomEvent('edi-assets-persist-requested', { detail: { files } })),
    publish = files => {
        window.ediPlayerAssetCache = [...files];
        document.dispatchEvent(new CustomEvent('edi-assets-cached', { detail: { files: window.ediPlayerAssetCache } }));
    } } = {}) {
    let cached = null;
    let uploaded = new Map();
    let fetching = null;
    let persistQueue = Promise.resolve();
    let cacheRevision = 0;

    function remember(files, { onServer = false } = {}) {
        cacheRevision++;
        cached = [...files];
        if (onServer) uploaded = byName(files);
        publish(cached);
    }

    async function restore() {
        const revision = cacheRevision;
        const files = (await repository.getStoredItems('assets')).map(record => record.file).filter(Boolean);
        if (files.length && revision === cacheRevision) remember(files);
    }

    function save(files) {
        const snapshot = [...byName(files).values()];
        const saving = persistQueue.then(() => repository.saveAssets(snapshot));
        persistQueue = saving.catch(() => {});
        return saving;
    }

    async function merge(files) {
        await persistQueue;
        const records = await repository.getStoredItems('assets');
        const merged = byName(records.map(record => record.file).filter(Boolean));
        files.filter(isSource).forEach(file => merged.set(file.name.toLowerCase(), file));
        return [...merged.values()];
    }

    function forPlaylist(files, playlist) {
        const stems = playlist.map(item => fileStem(item.name));
        if (!stems.length) return [];
        return files.filter(file => {
            const name = file.name.toLowerCase();
            if (name === 'definitions.csv' || name.startsWith('bundledefinition') && name.endsWith('.txt')) return true;
            const stem = fileStem(name);
            return stems.some(videoStem => stem === videoStem || stem.startsWith(`${videoStem}.`));
        });
    }

    async function download({ uploadsOnly = false } = {}) {
        const paths = await (await request('/Edi/Assets')).json();
        const selected = paths.filter(path => {
            if (typeof path !== 'string') return false;
            const name = decodeURIComponent(path.split('/').pop() || '');
            return name.toLowerCase() !== 'definitions_auto.csv'
                && (uploadsOnly
                    ? path.toLowerCase().startsWith('/edi/upload/') && isEdiAsset({ name })
                    : /(?:\.funscript|\.mp3|\.csv|\.txt)$/i.test(path));
        });
        return Promise.all(selected.map(async path => {
            const response = await request(path);
            return new File([await response.blob()], decodeURIComponent(path.split('/').pop() || 'asset'), {
                type: response.headers.get('content-type') || ''
            });
        }));
    }

    async function recoverUploaded() {
        // Reconcile with the server, which may have restarted or been changed by another client.
        const files = await download({ uploadsOnly: true });
        uploaded = byName(files);
        return files;
    }

    async function fetchForVariants() {
        if (cached) return [...cached];
        if (!fetching) {
            const revision = cacheRevision;
            fetching = download().then(files => {
                if (revision === cacheRevision) remember(files);
                return cached;
            }).finally(() => { fetching = null; });
        }
        return [...await fetching];
    }

    async function upload(files) {
        const form = new FormData();
        files.forEach(file => form.append('files', file, file.name));
        const response = await request('/Edi/Assets', { method: 'POST', body: form });
        remember(files, { onServer: true });
        return response.json();
    }

    async function uploadGenerated(generated, merged) {
        const form = new FormData();
        generated.forEach(file => form.append('files', file, file.name));
        await request('/Edi/Assets', { method: 'PUT', body: form });
        generated.forEach(file => uploaded.set(file.name.toLowerCase(), file));
        remember(merged);
        // Keep the existing asynchronous persistence notification contract.
        notifyPersistence([...merged]);
    }

    async function clear() {
        await persistQueue;
        await repository.saveAssets([]);
        await request('/Edi/Assets', { method: 'DELETE' });
        remember([], { onServer: true });
    }

    function bindPersistence(report) {
        const listener = event => {
            const files = Array.isArray(event.detail?.files) ? event.detail.files : [];
            if (!files.length) return;
            remember(files);
            void save(files).catch(error => report(`Could not persist the asset cache: ${error.message}`, true));
        };
        document.addEventListener('edi-assets-persist-requested', listener);
        return () => document.removeEventListener('edi-assets-persist-requested', listener);
    }

    return { restore, save, merge, forPlaylist, recoverUploaded, fetchForVariants, upload, uploadGenerated,
        clear, bindPersistence, clearStoredVideos: repository.clearStoredVideos,
        getUploaded: () => [...uploaded.values()] };
}

export const assets = createAssetManager();
