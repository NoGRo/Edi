document.addEventListener('DOMContentLoaded', () => {
    const grid = document.getElementById('devicesGrid');
    let editing = false;

    const post = async path => {
        const response = await fetch(path, { method: 'POST' });
        if (!response.ok) throw new Error(await response.text() || response.statusText);
    };

    function field(labelText, control) {
        const wrapper = document.createElement('div');
        wrapper.className = 'col-md-4';
        const label = document.createElement('label');
        label.className = 'form-label';
        label.textContent = labelText;
        wrapper.append(label, control);
        return wrapper;
    }

    function renderDevice(device) {
        const card = document.createElement('div');
        card.className = 'device-card';
        const heading = document.createElement('div');
        heading.className = 'd-flex justify-content-between align-items-center mb-3';
        const name = document.createElement('strong');
        name.textContent = device.name;
        const badge = document.createElement('span');
        badge.className = `badge ${device.isReady ? 'bg-success' : 'bg-secondary'}`;
        badge.textContent = device.isReady ? 'Conectado' : 'No disponible';
        heading.append(name, badge);

        const row = document.createElement('div');
        row.className = 'row g-3';
        const variant = document.createElement('select');
        variant.className = 'form-select';
        for (const value of device.variants || []) {
            variant.append(new Option(value, value, false, value === device.selectedVariant));
        }
        variant.disabled = !variant.options.length;
        variant.addEventListener('change', async () => {
            editing = true;
            try {
                await post(`/Devices/${encodeURIComponent(device.name)}/Variant/${encodeURIComponent(variant.value)}`);
            } finally { editing = false; }
        });

        const min = document.createElement('input');
        min.className = 'form-range';
        min.type = 'range'; min.min = 0; min.max = 100; min.value = device.min;
        const max = document.createElement('input');
        max.className = 'form-range';
        max.type = 'range'; max.min = 0; max.max = 100; max.value = device.max;
        const minLabel = document.createElement('span');
        const maxLabel = document.createElement('span');
        minLabel.textContent = `Mínimo: ${min.value}%`;
        maxLabel.textContent = `Máximo: ${max.value}%`;

        const applyRange = async () => {
            let low = Number(min.value), high = Number(max.value);
            if (low > high) [low, high] = [high, low];
            min.value = low; max.value = high;
            minLabel.textContent = `Mínimo: ${low}%`;
            maxLabel.textContent = `Máximo: ${high}%`;
            editing = true;
            try {
                await post(`/Devices/${encodeURIComponent(device.name)}/Range/${low}-${high}`);
            } finally { editing = false; }
        };
        min.addEventListener('input', () => minLabel.textContent = `Mínimo: ${min.value}%`);
        max.addEventListener('input', () => maxLabel.textContent = `Máximo: ${max.value}%`);
        min.addEventListener('change', applyRange);
        max.addEventListener('change', applyRange);

        const minBox = document.createElement('div');
        minBox.append(minLabel, min);
        const maxBox = document.createElement('div');
        maxBox.append(maxLabel, max);
        row.append(field('Variante', variant), field('Límite inferior', minBox), field('Límite superior', maxBox));
        card.append(heading, row);
        return card;
    }

    async function loadDevices() {
        if (editing) return;
        try {
            const response = await fetch('/Devices');
            if (!response.ok) throw new Error(await response.text() || response.statusText);
            const devices = await response.json();
            grid.replaceChildren();
            if (!devices.length) {
                const empty = document.createElement('div');
                empty.className = 'alert alert-secondary';
                empty.textContent = 'No hay dispositivos detectados.';
                grid.append(empty);
                return;
            }
            devices.forEach(device => grid.append(renderDevice(device)));
        } catch (error) {
            const message = document.createElement('div');
            message.className = 'alert alert-danger';
            message.textContent = `No se pudieron cargar los dispositivos: ${error.message}`;
            grid.replaceChildren(message);
        }
    }

    loadDevices();
    setInterval(loadDevices, 10000);
});
