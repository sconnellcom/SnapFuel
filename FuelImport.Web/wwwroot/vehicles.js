const statusEl = document.getElementById('status');
const vehicleManagerListEl = document.getElementById('vehicleManagerList');
const newVehicleNameEl = document.getElementById('newVehicleName');
const addVehicleButton = document.getElementById('addVehicleButton');

const state = {
    vehicles: []
};

function renderStatus(message) {
    statusEl.textContent = message;
}

function escapeHtml(value) {
    return String(value)
        .replaceAll('&', '&amp;')
        .replaceAll('<', '&lt;')
        .replaceAll('>', '&gt;')
        .replaceAll('"', '&quot;');
}

async function loadVehicles() {
    renderStatus('Loading vehicles…');

    try {
        const response = await fetch('/api/vehicles/manage');
        if (!response.ok) {
            throw new Error(`HTTP ${response.status}`);
        }

        state.vehicles = await response.json();
        renderVehicles();
        renderStatus(`Loaded ${state.vehicles.length} vehicles.`);
    } catch (error) {
        console.error(error);
        renderStatus('Unable to load vehicles.');
    }
}

function renderVehicles() {
    if (!state.vehicles.length) {
        vehicleManagerListEl.innerHTML = '<div class="helper">No vehicles configured yet.</div>';
        return;
    }

    vehicleManagerListEl.innerHTML = state.vehicles.map((vehicle) => `
    <div class="vehicle-row" data-vehicle-id="${vehicle.vehicleId}">
      <div>
        <input type="text" value="${escapeHtml(vehicle.name)}" data-role="name" />
                <label class="checkbox-row">
                    <input type="checkbox" data-role="no-odometer" ${vehicle.noOdometer ? 'checked' : ''} />
                    No Odometer
                </label>
        <div><span class="badge ${vehicle.active ? 'badge-active' : 'badge-inactive'}">${vehicle.active ? 'Active' : 'Inactive'}</span></div>
      </div>
      <button type="button" data-action="save">Save</button>
      <button type="button" data-action="toggle">${vehicle.active ? 'Archive' : 'Activate'}</button>
    </div>
  `).join('');
}

async function createVehicle() {
    const name = newVehicleNameEl.value.trim();
    const noOdometerToggle = document.getElementById('newVehicleNoOdometer');
    const noOdometer = noOdometerToggle instanceof HTMLInputElement ? noOdometerToggle.checked : false;
    if (!name) {
        renderStatus('Enter a vehicle name first.');
        return;
    }

    renderStatus('Adding vehicle…');

    try {
        const response = await fetch('/api/vehicles', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ name, noOdometer })
        });

        if (!response.ok) {
            const payload = await response.json().catch(() => null);
            throw new Error(payload?.message || `HTTP ${response.status}`);
        }

        newVehicleNameEl.value = '';
        if (noOdometerToggle instanceof HTMLInputElement) {
            noOdometerToggle.checked = false;
        }
        await loadVehicles();
        renderStatus('Vehicle added.');
    } catch (error) {
        console.error(error);
        renderStatus(error.message || 'Unable to add vehicle.');
    }
}

async function updateVehicle(vehicleId, name, active, noOdometer) {
    renderStatus('Saving vehicle…');

    try {
        const response = await fetch(`/api/vehicles/${vehicleId}`, {
            method: 'PUT',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ name, active, noOdometer })
        });

        if (!response.ok) {
            const payload = await response.json().catch(() => null);
            throw new Error(payload?.message || `HTTP ${response.status}`);
        }

        await loadVehicles();
        renderStatus('Vehicle saved.');
    } catch (error) {
        console.error(error);
        renderStatus(error.message || 'Unable to save vehicle.');
    }
}

addVehicleButton.addEventListener('click', createVehicle);
newVehicleNameEl.addEventListener('keydown', (event) => {
    if (event.key === 'Enter') {
        event.preventDefault();
        createVehicle();
    }
});

vehicleManagerListEl.addEventListener('click', (event) => {
    const target = event.target;
    if (!(target instanceof HTMLElement)) {
        return;
    }

    const row = target.closest('[data-vehicle-id]');
    if (!row) {
        return;
    }

    const vehicleId = Number(row.dataset.vehicleId);
    const vehicle = state.vehicles.find((item) => item.vehicleId === vehicleId);
    const input = row.querySelector('[data-role="name"]');
    const noOdometerInput = row.querySelector('[data-role="no-odometer"]');
    const name = input instanceof HTMLInputElement ? input.value.trim() : '';
    const noOdometer = noOdometerInput instanceof HTMLInputElement ? noOdometerInput.checked : false;
    if (!vehicle) {
        return;
    }

    if (target.dataset.action === 'save') {
        updateVehicle(vehicleId, name, vehicle.active, noOdometer);
        return;
    }

    if (target.dataset.action === 'toggle') {
        updateVehicle(vehicleId, name || vehicle.name, !vehicle.active, noOdometer);
    }
});

loadVehicles();