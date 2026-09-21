const statusEl = document.getElementById('status');
const logBodyEl = document.getElementById('logBody');
const refreshButton = document.getElementById('refreshButton');
const anomalyFilterEl = document.getElementById('anomalyFilter');
const vehicleFilterEl = document.getElementById('vehicleFilter');

const state = {
    events: []
};

function renderStatus(message) {
    statusEl.textContent = message;
}

function formatDate(valueUtc, valueLocal) {
    const value = valueLocal || valueUtc;
    if (!value) {
        return '-';
    }

    return new Date(value).toLocaleString();
}

function formatNumber(value, digits = 2) {
    if (value == null) {
        return '-';
    }

    return Number(value).toFixed(digits);
}

function formatCurrency(value) {
    if (value == null) {
        return '-';
    }

    return `$${Number(value).toFixed(2)}`;
}

function escapeHtml(value) {
    return String(value)
        .replaceAll('&', '&amp;')
        .replaceAll('<', '&lt;')
        .replaceAll('>', '&gt;')
        .replaceAll('"', '&quot;');
}

function filteredEvents() {
    let list = state.events;

    if (vehicleFilterEl instanceof HTMLSelectElement && vehicleFilterEl.value) {
        const vehicleFilterValue = vehicleFilterEl.value;
        if (vehicleFilterValue === 'unassigned') {
            list = list.filter((item) => item.vehicleId == null);
        } else {
            const vid = Number(vehicleFilterValue);
            list = list.filter((item) => item.vehicleId === vid);
        }
    }

    if (anomalyFilterEl instanceof HTMLSelectElement) {
        if (anomalyFilterEl.value === 'onlyAnomalies') {
            list = list.filter((item) => (item.anomalyFlags?.length ?? 0) > 0 && !item.anomalyAcknowledged);
        } else if (anomalyFilterEl.value === 'onlyWithoutImages') {
            list = list.filter((item) => !item.hasImages);
        }
    }

    return list;
}

function renderVehicleFilterOptions() {
    if (!(vehicleFilterEl instanceof HTMLSelectElement)) {
        return;
    }

    const previousValue = vehicleFilterEl.value;
    const vehicleMap = new Map();
    let hasUnassigned = false;

    (state.events || []).forEach((event) => {
        if (event.vehicleId != null) {
            const name = event.vehicleName && event.vehicleName !== 'Unassigned'
                ? event.vehicleName
                : `Vehicle #${event.vehicleId}`;
            if (!vehicleMap.has(event.vehicleId)) {
                vehicleMap.set(event.vehicleId, name);
            }
        } else {
            hasUnassigned = true;
        }
    });

    const options = ['<option value="">All Vehicles</option>'];

    const sortedVehicles = Array.from(vehicleMap.entries()).sort((a, b) => a[1].localeCompare(b[1]));
    sortedVehicles.forEach(([id, name]) => {
        options.push(`<option value="${id}">${escapeHtml(name)}</option>`);
    });

    if (hasUnassigned) {
        options.push('<option value="unassigned">Unassigned</option>');
    }

    vehicleFilterEl.innerHTML = options.join('');
    if (previousValue && Array.from(vehicleFilterEl.options).some((option) => option.value === previousValue)) {
        vehicleFilterEl.value = previousValue;
    }
}

/// Applies vehicleId/onlyAnomalies query params (once) so report page callout links land pre-filtered.
let urlFiltersApplied = false;
function applyFiltersFromUrl() {
    if (urlFiltersApplied) {
        return;
    }

    urlFiltersApplied = true;
    const params = new URLSearchParams(window.location.search);
    const vehicleId = params.get('vehicleId');
    if (vehicleId && vehicleFilterEl instanceof HTMLSelectElement) {
        vehicleFilterEl.value = vehicleId;
    }

    if (params.get('onlyAnomalies') === 'true' && anomalyFilterEl instanceof HTMLSelectElement) {
        anomalyFilterEl.value = 'onlyAnomalies';
    }
}

function formatReviewStatus(value) {
    return value === 'AutoDetected' ? 'Auto detected' : (value ?? 'Pending');
}

