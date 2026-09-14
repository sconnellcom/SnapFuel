const statusEl = document.getElementById('status');
const totalEventsEl = document.getElementById('totalEvents');
const eventsWithAnomaliesEl = document.getElementById('eventsWithAnomalies');
const healthRatioEl = document.getElementById('healthRatio');
const vehicleReportBodyEl = document.getElementById('vehicleReportBody');
const vehicleTrendSelectEl = document.getElementById('vehicleTrendSelect');
const trendChartsEl = document.getElementById('trendCharts');
const refreshButton = document.getElementById('refreshButton');

const state = {
    report: null,
    eventLog: [],
    selectedVehicleId: null
};

const chartDefinitions = [
    { key: 'totalPrice', label: 'Total Price', color: '#9d3b2e', digits: 2, format: (value) => `$${Number(value).toFixed(2)}` },
    { key: 'pricePerGallon', label: 'Price / Gallon', color: '#1d5d62', digits: 3, format: (value) => `$${Number(value).toFixed(3)}` },
    { key: 'gallons', label: 'Gallons', color: '#7a5b35', digits: 2, format: (value) => Number(value).toFixed(2) },
    { key: 'milesSincePrevious', label: 'Miles Since Previous Fill', color: '#6f3f7d', digits: 2, format: (value) => Number(value).toFixed(2) },
    { key: 'calculatedMpg', label: 'MPG', color: '#257a4f', digits: 2, format: (value) => Number(value).toFixed(2) },
    { key: 'odometer', label: 'Odometer', color: '#495a9f', digits: 0, format: (value) => Number(value).toFixed(0) }
];

