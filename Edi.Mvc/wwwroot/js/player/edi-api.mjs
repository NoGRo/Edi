export async function api(path, options = {}) {
    const response = await fetch(path, options);
    if (!response.ok) {
        const detail = await response.text();
        throw new Error(detail || `${response.status} ${response.statusText}`);
    }
    return response;
}

export async function confirmedPlaybackCommand(path) {
    const controller = new AbortController();
    const timeout = window.setTimeout(() => controller.abort(), 5000);
    try {
        return await api(path, { method: 'POST', signal: controller.signal });
    } catch (error) {
        if (controller.signal.aborted) {
            throw new Error('The EDI server did not confirm the command within 5 seconds.');
        }
        throw error;
    } finally {
        window.clearTimeout(timeout);
    }
}

export function report(message, isError = false) {
    if (isError) console.error(message);
}

