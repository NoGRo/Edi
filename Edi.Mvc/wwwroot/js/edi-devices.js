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

        const values = [device.min, device.max]
            .map(value => Math.min(100, Math.max(0, Number(value))))
            .sort((left, right) => left - right);
        let initialLow = Number.isFinite(values[0]) ? values[0] : 0;
        let initialHigh = Number.isFinite(values[1]) ? values[1] : 100;
        if (initialLow === initialHigh) {
            if (initialHigh < 100) initialHigh += 1;
            else initialLow -= 1;
        }

        const min = document.createElement('input');
        min.className = 'device-range-input';
        min.type = 'range'; min.min = 0; min.max = 100; min.step = 1; min.value = initialLow;
        min.setAttribute('aria-label', `Límite inferior de ${device.name}`);
        const max = document.createElement('input');
        max.className = 'device-range-input';
        max.type = 'range'; max.min = 0; max.max = 100; max.step = 1; max.value = initialHigh;
        max.setAttribute('aria-label', `Límite superior de ${device.name}`);
        const minLabel = document.createElement('span');
        const maxLabel = document.createElement('span');
        const range = document.createElement('div');
        range.className = 'device-range';
        const track = document.createElement('div');
        track.className = 'device-range-track';
        const rangeValues = document.createElement('div');
        rangeValues.className = 'device-range-values';

        const renderRange = changedInput => {
            let low = Number(min.value);
            let high = Number(max.value);
            if (changedInput === min && low >= high) low = high - 1;
            if (changedInput === max && high <= low) high = low + 1;
            min.value = low;
            max.value = high;
            minLabel.textContent = `Mínimo: ${low}%`;
            maxLabel.textContent = `Máximo: ${high}%`;
            range.style.setProperty('--range-low', `${low}%`);
            range.style.setProperty('--range-high', `${high}%`);
            min.style.zIndex = changedInput === min ? 3 : 2;
            max.style.zIndex = changedInput === max ? 3 : 2;
        };

        const applyRange = async () => {
            const low = Number(min.value), high = Number(max.value);
            editing = true;
            try {
                await post(`/Devices/${encodeURIComponent(device.name)}/Range/${low}-${high}`);
            } finally { editing = false; }
        };
        min.addEventListener('input', () => renderRange(min));
        max.addEventListener('input', () => renderRange(max));
        min.addEventListener('change', applyRange);
        max.addEventListener('change', applyRange);

        rangeValues.append(minLabel, maxLabel);
        range.append(track, min, max);
        const rangeBox = document.createElement('div');
        rangeBox.append(rangeValues, range);
        const rangeField = field('Rango', rangeBox);
        rangeField.className = 'col-md-8';
        renderRange();
        row.append(field('Variante', variant), rangeField);
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