function renderStatus(message) {
    statusEl.textContent = message;
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

function formatDate(valueUtc, valueLocal) {
    const value = valueLocal || valueUtc;
    if (!value) {
        return 'Unknown time';
    }

    return new Date(value).toLocaleString();
}

function formatThresholds(vehicle) {
    const parts = [];
    if (vehicle.maxGallonsPerFillUp != null) {
        parts.push(`max gal ${Number(vehicle.maxGallonsPerFillUp).toFixed(2)}`);
    }

    if (vehicle.maxMpg != null) {
        parts.push(`max mpg ${Number(vehicle.maxMpg).toFixed(2)}`);
    }

    return parts.length ? parts.join(' | ') : 'not set';
}

function escapeHtml(value) {
    return String(value)
        .replaceAll('&', '&amp;')
        .replaceAll('<', '&lt;')
        .replaceAll('>', '&gt;')
        .replaceAll('"', '&quot;');
}

function renderReport(payload) {
    const totalEvents = payload.totalEvents ?? 0;
    const eventsWithAnomalies = payload.eventsWithAnomalies ?? 0;
    const cleanEvents = Math.max(totalEvents - eventsWithAnomalies, 0);

    totalEventsEl.textContent = String(totalEvents);
    eventsWithAnomaliesEl.textContent = String(eventsWithAnomalies);
    healthRatioEl.textContent = totalEvents === 0
        ? '-'
        : `${Math.round((cleanEvents / totalEvents) * 100)}% clean`;

    const rows = payload.vehicles ?? [];
    if (!rows.length) {
        vehicleReportBodyEl.innerHTML = '<tr><td colspan="8">No vehicles are configured yet.</td></tr>';
        return;
    }

    vehicleReportBodyEl.innerHTML = rows.map((vehicle) => {
        const callout = vehicle.eventsWithAnomalies > 0
            ? `<span class="warn">${vehicle.eventsWithAnomalies} callout${vehicle.eventsWithAnomalies === 1 ? '' : 's'}</span>`
            : 'None';

        return `
        <tr>
            <td>${escapeHtml(vehicle.vehicleName)}</td>
            <td>${escapeHtml(formatThresholds(vehicle))}</td>
            <td>${vehicle.totalEvents ?? 0}</td>
            <td>${callout}</td>
            <td>${formatNumber(vehicle.averageGallons)}</td>
            <td>${formatNumber(vehicle.averageMpg)}</td>
            <td>${formatNumber(vehicle.totalGallons)}</td>
            <td>${formatCurrency(vehicle.totalSpend)}</td>
        </tr>
        `;
    }).join('');
}

function parseEventTime(eventItem) {
    const value = eventItem.eventTimeLocal || eventItem.eventTimeUtc;
    if (!value) {
        return null;
    }

    const timestamp = Date.parse(value);
    return Number.isNaN(timestamp) ? null : timestamp;
}

function getVehicleRows() {
    return state.report?.vehicles ?? [];
}

function renderVehicleTrendOptions() {
    if (!(vehicleTrendSelectEl instanceof HTMLSelectElement)) {
        return;
    }

    const vehicles = getVehicleRows();
    if (!vehicles.length) {
        vehicleTrendSelectEl.innerHTML = '<option value="">No vehicles</option>';
        state.selectedVehicleId = null;
        return;
    }

    if (!vehicles.some((vehicle) => vehicle.vehicleId === state.selectedVehicleId)) {
        const vehicleWithEvents = vehicles.find((vehicle) => (vehicle.totalEvents ?? 0) > 0);
        state.selectedVehicleId = vehicleWithEvents?.vehicleId ?? vehicles[0].vehicleId;
    }

    vehicleTrendSelectEl.innerHTML = vehicles
        .map((vehicle) => `<option value="${vehicle.vehicleId}">${escapeHtml(vehicle.vehicleName)} (${vehicle.totalEvents ?? 0})</option>`)
        .join('');
    vehicleTrendSelectEl.value = String(state.selectedVehicleId);
}

function getSelectedVehicleEvents() {
    if (state.selectedVehicleId == null) {
        return [];
    }

    return state.eventLog
        .filter((eventItem) => eventItem.vehicleId === state.selectedVehicleId)
        .sort((left, right) => {
            const leftTime = parseEventTime(left);
            const rightTime = parseEventTime(right);
            if (leftTime != null && rightTime != null && leftTime !== rightTime) {
                return leftTime - rightTime;
            }

            if (leftTime != null && rightTime == null) {
                return -1;
            }

            if (leftTime == null && rightTime != null) {
                return 1;
            }

            return (left.fuelEventId ?? 0) - (right.fuelEventId ?? 0);
        });
}

function getMetricPoints(events, key) {
    return events
        .map((eventItem, index) => {
            const value = eventItem[key];
            if (value == null) {
                return null;
            }

            const numeric = Number(value);
            if (!Number.isFinite(numeric)) {
                return null;
            }

            return {
                index,
                value: numeric,
                timeLabel: formatDate(eventItem.eventTimeUtc, eventItem.eventTimeLocal),
                fuelEventId: eventItem.fuelEventId
            };
        })
        .filter((point) => point != null);
}

function createChartSvg(points, color, formatValue) {
    const width = 600;
    const height = 220;
    const padLeft = 42;
    const padRight = 12;
    const padTop = 14;
    const padBottom = 28;
    const plotWidth = width - padLeft - padRight;
    const plotHeight = height - padTop - padBottom;

    const min = Math.min(...points.map((point) => point.value));
    const max = Math.max(...points.map((point) => point.value));
    const range = max - min;
    const denominator = range === 0 ? 1 : range;

    const mapped = points.map((point, order) => {
        const x = points.length === 1
            ? padLeft + (plotWidth / 2)
            : padLeft + ((order / (points.length - 1)) * plotWidth);
        const y = padTop + ((max - point.value) / denominator) * plotHeight;
        return { ...point, x, y };
    });

    const path = mapped.map((point, index) => `${index === 0 ? 'M' : 'L'} ${point.x.toFixed(2)} ${point.y.toFixed(2)}`).join(' ');
    const midY = padTop + plotHeight / 2;

    return `
    <svg class="chart-svg" viewBox="0 0 ${width} ${height}" preserveAspectRatio="none">
        <line class="chart-axis" x1="${padLeft}" y1="${padTop}" x2="${padLeft}" y2="${height - padBottom}" />
        <line class="chart-axis" x1="${padLeft}" y1="${height - padBottom}" x2="${width - padRight}" y2="${height - padBottom}" />
        <line class="chart-grid-line" x1="${padLeft}" y1="${midY}" x2="${width - padRight}" y2="${midY}" />
        <text class="chart-label" x="${padLeft - 4}" y="${padTop + 10}" text-anchor="end">${escapeHtml(formatValue(max))}</text>
        <text class="chart-label" x="${padLeft - 4}" y="${height - padBottom + 2}" text-anchor="end">${escapeHtml(formatValue(min))}</text>
        <path class="chart-line" d="${path}" style="stroke:${color};" />
        ${mapped.map((point) => `
            <circle class="chart-point" cx="${point.x.toFixed(2)}" cy="${point.y.toFixed(2)}" r="3.5" style="fill:${color};">
                <title>${escapeHtml(point.timeLabel)} | ${escapeHtml(formatValue(point.value))} | event #${point.fuelEventId}</title>
            </circle>
        `).join('')}
    </svg>
    `;
}

function renderTrendCharts() {
    if (!(trendChartsEl instanceof HTMLElement)) {
        return;
    }

    const events = getSelectedVehicleEvents();
    if (!events.length) {
        trendChartsEl.innerHTML = '<div class="chart-empty">No events found for the selected vehicle.</div>';
        return;
    }

    trendChartsEl.innerHTML = chartDefinitions.map((definition) => {
        const points = getMetricPoints(events, definition.key);
        if (points.length < 2) {
            return `
            <article class="chart-card">
                <h3 class="chart-title">${escapeHtml(definition.label)}</h3>
                <div class="chart-empty">Need at least 2 data points to draw this chart.</div>
            </article>
            `;
        }

        const values = points.map((point) => point.value);
        const average = values.reduce((sum, value) => sum + value, 0) / values.length;
        const latest = values[values.length - 1];
        return `
        <article class="chart-card">
            <h3 class="chart-title">${escapeHtml(definition.label)}</h3>
            <div class="chart-meta">${points.length} points | avg ${escapeHtml(definition.format(average))} | latest ${escapeHtml(definition.format(latest))}</div>
            ${createChartSvg(points, definition.color, definition.format)}
        </article>
        `;
    }).join('');
}

async function loadReport() {
    renderStatus('Loading report…');

    try {
        const [reportResponse, logResponse] = await Promise.all([
            fetch('/api/events/report'),
            fetch('/api/events/log')
        ]);

        if (!reportResponse.ok || !logResponse.ok) {
            throw new Error(`HTTP ${reportResponse.status}/${logResponse.status}`);
        }

        state.report = await reportResponse.json();
        state.eventLog = await logResponse.json();

        renderReport(state.report);
        renderVehicleTrendOptions();
        renderTrendCharts();
        renderStatus('Report loaded.');
    } catch (error) {
        console.error(error);
        vehicleReportBodyEl.innerHTML = '<tr><td colspan="8">Unable to load report data.</td></tr>';
        if (trendChartsEl instanceof HTMLElement) {
            trendChartsEl.innerHTML = '<div class="chart-empty">Unable to load trend chart data.</div>';
        }
        renderStatus('Unable to load report.');
    }
}

refreshButton.addEventListener('click', loadReport);
if (vehicleTrendSelectEl instanceof HTMLSelectElement) {
    vehicleTrendSelectEl.addEventListener('change', () => {
        state.selectedVehicleId = Number(vehicleTrendSelectEl.value);
        renderTrendCharts();
    });
}

loadReport();
