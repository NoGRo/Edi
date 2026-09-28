import { api } from './edi-api.mjs';
import * as storage from './asset-storage.mjs';
import { fileStem, isEdiAsset } from './media-files.mjs';
import { autoVariantName, generatedFunscriptName, getDoubleSpeedScript, getHalfSpeedScript,
    parseAutoVariant, parseFunscriptName } from '../funscript-tools.mjs';

const isSource = file => file.name.toLowerCase() !== 'definitions_auto.csv';
const byName = files => new Map(files.filter(isSource).map(file => [file.name.toLowerCase(), file]));
const scriptName = file => parseFunscriptName(file.name.replace(/\.mp3$/i, '.funscript'));
const videoScripts = (files, name) => files.filter(file => /\.(funscript|mp3)$/i.test(file.name)
    && (!name || scriptName(file).name.toLowerCase() === fileStem(name)));

export const assetVariantsForVideo = (files, name) => [...new Set(videoScripts(files, name)
    .map(file => scriptName(file).variant))];
export const generationBasesForVideo = (files, name) => [...new Set(videoScripts(files, name)
    .filter(file => /\.funscript$/i.test(file.name)).map(file => scriptName(file).variant)
    .filter(variant => variant !== 'None' && !parseAutoVariant(variant)))];

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
    let partialDownload = false;
    const downloadedVideos = new Set();
    let persistQueue = Promise.resolve();
    let cacheRevision = 0;
    let serverNames = null, serverListing = null, preparationQueue = Promise.resolve();
    const scriptData = new WeakMap(), prepared = new Map();
    const readScript = file => {
        if (!scriptData.has(file)) scriptData.set(file, file.text().then(async text => {
            const bytes = await crypto.subtle.digest('SHA-256', new TextEncoder().encode(text));
            return { script: JSON.parse(text), fingerprint: Array.from(new Uint8Array(bytes),
                byte => byte.toString(16).padStart(2, '0')).join('') };
        }));
        return scriptData.get(file);
    };

    function remember(files, { onServer = false } = {}) {
        cacheRevision++;
        cached = [...files];
        if (onServer) {
            partialDownload = false;
            downloadedVideos.clear();
            uploaded = byName(files);
            serverNames = new Set(uploaded.keys());
        }
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

    async function download({ uploadsOnly = false, videoName } = {}) {
        const revision = cacheRevision;
        const paths = await (await request('/Edi/Assets')).json();
        if (revision === cacheRevision) serverNames = new Set(paths.filter(path => typeof path === 'string')
            .map(path => decodeURIComponent(path.split('/').pop() || '').toLowerCase()));
        const selected = paths.filter(path => {
            if (typeof path !== 'string') return false;
            const name = decodeURIComponent(path.split('/').pop() || '');
            if (videoName && !videoScripts([{ name }], videoName).length) return false;
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

    async function fetchForVariants(videoName) {
        const stem = videoName && fileStem(videoName);
        if (cached && (!partialDownload || !stem || downloadedVideos.has(stem))) return [...cached];
        if (fetching) {
            await fetching;
            return fetchForVariants(videoName);
        }
        if (!fetching) {
            const revision = cacheRevision;
            fetching = download({ videoName, uploadsOnly: !videoName }).then(files => {
                if (revision === cacheRevision) {
                    remember([...byName([...(cached || []), ...files]).values()]);
                    partialDownload = Boolean(stem);
                    if (stem) downloadedVideos.add(stem);
                }
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
        if (!generated.length) return;
        const form = new FormData();
        generated.forEach(file => form.append('files', file, file.name));
        await request('/Edi/Assets', { method: 'PUT', body: form });
        generated.forEach(file => uploaded.set(file.name.toLowerCase(), file));
        generated.forEach(file => serverNames?.add(file.name.toLowerCase()));
        remember(merged);
        // Keep the existing asynchronous persistence notification contract.
        notifyPersistence([...merged]);
    }

    async function ensureServerNames() {
        if (serverNames) return;
        if (!serverListing) {
            const revision = cacheRevision;
            serverListing = request('/Edi/Assets').then(response => response.json()).then(paths => {
                if (revision === cacheRevision || !serverNames) serverNames = new Set(paths
                    .filter(path => typeof path === 'string')
                    .map(path => decodeURIComponent(path.split('/').pop() || '').toLowerCase()));
            }).finally(() => { serverListing = null; });
        }
        await serverListing;
    }

    function prepareVariant({ videoName, selection, baseVariant } = {}) {
        const preparing = preparationQueue.then(async () => {
            if (selection === 'real:None') return { variant: 'None', changed: false };
            const kind = selection?.startsWith('auto:') ? selection.slice(5) : null;
            if (kind && (!['double', 'halve'].includes(kind) || parseAutoVariant(baseVariant) || baseVariant === 'None'))
                throw new Error('Automatic variants require an original base variant.');
            const key = JSON.stringify([videoName?.toLowerCase(), selection, baseVariant?.toLowerCase()]);
            if (prepared.get(key)?.revision === cacheRevision) return { variant: prepared.get(key).variant, changed: false };
            const files = await fetchForVariants(videoName);
            await ensureServerNames();
            const relevant = videoScripts(files, videoName);
            let generated = [];
            let variant = selection?.startsWith('real:') ? selection.slice(5) : null;
            if (kind) {
                variant = autoVariantName(kind, baseVariant);
                const sources = relevant.filter(file => /\.funscript$/i.test(file.name)
                    && scriptName(file).variant.toLowerCase() === baseVariant?.toLowerCase());
                if (!sources.length) throw new Error(`No funscript assets were found for ${baseVariant}.`);
                for (const source of sources) {
                    const name = generatedFunscriptName(source.name, variant);
                    const existing = files.find(file => file.name.toLowerCase() === name.toLowerCase());
                    const { script: sourceScript, fingerprint } = await readScript(source);
                    const previous = existing ? (await readScript(existing)).script : null;
                    const metadata = previous?.metadata?.ediAutoVariant;
                    // Compare source actions as well as its name: replacing an asset invalidates its derivative.
                    if (metadata?.baseVariant?.toLowerCase() === baseVariant.toLowerCase()
                        && metadata.kind === kind && metadata.sourceFingerprint === fingerprint) continue;
                    if (!Array.isArray(sourceScript.actions)) throw new Error(`${source.name} has no actions array.`);
                    const output = (kind === 'double' ? getDoubleSpeedScript : getHalfSpeedScript)(sourceScript, {});
                    output.metadata = { ...output.metadata,
                        ediAutoVariant: { kind, baseVariant, sourceFingerprint: fingerprint } };
                    generated.push(new File([JSON.stringify(output)], name, { type: 'application/json' }));
                }
            }
            const merged = byName(files);
            generated.forEach(file => merged.set(file.name.toLowerCase(), file));
            const missing = videoScripts([...merged.values()], videoName).filter(file =>
                (selection || !parseAutoVariant(scriptName(file).variant))
                && !serverNames.has(file.name.toLowerCase()) && !generated.some(output => output.name === file.name));
            const definitions = files.filter(file => /^(definitions\.csv|bundledefinition.*\.txt)$/i.test(file.name)
                && !serverNames.has(file.name.toLowerCase()));
            const additions = [...missing, ...definitions, ...generated];
            if (additions.length) await uploadGenerated(additions, [...merged.values()]);
            prepared.set(key, { revision: cacheRevision, variant });
            return { variant, changed: additions.length > 0 };
        });
        preparationQueue = preparing.catch(() => {});
        return preparing;
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

    return { restore, save, merge, forPlaylist, recoverUploaded, fetchForVariants, upload, uploadGenerated, prepareVariant,
        async generationBases(videoName) {
            return generationBasesForVideo(await fetchForVariants(videoName), videoName);
        },
        clear, bindPersistence, clearStoredVideos: repository.clearStoredVideos,
        getUploaded: () => [...uploaded.values()] };
}

export const assets = createAssetManager();
