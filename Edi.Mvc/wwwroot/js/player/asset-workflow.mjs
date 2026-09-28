import { api, report } from './edi-api.mjs';
import { deviceRouting } from './device-routing.mjs';

export function createAssetWorkflow({ state, assetManager, stopEdi, renderPlaylist, renderPlaybackOptions, resyncAfterAssetsReload }) {
    const updateVariants = async () => {
        const item = state.playlist.find(item => item.id === state.currentId);
        await deviceRouting.setVideoContext(item ? { name: item.name, definitions: state.definitions } : null);
    };
    async function uploadAssets(files) {
        const preserveStrokerPause = state.strokerPaused;
        await stopEdi('Updating EDI assets...');
        state.definitions = await assetManager.upload(files);
        renderPlaylist();
        await updateVariants();
        if (!deviceRouting.getVideoContext()) document.dispatchEvent(new CustomEvent('edi-devices-refresh-requested'));
        if (preserveStrokerPause) {
            state.strokerPaused = true;
            state.strokerNeedsResync = true;
            renderPlaybackOptions();
        }
        await resyncAfterAssetsReload();
    }

    async function reloadAssets() {
        try {
            report('Reloading EDI assets...');
            const assets = assetManager.forPlaylist(await assetManager.merge(await assetManager.recoverUploaded()), state.playlist);
            if (!assets.length) {
                await loadDefinitions();
                report('No saved assets match the current playlist.', true);
                return;
            }
            await uploadAssets(assets);
            report(`${assets.length} EDI asset${assets.length === 1 ? '' : 's'} reloaded.`);
        } catch (error) {
            report(`Could not reload assets: ${error.message}`, true);
        }
    }

    async function loadDefinitions() {
        try {
            report('Loading EDI assets...');
            state.definitions = await (await api('/Edi/Definitions')).json();
            renderPlaylist();
            await updateVariants();
            report(state.playlist.length ? 'EDI assets updated.' : 'Add videos and assets to get started.');
        } catch (error) {
            report(`Could not load EDI: ${error.message}`, true);
        }
    }

    function mount() {
        document.addEventListener('edi-assets-reloaded', () => {
            void (async () => {
                try {
                    state.definitions = await (await api('/Edi/Definitions')).json();
                    renderPlaylist();
                    await updateVariants();
                    await resyncAfterAssetsReload();
                } catch (error) {
                    report(`Could not resynchronize EDI after reloading assets: ${error.message}`, true);
                }
            })();
        });
    }
    return { uploadAssets, reloadAssets, loadDefinitions, mount };
}

