const statusEl = document.getElementById('status');
const logBodyEl = document.getElementById('logBody');
const refreshButton = document.getElementById('refreshButton');
const anomalyFilterEl = document.getElementById('anomalyFilter');

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
    if (!(anomalyFilterEl instanceof HTMLSelectElement)) {
        return state.events;
    }

    if (anomalyFilterEl.value === 'onlyAnomalies') {
        return state.events.filter((item) => (item.anomalyFlags?.length ?? 0) > 0);
    }

    return state.events;
}

function renderLog() {
    const rows = filteredEvents();
    if (!rows.length) {
        logBodyEl.innerHTML = '<tr><td colspan="9" class="empty">No matching events.</td></tr>';
        return;
    }

    logBodyEl.innerHTML = rows.map((item) => {
        const callouts = (item.anomalyFlags ?? []).length
            ? item.anomalyFlags.map((flag) => `<span class="warn">${escapeHtml(flag)}</span>`).join('')
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
            <td><span class="pill pill-review">${escapeHtml(item.reviewStatus)}</span></td>
            <td>${callouts}</td>
        </tr>
        `;
    }).join('');
}

async function loadLog() {
    renderStatus('Loading event log…');

    try {
        const response = await fetch('/api/events/log');
        if (!response.ok) {
            throw new Error(`HTTP ${response.status}`);
        }

        state.events = await response.json();
        renderLog();
        renderStatus(`Loaded ${state.events.length} events.`);
    } catch (error) {
        console.error(error);
        state.events = [];
        renderLog();
        renderStatus('Unable to load event log.');
    }
}

refreshButton.addEventListener('click', loadLog);
anomalyFilterEl.addEventListener('change', renderLog);

loadLog();
