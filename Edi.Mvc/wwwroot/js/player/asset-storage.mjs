const databaseName = 'edi-player';
const playlistStore = 'playlist';
const assetStore = 'assets';
function openDatabase() {
    return new Promise((resolve, reject) => {
        const request = indexedDB.open(databaseName, 2);
        request.onupgradeneeded = () => {
            if (!request.result.objectStoreNames.contains(playlistStore)) {
                request.result.createObjectStore(playlistStore, { keyPath: 'id' });
            }
            if (!request.result.objectStoreNames.contains(assetStore)) {
                request.result.createObjectStore(assetStore, { keyPath: 'name' });
            }
        };
        request.onsuccess = () => resolve(request.result);
        request.onerror = () => reject(request.error);
    });
}

async function withStore(storeName, mode, operation) {
    const database = await openDatabase();
    return new Promise((resolve, reject) => {
        const transaction = database.transaction(storeName, mode);
        const store = transaction.objectStore(storeName);
        operation(store);
        transaction.oncomplete = () => {
            database.close();
            resolve();
        };
        transaction.onerror = () => {
            database.close();
            reject(transaction.error);
        };
        transaction.onabort = () => {
            database.close();
            reject(transaction.error || new Error('Asset transaction aborted.'));
        };
    });
}

export function clearStoredVideos() {
    return withStore(playlistStore, 'readwrite', store => {
        store.clear();
    });
}

export async function getStoredItems(storeName) {
    const database = await openDatabase();
    return new Promise((resolve, reject) => {
        const transaction = database.transaction(storeName, 'readonly');
        const request = transaction.objectStore(storeName).getAll();
        request.onsuccess = () => resolve(request.result);
        request.onerror = () => reject(request.error);
        transaction.oncomplete = () => database.close();
    });
}

export function saveAssets(files) {
    return withStore(assetStore, 'readwrite', store => {
        store.clear();
        files
            .filter(file => file.name.toLowerCase() !== 'definitions_auto.csv')
            .forEach(file => store.put({ name: file.name, file }));
    });
}