function renderLog() {
    const rows = filteredEvents();
    if (!rows.length) {
        logBodyEl.innerHTML = '<tr><td colspan="10" class="empty">No matching events.</td></tr>';
        return;
    }

    logBodyEl.innerHTML = rows.map((item) => {
        const hasCallouts = (item.anomalyFlags ?? []).length > 0;
        const callouts = hasCallouts
            ? item.anomalyFlags.map((flag) => `<span class="warn">${escapeHtml(flag)}</span>`).join('') + (item.anomalyAcknowledged ? '<span class="pill pill-approved">Approved</span>' : '')
            : 'None';

        return `
        <tr>
            <td>${formatDate(item.eventTimeUtc, item.eventTimeLocal)}</td>
            <td>${escapeHtml(item.vehicleName || 'Unassigned')}</td>
            <td>${formatNumber(item.gallons)}</td>
            <td>${item.odometer ?? '-'}</td>
            <td>${formatNumber(item.milesSincePrevious)}</td>
            <td>${formatNumber(item.calculatedMpg)}</td>
            <td>${formatCurrency(item.totalPrice)}${item.pricePerGallon != null ? ` ($${Number(item.pricePerGallon).toFixed(3)}/gal)` : ''}</td>
            <td><span class="pill pill-review">${escapeHtml(formatReviewStatus(item.reviewStatus))}</span></td>
            <td>${callouts}</td>
            <td>
                <div class="row-actions">
                    <a class="secondary" href="/?fuelEventId=${item.fuelEventId}">Edit</a>
                    ${!item.hasImages ? `<button type="button" class="secondary" data-action="delete-orphan" data-fuel-event-id="${item.fuelEventId}">Delete</button>` : ''}
                    ${hasCallouts ? `<button type="button" class="secondary" data-action="toggle-ack" data-fuel-event-id="${item.fuelEventId}" data-acknowledged="${item.anomalyAcknowledged ? 'true' : 'false'}">${item.anomalyAcknowledged ? 'Unapprove' : 'Approve'}</button>` : ''}
                </div>
            </td>
        </tr>
        `;
    }).join('');

    logBodyEl.querySelectorAll('[data-action="toggle-ack"]').forEach((button) => {
        button.addEventListener('click', () => {
            const fuelEventId = Number(button.dataset.fuelEventId);
            const acknowledged = button.dataset.acknowledged !== 'true';
            toggleAnomalyAcknowledged(fuelEventId, acknowledged);
        });
    });

    logBodyEl.querySelectorAll('[data-action="delete-orphan"]').forEach((button) => {
        button.addEventListener('click', () => deleteImageLessEvent(Number(button.dataset.fuelEventId)));
    });
}

async function deleteImageLessEvent(fuelEventId) {
    if (!window.confirm('Delete this event? It has no linked images and cannot be restored.')) {
        return;
    }

    renderStatus('Deleting event…');
    try {
        const response = await fetch(`/api/events/${fuelEventId}`, { method: 'DELETE' });
        if (!response.ok) {
            throw new Error(`HTTP ${response.status}`);
        }

        state.events = state.events.filter((event) => event.fuelEventId !== fuelEventId);
        renderVehicleFilterOptions();
        renderLog();
        renderStatus('Image-less event deleted.');
    } catch (error) {
        console.error(error);
        renderStatus('Unable to delete event.');
    }
}

async function toggleAnomalyAcknowledged(fuelEventId, acknowledged) {
    renderStatus(acknowledged ? 'Approving callout…' : 'Reopening callout…');
    try {
        const response = await fetch(`/api/events/${fuelEventId}/anomaly-ack`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ acknowledged })
        });

        if (!response.ok) {
            throw new Error(`HTTP ${response.status}`);
        }

        const item = state.events.find((event) => event.fuelEventId === fuelEventId);
        if (item) {
            item.anomalyAcknowledged = acknowledged;
        }

        renderLog();
        renderStatus(acknowledged ? 'Callout approved.' : 'Callout reopened.');
    } catch (error) {
        console.error(error);
        renderStatus(error.message || 'Unable to update the callout.');
    }
}

async function loadLog() {
    renderStatus('Loading event log…');

    try {
        const response = await fetch('/api/events/log');
        if (!response.ok) {
            throw new Error(`HTTP ${response.status}`);
        }

        state.events = await response.json();
        renderVehicleFilterOptions();
        applyFiltersFromUrl();
        renderLog();
        renderStatus(`Loaded ${state.events.length} events.`);
    } catch (error) {
        console.error(error);
        state.events = [];
        renderVehicleFilterOptions();
        renderLog();
        renderStatus('Unable to load event log.');
    }
}

refreshButton.addEventListener('click', loadLog);
anomalyFilterEl.addEventListener('change', renderLog);
if (vehicleFilterEl instanceof HTMLSelectElement) {
    vehicleFilterEl.addEventListener('change', renderLog);
}

loadLog();
