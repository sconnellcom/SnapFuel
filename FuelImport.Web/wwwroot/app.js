const statusEl = document.getElementById('status');
const queueDisplayCountEl = document.getElementById('queueDisplayCount');
const groupListEl = document.getElementById('groupList');
const viewerShellEl = document.getElementById('viewerShell');
const entryForm = document.getElementById('entryForm');
const vehicleSelect = document.getElementById('vehicleId');
const groupMetaEl = document.getElementById('groupMeta');
const pricePerGallonDisplayEl = document.getElementById('pricePerGallonDisplay');
const detectionCalloutEl = document.getElementById('detectionCallout');
const saveButton = document.getElementById('saveButton');
const scanButton = document.getElementById('scanButton');
const autoDetectButton = document.getElementById('autoDetectButton');
const autoDetectGroupButton = document.getElementById('autoDetectGroupButton');
const refreshButton = document.getElementById('refreshButton');

let initialReviewerName = '';
try {
    initialReviewerName = localStorage.getItem('snapfuel_last_reviewer_name') || '';
} catch {
    initialReviewerName = '';
}

const MAGNIFICATION_LEVELS = [1.6, 2.5, 4.0];
const MAGNIFICATION_LABELS = ['1.6x', '2.5x', '4.0x'];
const THUMB_ZOOM_LEVELS = [1.35, 2.2, 1.0];
const LITERS_PER_GALLON = 3.785411784;

function roundTo(value, decimals) {
    const factor = 10 ** decimals;
    return Math.round(value * factor) / factor;
}

function gallonsToLiters(gallons) {
    return roundTo(gallons * LITERS_PER_GALLON, 3);
}

function litersToGallons(liters) {
    return roundTo(liters / LITERS_PER_GALLON, 3);
}

const state = {
    groups: [],
    vehicles: [],
    selectedGroupKey: null,
    activeImageId: null,
    drafts: {},
    splitSelections: {},
    thumbZoomByImageId: {},
    saveInFlightByGroup: {},
    pendingAutosaveByGroup: {},
    queueFilter: 'all',
    incompleteOnly: false,
    dateFilter: null,
    liveCalloutsByGroup: {},
    pendingSnapshotGroupKeys: new Set(),
    autoSnapshotGroupKeys: new Set(),
    autoDetectStatusByGroup: {},
    autoDetectQueue: [],
    autoDetectWorkerActive: false,
    magnificationIndex: 1,
    lastSavedReviewerName: initialReviewerName
};

function renderStatus(message) {
    statusEl.textContent = message;
}

function numberOrNull(value) {
    return value === '' ? null : Number(value);
}

function parseSafeMathExpression(value) {
    const raw = String(value ?? '').trim();
    if (!raw) {
        return null;
    }

    const normalized = raw.replace(/\s+/g, '');
    if (!/^[0-9.+-]+$/.test(normalized)) {
        return null;
    }

    if (!/[+-]/.test(normalized)) {
        return null;
    }

    const tokens = normalized.split(/([+-])/).filter((token) => token !== '');
    let total = 0;
    let currentSign = 1;

    for (const token of tokens) {
        if (token === '+' || token === '-') {
            currentSign = token === '+' ? 1 : -1;
            continue;
        }

        const parsedValue = Number(token);
        if (!Number.isFinite(parsedValue)) {
            return null;
        }

        total += currentSign * parsedValue;
        currentSign = 1;
    }

    return total;
}

function unifyMathInputValue(field) {
    if (!(field instanceof HTMLInputElement)) {
        return;
    }

    const rawValue = field.value;
    const evaluated = parseSafeMathExpression(rawValue);
    if (evaluated == null) {
        return;
    }

    field.value = String(evaluated);
    if (field.value === '-0') {
        field.value = '0';
    }
}

function formatDate(value) {
    if (!value) return 'Unknown time';
    return new Date(value).toLocaleString();
}

function formatTime(value) {
    if (!value) return 'Unknown time';
    return new Date(value).toLocaleTimeString();
}

function formatCompactDateRangeWithLinkedDates(startedAtUtc, endedAtUtc) {
    if (!startedAtUtc) {
        return 'Unknown time';
    }

    const start = new Date(startedAtUtc);
    const end = endedAtUtc ? new Date(endedAtUtc) : start;
    const startDate = start.toLocaleDateString();
    const endDate = end.toLocaleDateString();
    const startLink = `<a href="${googlePhotosSearchUrl(startDate)}" target="_blank" rel="noopener noreferrer">${startDate}</a>`;
    const endLink = `<a href="${googlePhotosSearchUrl(endDate)}" target="_blank" rel="noopener noreferrer">${endDate}</a>`;
    return startDate === endDate
        ? `${startLink} ${formatTime(startedAtUtc)} to ${formatTime(endedAtUtc ?? startedAtUtc)}`
        : `${startLink} - ${endLink} ${formatTime(startedAtUtc)} to ${formatTime(endedAtUtc ?? startedAtUtc)}`;
}

function googlePhotosSearchUrl(value) {
    return `https://photos.google.com/search/${encodeURIComponent(value ?? '')}`;
}

function googleMapsSearchUrl(latitude, longitude) {
    return `https://www.google.com/maps/search/${encodeURIComponent(`${latitude},${longitude}`)}`;
}

function googleMapsDirectionsUrl(startImage, endImage) {
    if (startImage?.latitude == null || startImage?.longitude == null || endImage?.latitude == null || endImage?.longitude == null) {
        return null;
    }

    return `https://www.google.com/maps/dir/${startImage.latitude},${startImage.longitude}/${endImage.latitude},${endImage.longitude}`;
}

function formatDateOnly(value) {
    if (!value) return 'Unknown date';
    return new Date(value).toLocaleDateString();
}

function formatCoordinate(latitude, longitude) {
    if (latitude == null || longitude == null) return 'No GPS';
    return `${latitude.toFixed(5)}, ${longitude.toFixed(5)}`;
}

function formatDistance(distanceKilometers) {
    if (distanceKilometers == null) return 'No GPS';
    if (distanceKilometers === 0) {
        return '';
    }

    const miles = distanceKilometers * 0.621371;
    if (miles < 0.1) {
        return `${miles.toFixed(2)} miles`;
    }

    return `${miles.toFixed(2)} miles`;
}

function formatCurrency(value) {
    if (value == null) {
        return null;
    }

    return `$${value.toFixed(2)}`;
}

/// Renders the per-image auto-detect OCR reading (if any) so a reviewer can eyeball each photo against the saved total.
function formatOcrSummary(item) {
    if (item.detectedErrorMessage) {
        return `<div class="thumb-ocr thumb-ocr-warning">OCR: ${escapeHtml(item.detectedErrorMessage)}</div>`;
    }

    const parts = [];
    if (item.detectedTotalCost != null) {
        parts.push(`$${Number(item.detectedTotalCost).toFixed(2)}`);
    }

    if (item.detectedLiters != null) {
        parts.push(`${Number(item.detectedLiters).toFixed(3)} L`);
    } else if (item.detectedGallons != null) {
        parts.push(`${Number(item.detectedGallons).toFixed(3)} gal`);
    }

    if (item.detectedOdometer != null) {
        parts.push(`odo ${item.detectedOdometer}`);
    }

    if (item.detectedVehicleName) {
        parts.push(escapeHtml(item.detectedVehicleName));
    }

    if (item.detectedConfidence != null) {
        parts.push(`${Math.round(item.detectedConfidence * 100)}% confidence`);
    }

    if (!parts.length) {
        return '';
    }

    const warnings = (item.detectedWarnings ?? [])
        .map((warning) => `<div class="thumb-ocr-warning">${escapeHtml(warning)}</div>`)
        .join('');

    return `<div class="thumb-ocr">OCR: ${parts.join(' · ')}${warnings}</div>`;
}

function computePricePerGallon(gallons, totalPrice) {
    if (!gallons || !totalPrice || gallons <= 0) {
        return null;
    }

    return totalPrice / gallons;
}

function formatMinutesSpread(startedAtUtc, endedAtUtc) {
    if (!startedAtUtc || !endedAtUtc) {
        return '0 min';
    }

    const start = new Date(startedAtUtc).getTime();
    const end = new Date(endedAtUtc).getTime();
    const seconds = Math.max(0, Math.round((end - start) / 1000));
    if (seconds < 60) {
        return `${seconds} sec`;
    }

    const minutes = Math.round(seconds / 60);
    return `${minutes} min`;
}

function formatImageTravel(previousImage, image) {
    if (!previousImage || !image) {
        return '';
    }

    const minutes = getMinutesSpread(previousImage.capturedAtUtc, image.capturedAtUtc);
    const miles = formatDistance(image.distanceFromPreviousKilometers);
    return `${minutes} min${miles ? `, ${miles}` : ''}`;
}

function getMinutesSpread(startedAtUtc, endedAtUtc) {
    if (!startedAtUtc || !endedAtUtc) {
        return 0;
    }

    const start = new Date(startedAtUtc).getTime();
    const end = new Date(endedAtUtc).getTime();
    return Math.max(0, Math.round((end - start) / 60000));
}

function getMiles(distanceKilometers) {
    if (distanceKilometers == null || distanceKilometers === 0) {
        return 0;
    }

    return distanceKilometers * 0.621371;
}

function findVehicleById(vehicleId) {
    if (vehicleId == null) {
        return null;
    }

    return state.vehicles.find((vehicle) => vehicle.vehicleId === vehicleId) ?? null;
}

function isGroupComplete(group) {
    const formState = getGroupFormState(group);
    if (formState.vehicleId == null || formState.totalPrice == null || formState.gallons == null) {
        return false;
    }

    const vehicle = findVehicleById(formState.vehicleId);
    const odometerRequired = !(vehicle?.noOdometer ?? false);
    return !odometerRequired || formState.odometer != null;
}

function hasQueueGreenCheck(group) {
    return isGroupComplete(group);
}

function escapeHtml(value) {
    return String(value)
        .replaceAll('&', '&amp;')
        .replaceAll('<', '&lt;')
        .replaceAll('>', '&gt;')
        .replaceAll('"', '&quot;');
}

function activeGroup() {
    return state.groups.find((group) => group.groupKey === state.selectedGroupKey) ?? null;
}

function updatePendingSnapshot() {
    state.pendingSnapshotGroupKeys = new Set(
        state.groups
            .filter((group) => isPending(group))
            .map((group) => group.groupKey)
    );
}

function isAutoDetected(group) {
    return group?.entrySource === 'AutoDetected';
}

function isAwaitingApproval(group) {
    return isAutoDetected(group) && group.reviewStatus !== 'Reviewed';
}

function isReviewed(group) {
    return group.fuelEventId != null && group.reviewStatus === 'Reviewed';
}

function isPending(group) {
    return !isReviewed(group) && !isAwaitingApproval(group);
}

function getGroupBadge(group) {
    if (isPending(group)) {
        return { label: 'Pending', className: 'badge-pending' };
    }

    if (isAwaitingApproval(group)) {
        return { label: 'Auto detected', className: 'badge-auto' };
    }

    if (isReviewed(group)) {
        return { label: 'Reviewed', className: 'badge-saved' };
    }

    return { label: 'Pending', className: 'badge-pending' };
}

function updateAutoSnapshot() {
    state.autoSnapshotGroupKeys = new Set(
        state.groups
            .filter((group) => isAwaitingApproval(group))
            .map((group) => group.groupKey)
    );
}

function getVisibleGroups() {
    if (state.dateFilter) {
        return state.groups.filter((group) => getGroupDayKey(group) === state.dateFilter);
    }

    let groups = state.groups;
    if (state.queueFilter === 'pending') {
        groups = groups.filter((group) => state.pendingSnapshotGroupKeys.has(group.groupKey) || isPending(group));
    }
    else if (state.queueFilter === 'auto') {
        groups = groups.filter((group) => state.autoSnapshotGroupKeys.has(group.groupKey) || isAwaitingApproval(group));
    }
    else if (state.queueFilter === 'reviewed') {
        groups = groups.filter((group) => isReviewed(group));
    }

    if (state.incompleteOnly) {
        groups = groups.filter((group) => !isGroupComplete(group));
    }

    return groups;
}

function activeImage() {
    const group = activeGroup();
    return group?.images.find((image) => image.sourceImageId === state.activeImageId) ?? group?.images[0] ?? null;
}

function getOrderedImages(group) {
    return [...(group?.images ?? [])].sort((left, right) => {
        const leftTime = left.capturedAtUtc ? new Date(left.capturedAtUtc).getTime() : Number.MAX_SAFE_INTEGER;
        const rightTime = right.capturedAtUtc ? new Date(right.capturedAtUtc).getTime() : Number.MAX_SAFE_INTEGER;
        if (leftTime !== rightTime) {
            return leftTime - rightTime;
        }

        return left.sourceImageId - right.sourceImageId;
    });
}

function getDefaultImageId(group) {
    const orderedImages = getOrderedImages(group);
    return orderedImages[0]?.sourceImageId ?? null;
}

/// Advances to the next group still outstanding under the filter the user is currently looking at.
function getNextPendingGroupKey(currentGroupKey) {
    const visibleGroups = getVisibleGroups();
    const currentIndex = visibleGroups.findIndex((group) => group.groupKey === currentGroupKey);
    if (currentIndex === -1) {
        return visibleGroups.find((group) => isOutstandingForFilter(group))?.groupKey ?? null;
    }

    for (let index = currentIndex + 1; index < visibleGroups.length; index++) {
        if (isOutstandingForFilter(visibleGroups[index])) {
            return visibleGroups[index].groupKey;
        }
    }

    for (let index = 0; index < currentIndex; index++) {
        if (isOutstandingForFilter(visibleGroups[index])) {
            return visibleGroups[index].groupKey;
        }
    }

    return currentGroupKey;
}

function isOutstandingForFilter(group) {
    if (state.queueFilter === 'reviewed') {
        return false;
    }

    if (state.queueFilter === 'auto') {
        return isAwaitingApproval(group);
    }

    if (state.incompleteOnly) {
        return !isGroupComplete(group);
    }

    return isPending(group);
}

function getDraft(groupKey) {
    return groupKey ? state.drafts[groupKey] ?? null : null;
}

function getSplitSelection(groupKey) {
    return groupKey ? state.splitSelections[groupKey] ?? [] : [];
}

function setSplitSelection(groupKey, imageIds) {
    if (groupKey) {
        state.splitSelections[groupKey] = imageIds;
    }
}

function updateSplitButtonsVisibility(groupKey) {
    const container = document.getElementById('splitActionsContainer');
    const splitBtn = document.getElementById('splitSelectedButton');
    const selection = getSplitSelection(groupKey);
    if (container instanceof HTMLElement) {
        container.style.display = selection.length > 0 ? '' : 'none';
    }
    if (splitBtn instanceof HTMLElement) {
        splitBtn.textContent = `Split selected (${selection.length})`;
    }
}

function getGroupFormState(group) {
    return getDraft(group.groupKey) ?? {
        fuelEventId: group.fuelEventId,
        imageIds: group.images.map((image) => image.sourceImageId),
        vehicleId: group.vehicleId ?? null,
        odometer: group.odometer ?? null,
        gallons: group.gallons ?? null,
        liters: group.liters ?? null,
        volumeUnit: group.liters != null ? 'liters' : 'gallons',
        totalPrice: group.totalPrice ?? null,
        locationName: group.locationName ?? null,
        notes: group.notes ?? null,
        reviewerName: state.lastSavedReviewerName || ''
    };
}

function serializeFormState(formState) {
    return JSON.stringify({
        fuelEventId: formState.fuelEventId ?? null,
        imageIds: [...(formState.imageIds ?? [])].sort((left, right) => left - right),
        vehicleId: formState.vehicleId ?? null,
        odometer: formState.odometer ?? null,
        gallons: formState.gallons ?? null,
        liters: formState.liters ?? null,
        totalPrice: formState.totalPrice ?? null,
        locationName: formState.locationName ?? null,
        notes: formState.notes ?? null,
        reviewerName: formState.reviewerName ?? (state.lastSavedReviewerName || '')
    });
}

function captureFormDraft() {
    const group = activeGroup();
    if (!group) {
        return null;
    }

    const reviewerInput = document.getElementById('reviewerName');
    const reviewerValue = reviewerInput instanceof HTMLInputElement ? reviewerInput.value : (state.lastSavedReviewerName || '');

    const volumeUnitSelect = document.getElementById('volumeUnit');
    const volumeUnit = volumeUnitSelect instanceof HTMLSelectElement ? volumeUnitSelect.value : 'gallons';
    const volumeValue = numberOrNull(document.getElementById('volume').value);
    const gallons = volumeUnit === 'liters'
        ? (volumeValue != null ? litersToGallons(volumeValue) : null)
        : volumeValue;
    const liters = volumeUnit === 'liters' ? volumeValue : null;

    const draft = {
        fuelEventId: group.fuelEventId,
        imageIds: group.images.map((image) => image.sourceImageId),
        vehicleId: numberOrNull(vehicleSelect.value),
        odometer: numberOrNull(document.getElementById('odometer').value),
        gallons,
        liters,
        volumeUnit,
        totalPrice: numberOrNull(document.getElementById('totalPrice').value),
        locationName: document.getElementById('locationName').value || null,
        notes: document.getElementById('notes').value || null,
        reviewerName: reviewerValue
    };

    state.drafts[group.groupKey] = draft;
    return draft;
}

function captureFocusState() {
    const activeElement = document.activeElement;
    if (activeElement instanceof HTMLInputElement || activeElement instanceof HTMLTextAreaElement || activeElement instanceof HTMLSelectElement) {
        return {
            id: activeElement.id,
            selectionStart: activeElement instanceof HTMLInputElement || activeElement instanceof HTMLTextAreaElement ? activeElement.selectionStart : null,
            selectionEnd: activeElement instanceof HTMLInputElement || activeElement instanceof HTMLTextAreaElement ? activeElement.selectionEnd : null
        };
    }

    return null;
}

function focusFirstEmptyField() {
    const orderedFields = [
        document.getElementById('totalPrice'),
        document.getElementById('volume'),
        document.getElementById('locationName'),
        document.getElementById('vehicleId'),
        document.getElementById('odometer'),
        document.getElementById('notes')
    ];

    const firstEmpty = orderedFields.find((field) => {
        if (!(field instanceof HTMLInputElement) && !(field instanceof HTMLTextAreaElement) && !(field instanceof HTMLSelectElement)) {
            return false;
        }

        return field.value === '';
    });

    if (firstEmpty instanceof HTMLInputElement || firstEmpty instanceof HTMLTextAreaElement || firstEmpty instanceof HTMLSelectElement) {
        firstEmpty.focus();
    }
}

function restoreFocusState(focusState) {
    if (!focusState?.id) {
        focusFirstEmptyField();
        return;
    }

    const field = document.getElementById(focusState.id);
    if (!(field instanceof HTMLInputElement) && !(field instanceof HTMLTextAreaElement) && !(field instanceof HTMLSelectElement)) {
        focusFirstEmptyField();
        return;
    }

    field.focus();
    if ((field instanceof HTMLInputElement || field instanceof HTMLTextAreaElement) && focusState.selectionStart != null && focusState.selectionEnd != null) {
        field.setSelectionRange(focusState.selectionStart, focusState.selectionEnd);
    }
}

function selectLastImageForOdometerFocus() {
    const group = activeGroup();
    if (!group) {
        return;
    }

    const orderedImages = getOrderedImages(group);
    const lastImage = orderedImages[orderedImages.length - 1];
    if (!lastImage || lastImage.sourceImageId === state.activeImageId) {
        return;
    }

    const odometerField = document.getElementById('odometer');
    const selectionStart = odometerField instanceof HTMLInputElement ? odometerField.selectionStart : null;
    const selectionEnd = odometerField instanceof HTMLInputElement ? odometerField.selectionEnd : null;

    captureFormDraft();
    state.activeImageId = lastImage.sourceImageId;
    state.hoverPreview = null;
    renderWorkspace();

    const refreshedOdometerField = document.getElementById('odometer');
    if (refreshedOdometerField instanceof HTMLInputElement) {
        refreshedOdometerField.focus();
        if (selectionStart != null && selectionEnd != null) {
            refreshedOdometerField.setSelectionRange(selectionStart, selectionEnd);
        }
    }
}

/// Lets the Log page's "Edit" link (`/?fuelEventId=123`) deep-link back to the matching review queue entry.
function getRequestedFuelEventIdFromUrl() {
    const params = new URLSearchParams(window.location.search);
    const raw = params.get('fuelEventId');
    if (!raw) {
        return null;
    }

    const value = Number(raw);
    return Number.isFinite(value) ? value : null;
}

async function loadData(preferredGroupKey, preferredImageId, focusFieldId = null) {
    renderStatus('Loading grouped images…');
    try {
        const [groupResponse, vehicleResponse] = await Promise.all([
            fetch('/api/manual/groups'),
            fetch('/api/vehicles')
        ]);

        if (!groupResponse.ok || !vehicleResponse.ok) {
            throw new Error(`HTTP ${groupResponse.status}/${vehicleResponse.status}`);
        }

        state.groups = await groupResponse.json();
        state.vehicles = await vehicleResponse.json();
        updatePendingSnapshot();
        updateAutoSnapshot();

        const requestedFuelEventId = getRequestedFuelEventIdFromUrl();
        let targetGroupKey = preferredGroupKey && state.groups.some((group) => group.groupKey === preferredGroupKey)
            ? preferredGroupKey
            : null;

        if (!targetGroupKey && preferredImageId) {
            targetGroupKey = state.groups.find((group) => group.images.some((image) => image.sourceImageId === preferredImageId))?.groupKey ?? null;
        }

        if (!targetGroupKey && requestedFuelEventId != null) {
            const matched = state.groups.find((group) => group.fuelEventId === requestedFuelEventId);
            if (matched) {
                targetGroupKey = matched.groupKey;
                state.queueFilter = 'all';
                state.dateFilter = null;
                updateQueueFilterButtons();
            }
        }

        const visibleGroups = getVisibleGroups();
        state.selectedGroupKey = targetGroupKey ?? (visibleGroups[0]?.groupKey ?? state.groups[0]?.groupKey ?? null);

        const selectedGroup = activeGroup();
        state.activeImageId = preferredImageId && selectedGroup?.images.some((image) => image.sourceImageId === preferredImageId)
            ? preferredImageId
            : getDefaultImageId(selectedGroup);
        state.hoverPreview = null;

        renderVehicleOptions();
        renderQueue();
        renderWorkspace();

        if (focusFieldId) {
            const field = document.getElementById(focusFieldId);
            if (field instanceof HTMLInputElement || field instanceof HTMLTextAreaElement || field instanceof HTMLSelectElement) {
                field.focus();
                if (field instanceof HTMLInputElement) {
                    field.select();
                }
            }
        }

        renderStatus(`Loaded ${state.groups.length} image groups.`);
    } catch (error) {
        console.error(error);
        state.groups = [];
        state.vehicles = [];
        state.selectedGroupKey = null;
        state.activeImageId = null;
        state.hoverPreview = null;
        renderQueue();
        renderWorkspace();
        renderStatus('Unable to load grouped images.');
    }
}

function renderVehicleOptions() {
    const currentValue = vehicleSelect.value;
    vehicleSelect.innerHTML = ['<option value="">Choose a vehicle</option>']
        .concat(state.vehicles.map((vehicle) => `<option value="${vehicle.vehicleId}">${escapeHtml(vehicle.name)}</option>`))
        .join('');
    vehicleSelect.value = currentValue;
}

function getGroupTimestamp(group) {
    const orderedImages = getOrderedImages(group);
    return orderedImages[0]?.capturedAtUtc ?? group.startedAtUtc ?? null;
}

function getGroupDayKey(group) {
    const timestamp = getGroupTimestamp(group);
    if (!timestamp) {
        return 'unknown';
    }

    const date = new Date(timestamp);
    return `${date.getFullYear()}-${date.getMonth()}-${date.getDate()}`;
}

function formatDayHeading(group) {
    const timestamp = getGroupTimestamp(group);
    if (!timestamp) {
        return 'Unknown date';
    }

    const date = new Date(timestamp);
    const today = new Date();
    const yesterday = new Date(today);
    yesterday.setDate(today.getDate() - 1);

    const sameDay = (left, right) => left.toDateString() === right.toDateString();
    if (sameDay(date, today)) {
        return 'Today';
    }

    if (sameDay(date, yesterday)) {
        return 'Yesterday';
    }

    return date.toLocaleDateString(undefined, {
        weekday: 'short',
        month: 'short',
        day: 'numeric',
        year: date.getFullYear() === today.getFullYear() ? undefined : 'numeric'
    });
}

const QUEUE_EMPTY_MESSAGES = {
    pending: 'No pending image groups.',
    auto: 'No auto detected groups awaiting approval.',
    reviewed: 'No reviewed groups yet.'
};

function renderQueue() {
    const visibleGroups = getVisibleGroups();
    if (queueDisplayCountEl instanceof HTMLElement) {
        queueDisplayCountEl.textContent = `${visibleGroups.length} ${visibleGroups.length === 1 ? 'entry' : 'entries'} displayed`;
    }

    if (!visibleGroups.length) {
        const message = state.dateFilter ? 'No image groups found for this date.' : (QUEUE_EMPTY_MESSAGES[state.queueFilter] ?? 'No imported images found yet.');
        groupListEl.innerHTML = `<div class="empty-state">${message}</div>`;
        return;
    }

    const groupsPerDay = visibleGroups.reduce((counts, group) => {
        const dayKey = getGroupDayKey(group);
        counts[dayKey] = (counts[dayKey] ?? 0) + 1;
        return counts;
    }, {});

    let lastDayKey = null;

    groupListEl.innerHTML = visibleGroups.map((group) => {
        const dayKey = getGroupDayKey(group);
        const dayHeader = dayKey === lastDayKey
            ? ''
            : `<div class="queue-day ${dayKey === state.dateFilter ? 'active' : ''}" data-day-key="${dayKey}" title="Show every entry for this date"><span>${escapeHtml(formatDayHeading(group))}</span><span class="queue-day-count">${groupsPerDay[dayKey]} stop${groupsPerDay[dayKey] === 1 ? '' : 's'}</span></div>`;
        lastDayKey = dayKey;
        const formState = getGroupFormState(group);
        const orderedImages = getOrderedImages(group);
        const earliestPhotoTimestamp = orderedImages[0]?.capturedAtUtc ?? group.startedAtUtc;
        const vehicleName = findVehicleById(formState.vehicleId)?.name ?? null;
        const locationName = formState.locationName || group.locationName || null;
        const title = vehicleName || locationName
            ? [vehicleName, locationName].filter((value) => !!value).join(' - ')
            : `${group.images.length} image${group.images.length === 1 ? '' : 's'}`;
        const ppg = computePricePerGallon(formState.gallons, formState.totalPrice) ?? group.pricePerGallon;
        const dollarsText = formatCurrency(formState.totalPrice);
        const spreadDistance = formatDistance(group.maxDistanceKilometers) || '0.00 miles';
        const spreadMinutes = getMinutesSpread(group.startedAtUtc, group.endedAtUtc);
        const spreadMiles = getMiles(group.maxDistanceKilometers);
        const imageCountClass = group.images.length > 2 ? 'queue-count-emphasis' : '';
        const minutesClass = spreadMinutes > 20 ? 'queue-spread-alert' : '';
        const milesClass = spreadMiles > 0.1 ? 'queue-spread-alert' : '';
        const showGreenCheck = hasQueueGreenCheck(group);
        const badge = getGroupBadge(group);
        const autoStatus = getAutoDetectStatus(group.groupKey);
        const autoStatusMarkup = autoStatus
            ? `<div class="queue-meta queue-detecting"><span class="spinner"></span>${autoStatus === 'running' ? 'Detecting…' : 'Queued for auto detect'}</div>`
            : '';

        return `
    ${dayHeader}
    <div class="queue-item ${group.groupKey === state.selectedGroupKey ? 'active' : ''}" data-group-key="${group.groupKey}">
      <div class="queue-title">
                <strong class="queue-title-main">${showGreenCheck ? '<span class="queue-check">✓</span>' : ''}<span>${escapeHtml(title)}</span></strong>
        <span class="badge ${badge.className}">${badge.label}</span>
      </div>
                        <div class="queue-meta">${formatDate(earliestPhotoTimestamp)}</div>
            ${autoStatusMarkup}
            <div class="queue-meta"><span class="${imageCountClass}">${group.images.length} image${group.images.length === 1 ? '' : 's'}</span> - <span class="${minutesClass}">${formatMinutesSpread(group.startedAtUtc, group.endedAtUtc)}</span>, <span class="${milesClass}">${spreadDistance}</span></div>
            <div class="queue-meta">Dollars: ${dollarsText ?? 'not set'}${ppg != null ? ` (${escapeHtml(`$${ppg.toFixed(3)}`)})` : ''}</div>
            ${formState.odometer != null ? `<div class="queue-meta">Odometer: ${formState.odometer}</div>` : ''}
      <div class="queue-actions ${state.selectedGroupKey && group.groupKey === state.selectedGroupKey ? 'hidden' : ''}">
        <button type="button" class="secondary" data-action="merge-into-selected" data-source-group-key="${group.groupKey}">Merge into selected</button>
      </div>
    </div>
    `;
    }).join('');

    groupListEl.querySelectorAll('[data-group-key]').forEach((element) => {
        element.addEventListener('click', () => {
            captureFormDraft();
            state.selectedGroupKey = element.dataset.groupKey;
            state.activeImageId = getDefaultImageId(activeGroup());
            state.hoverPreview = null;
            renderQueue();
            renderWorkspace();
        });
    });

    groupListEl.querySelectorAll('[data-day-key]').forEach((element) => {
        element.addEventListener('click', (event) => {
            event.stopPropagation();
            toggleDateFilter(element.dataset.dayKey);
        });
    });

    groupListEl.querySelectorAll('[data-action="merge-into-selected"]').forEach((button) => {
        button.addEventListener('click', async (event) => {
            event.stopPropagation();
            await mergeIntoSelected(button.dataset.sourceGroupKey);
        });
    });

    scrollSelectedQueueItemIntoView();
}

/// Keeps the active card one slot down from the top so the previous group stays visible for context.
function scrollSelectedQueueItemIntoView() {
    const selected = groupListEl.querySelector('.queue-item.active');
    if (!(selected instanceof HTMLElement)) {
        return;
    }

    const previous = selected.previousElementSibling;
    const target = previous instanceof HTMLElement ? previous : selected;
    const offset = target.offsetTop - groupListEl.offsetTop;

    groupListEl.scrollTo({ top: Math.max(0, offset), behavior: 'smooth' });
}

function renderWorkspace() {
    const group = activeGroup();
    if (!group) {
        viewerShellEl.innerHTML = '<div class="empty-state">Select a group after importing images.</div>';
        entryForm.reset();
        groupMetaEl.textContent = '';
        if (detectionCalloutEl instanceof HTMLElement) {
            detectionCalloutEl.style.display = 'none';
        }
        const calloutsPanelEl = document.getElementById('calloutsPanel');
        if (calloutsPanelEl instanceof HTMLElement) {
            calloutsPanelEl.style.display = 'none';
        }
        renderPricePerGallon();
        return;
    }

    const image = activeImage();
    if (image) {
        state.activeImageId = image.sourceImageId;
    }

    const orderedImages = getOrderedImages(group);

    const splitSelection = getSplitSelection(group.groupKey);
    const hasSplitSelection = splitSelection.length > 0;
    const firstImage = orderedImages[0];
    const lastImage = orderedImages[orderedImages.length - 1];
    const directionsUrl = googleMapsDirectionsUrl(firstImage, lastImage);
    const groupMiles = formatDistance(group.maxDistanceKilometers);

    viewerShellEl.innerHTML = `
    <div class="viewer-top">
      <div>
        <strong>${escapeHtml(group.locationName || 'Manual review group')}</strong>
            <div class="group-meta">${formatCompactDateRangeWithLinkedDates(group.startedAtUtc, group.endedAtUtc)} · ${formatMinutesSpread(group.startedAtUtc, group.endedAtUtc)} spread</div>
        ${directionsUrl ? `<div class="group-meta"><a href="${directionsUrl}" target="_blank" rel="noopener noreferrer">Directions${groupMiles ? ` (${groupMiles})` : ''}</a></div>` : ''}
      </div>
      <div class="viewer-actions">
        <div id="splitActionsContainer" style="${hasSplitSelection ? '' : 'display: none;'}">
          <button id="splitSelectedButton" type="button" class="secondary">Split selected (${splitSelection.length})</button>
          <button id="clearSplitSelectionButton" type="button" class="secondary">Clear selection</button>
        </div>
      </div>
      <div class="thumb-strip">
                                ${orderedImages.map((item, index) => {
        const zoomIndex = state.thumbZoomByImageId[item.sourceImageId] ?? 0;
        const currentZoom = THUMB_ZOOM_LEVELS[zoomIndex];
        const mapsUrl = item.latitude != null && item.longitude != null ? googleMapsSearchUrl(item.latitude, item.longitude) : null;
        const travel = formatImageTravel(orderedImages[index - 1], item);
        return `
          <div class="thumb-card ${item.sourceImageId === state.activeImageId ? 'active' : ''}" data-role="thumb-card" data-image-id="${item.sourceImageId}">
            <div class="thumb-actions">
              <label class="thumb-check">
                <input type="checkbox" data-role="split-checkbox" data-image-id="${item.sourceImageId}" ${splitSelection.includes(item.sourceImageId) ? 'checked' : ''} />
              </label>
            </div>
            <div class="thumb-image-frame" data-role="thumb-frame" data-image-id="${item.sourceImageId}">
              <img src="${item.imageUrl}" alt="${escapeHtml(item.fileName)}" data-role="thumb-image" data-image-id="${item.sourceImageId}" style="transform: scale(${currentZoom});" />
            </div>
                        ${formatOcrSummary(item)}
                        <div class="image-meta"><a href="${googlePhotosSearchUrl(item.fileName)}" target="_blank" rel="noopener noreferrer">${escapeHtml(item.fileName)}</a></div>
                        <div class="thumb-time"><a href="${googlePhotosSearchUrl(item.fileName)}" target="_blank" rel="noopener noreferrer">${formatDate(item.capturedAtUtc)}</a></div>
                        ${mapsUrl ? `<div class="thumb-time"><a href="${mapsUrl}" target="_blank" rel="noopener noreferrer">${formatCoordinate(item.latitude, item.longitude)}</a></div>` : ''}
                        ${travel ? `<div class="thumb-time">${travel}</div>` : ''}
                        <button type="button" class="thumb-delete" data-role="delete-image" data-image-id="${item.sourceImageId}" title="Delete this image">&#128465;</button>
          </div>
        `;
    }).join('')}
      </div>
    </div>
    <div class="hero-image" id="heroImage">${image ? `
      <img id="heroImageTag" src="${image.imageUrl}" alt="${escapeHtml(image.fileName)}" />
      <div id="magnifierLens" class="magnifier-lens"></div>
      <div id="zoomBadge" class="zoom-badge">${MAGNIFICATION_LABELS[state.magnificationIndex]} (click to cycle)</div>
    ` : '<div class="helper">No image selected.</div>'}</div>
    <div>
      <div class="image-meta">${image ? `${escapeHtml(image.fileName)} · ${escapeHtml(image.imageTypeCandidate)} · ${(image.imageTypeConfidence * 100).toFixed(0)}% heuristic confidence` : 'No image selected.'}</div>
      <div class="image-meta">Captured: ${image ? formatDate(image.capturedAtUtc) : 'Unknown time'}</div>
    </div>
  `;

    viewerShellEl.querySelectorAll('[data-role="thumb-card"]').forEach((card) => {
        card.addEventListener('click', () => {
            const imageId = Number(card.dataset.imageId);
            const focusState = captureFocusState();
            captureFormDraft();
            state.activeImageId = imageId;
            const currentIndex = state.thumbZoomByImageId[imageId] ?? 0;
            state.thumbZoomByImageId[imageId] = (currentIndex + 1) % THUMB_ZOOM_LEVELS.length;
            renderWorkspace();
            restoreFocusState(focusState);
        });
    });

    const heroEl = document.getElementById('heroImage');
    if (heroEl && image) {
        heroEl.addEventListener('mousemove', handleHeroMouseMove);
        heroEl.addEventListener('mouseenter', handleHeroMouseMove);
        heroEl.addEventListener('mouseleave', handleHeroMouseLeave);
        heroEl.addEventListener('click', handleHeroClick);
    }

    viewerShellEl.querySelectorAll('[data-role="split-checkbox"]').forEach((element) => {
        element.addEventListener('click', (event) => {
            event.stopPropagation();
        });

        const label = element.closest('.thumb-check');
        if (label instanceof HTMLElement) {
            label.addEventListener('click', (event) => {
                event.stopPropagation();
            });
        }

        element.addEventListener('change', () => {
            const selected = Array.from(viewerShellEl.querySelectorAll('[data-role="split-checkbox"]'))
                .filter((input) => input.checked)
                .map((input) => Number(input.dataset.imageId));
            setSplitSelection(group.groupKey, selected);
            updateSplitButtonsVisibility(group.groupKey);
        });
    });

    const splitBtn = document.getElementById('splitSelectedButton');
    if (splitBtn) {
        splitBtn.addEventListener('click', splitSelectedImages);
    }
    const clearSplitBtn = document.getElementById('clearSplitSelectionButton');
    if (clearSplitBtn) {
        clearSplitBtn.addEventListener('click', () => {
            setSplitSelection(group.groupKey, []);
            renderWorkspace();
        });
    }

    viewerShellEl.querySelectorAll('[data-role="delete-image"]').forEach((button) => {
        button.addEventListener('click', (event) => {
            event.stopPropagation();
            deleteImage(Number(button.dataset.imageId));
        });
    });

    const formState = getGroupFormState(group);
    vehicleSelect.value = formState.vehicleId ?? '';
    document.getElementById('odometer').value = formState.odometer ?? '';
    const volumeUnitSelect = document.getElementById('volumeUnit');
    const volumeUnit = formState.volumeUnit ?? (formState.liters != null ? 'liters' : 'gallons');
    if (volumeUnitSelect instanceof HTMLSelectElement) {
        volumeUnitSelect.value = volumeUnit;
        volumeUnitSelect.dataset.previousUnit = volumeUnit;
    }
    document.getElementById('volume').value = (volumeUnit === 'liters' ? formState.liters : formState.gallons) ?? '';
    document.getElementById('totalPrice').value = formState.totalPrice ?? '';
    document.getElementById('locationName').value = formState.locationName ?? '';
    document.getElementById('notes').value = formState.notes ?? '';
    document.getElementById('reviewerName').value = formState.reviewerName ?? (state.lastSavedReviewerName || '');
    groupMetaEl.textContent = group.fuelEventId ? `Editing event #${group.fuelEventId}` : `${group.images.length} linked image${group.images.length === 1 ? '' : 's'}`;
    renderDetectionCallout(group);
    renderAnomalyCallouts(group);
    updateAutoDetectGroupButton();
    renderPricePerGallon(group.pricePerGallon);
}

function renderDetectionCallout(group) {
    if (saveButton instanceof HTMLButtonElement) {
        saveButton.textContent = isAwaitingApproval(group) ? 'Approve group' : 'Save group';
    }

    if (!(detectionCalloutEl instanceof HTMLElement)) {
        return;
    }

    if (!isAwaitingApproval(group)) {
        detectionCalloutEl.style.display = 'none';
        detectionCalloutEl.textContent = '';
        return;
    }

    const confidence = group.detectionConfidence != null
        ? ` (${(group.detectionConfidence * 100).toFixed(0)}% model confidence)`
        : '';

    detectionCalloutEl.style.display = '';
    detectionCalloutEl.textContent = `Auto detected${confidence}. ${group.reviewReason || 'Check the values and approve to mark this group reviewed.'}`;
}

async function deleteImage(sourceImageId) {
    if (!sourceImageId) {
        return;
    }

    if (!window.confirm('Delete this image and its database entry? This cannot be undone.')) {
        return;
    }

    const group = activeGroup();
    const remainingImageId = group?.images.find((image) => image.sourceImageId !== sourceImageId)?.sourceImageId ?? null;
    renderStatus('Deleting image…');
    try {
        const response = await fetch(`/api/images/${sourceImageId}`, { method: 'DELETE' });
        if (!response.ok) {
            throw new Error(`HTTP ${response.status}`);
        }

        delete state.drafts[group?.groupKey];
        await loadData(null, remainingImageId);
        renderStatus('Image deleted.');
    } catch (error) {
        console.error(error);
        renderStatus(error.message || 'Unable to delete the image.');
    }
}

function renderAnomalyCallouts(group) {
    const calloutsPanelEl = document.getElementById('calloutsPanel');
    if (!(calloutsPanelEl instanceof HTMLElement)) {
        return;
    }

    const persistedFlags = group.anomalyFlags ?? [];
    const liveFlags = state.liveCalloutsByGroup[group.groupKey] ?? [];
    const flags = [...new Set([...persistedFlags, ...liveFlags])];
    if (!flags.length) {
        calloutsPanelEl.style.display = 'none';
        calloutsPanelEl.innerHTML = '';
        return;
    }

    const acknowledged = !!group.anomalyAcknowledged && liveFlags.length === 0;
    calloutsPanelEl.style.display = '';
    calloutsPanelEl.classList.toggle('acknowledged', acknowledged);
    calloutsPanelEl.innerHTML = `
        <div class="callout-panel-header">${acknowledged ? 'Callouts (approved)' : 'Callouts'}</div>
        <ul>${flags.map((flag) => `<li>${escapeHtml(flag)}</li>`).join('')}</ul>
        ${group.fuelEventId && persistedFlags.length > 0 ? `<button type="button" class="secondary" data-action="toggle-anomaly-ack">${acknowledged ? 'Unapprove' : 'Approve, not a problem'}</button>` : ''}
    `;

    const toggleBtn = calloutsPanelEl.querySelector('[data-action="toggle-anomaly-ack"]');
    if (toggleBtn) {
        toggleBtn.addEventListener('click', () => toggleAnomalyAcknowledged(group, !acknowledged));
    }
}

async function validateCurrentGroup() {
    const group = activeGroup();
    const draft = captureFormDraft();
    if (!group || !draft) {
        return;
    }

    try {
        const response = await fetch('/api/manual/live-validation', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({
                fuelEventId: draft.fuelEventId,
                vehicleId: draft.vehicleId,
                odometer: draft.odometer,
                gallons: draft.gallons,
                eventTimeUtc: group.startedAtUtc
            })
        });

        if (!response.ok) {
            throw new Error(`HTTP ${response.status}`);
        }

        const result = await response.json();
        state.liveCalloutsByGroup[group.groupKey] = result.callouts ?? [];
        renderAnomalyCallouts(group);
    } catch (error) {
        console.error(error);
    }
}

async function toggleAnomalyAcknowledged(group, acknowledged) {
    if (!group?.fuelEventId) {
        return;
    }

    renderStatus(acknowledged ? 'Approving callout…' : 'Reopening callout…');
    try {
        const response = await fetch(`/api/events/${group.fuelEventId}/anomaly-ack`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ acknowledged })
        });

        if (!response.ok) {
            throw new Error(`HTTP ${response.status}`);
        }

        group.anomalyAcknowledged = acknowledged;
        renderAnomalyCallouts(group);
        renderStatus(acknowledged ? 'Callout approved.' : 'Callout reopened.');
    } catch (error) {
        console.error(error);
        renderStatus(error.message || 'Unable to update the callout.');
    }
}

function handleHeroMouseMove(event) {
    const hero = document.getElementById('heroImage');
    const imageTag = document.getElementById('heroImageTag');
    const lens = document.getElementById('magnifierLens');
    const badge = document.getElementById('zoomBadge');
    if (!(hero instanceof HTMLElement) || !(imageTag instanceof HTMLImageElement) || !(lens instanceof HTMLElement)) {
        return;
    }

    const heroRect = hero.getBoundingClientRect();
    const imgRect = imageTag.getBoundingClientRect();

    const mouseX = event.clientX - heroRect.left;
    const mouseY = event.clientY - heroRect.top;

    if (mouseX < 0 || mouseX > heroRect.width || mouseY < 0 || mouseY > heroRect.height) {
        lens.classList.remove('visible');
        return;
    }

    const lensSize = 400;
    lens.style.left = `${mouseX - lensSize / 2}px`;
    lens.style.top = `${mouseY - lensSize / 2}px`;

    const naturalWidth = imageTag.naturalWidth || imgRect.width;
    const naturalHeight = imageTag.naturalHeight || imgRect.height;
    const naturalRatio = naturalWidth / (naturalHeight || 1);
    const boxRatio = imgRect.width / (imgRect.height || 1);

    let renderedWidth = imgRect.width;
    let renderedHeight = imgRect.height;
    let renderedLeft = imgRect.left;
    let renderedTop = imgRect.top;

    if (boxRatio > naturalRatio) {
        renderedHeight = imgRect.height;
        renderedWidth = imgRect.height * naturalRatio;
        renderedLeft = imgRect.left + (imgRect.width - renderedWidth) / 2;
    } else {
        renderedWidth = imgRect.width;
        renderedHeight = imgRect.width / naturalRatio;
        renderedTop = imgRect.top + (imgRect.height - renderedHeight) / 2;
    }

    let relX = renderedWidth > 0 ? (event.clientX - renderedLeft) / renderedWidth : 0.5;
    let relY = renderedHeight > 0 ? (event.clientY - renderedTop) / renderedHeight : 0.5;
    relX = Math.max(0, Math.min(1, relX));
    relY = Math.max(0, Math.min(1, relY));

    const zoom = MAGNIFICATION_LEVELS[state.magnificationIndex];
    const bgWidth = renderedWidth * zoom;
    const bgHeight = renderedHeight * zoom;
    const bgX = -(relX * bgWidth - lensSize / 2);
    const bgY = -(relY * bgHeight - lensSize / 2);

    lens.style.backgroundImage = `url("${imageTag.src}")`;
    lens.style.backgroundSize = `${bgWidth}px ${bgHeight}px`;
    lens.style.backgroundPosition = `${bgX}px ${bgY}px`;
    lens.classList.add('visible');

    if (badge) {
        badge.textContent = `${MAGNIFICATION_LABELS[state.magnificationIndex]} (click to cycle)`;
    }
}

function handleHeroClick(event) {
    state.magnificationIndex = (state.magnificationIndex + 1) % MAGNIFICATION_LEVELS.length;
    handleHeroMouseMove(event);
}

function handleHeroMouseLeave() {
    const lens = document.getElementById('magnifierLens');
    if (lens instanceof HTMLElement) {
        lens.classList.remove('visible');
    }
}

function renderPricePerGallon(existingValue) {
    const volumeUnitSelect = document.getElementById('volumeUnit');
    const volumeUnit = volumeUnitSelect instanceof HTMLSelectElement ? volumeUnitSelect.value : 'gallons';
    const volumeValue = numberOrNull(document.getElementById('volume').value);
    const gallons = volumeUnit === 'liters'
        ? (volumeValue != null ? litersToGallons(volumeValue) : null)
        : volumeValue;
    const totalPrice = numberOrNull(document.getElementById('totalPrice').value);
    const computed = gallons && totalPrice ? totalPrice / gallons : null;
    const displayValue = computed ?? existingValue;
    pricePerGallonDisplayEl.textContent = displayValue
        ? `Estimated price per gallon: $${displayValue.toFixed(3)}`
        : 'Price per gallon will be calculated after gallons and dollars are entered.';
}

function updateLocalGroupFromPayload(groupKey, payload, savedEvent) {
    const group = state.groups.find((item) => item.groupKey === groupKey);
    if (!group) {
        return;
    }

    group.fuelEventId = savedEvent?.fuelEventId ?? group.fuelEventId;
    group.vehicleId = payload.vehicleId;
    group.odometer = payload.odometer;
    group.gallons = payload.gallons;
    group.liters = payload.liters;
    group.totalPrice = payload.totalPrice;
    group.locationName = payload.locationName;
    group.notes = payload.notes;
    group.reviewStatus = savedEvent?.reviewStatus ?? 'Reviewed';
    group.pricePerGallon = savedEvent?.pricePerGallon ?? (payload.gallons && payload.totalPrice ? payload.totalPrice / payload.gallons : null);
}

async function persistCurrentGroup(options = {}) {
    const group = activeGroup();
    if (!group) {
        return false;
    }

    if (getAutoDetectStatus(group.groupKey)) {
        renderStatus('This group is being auto detected. It will be saveable once detection finishes.');
        return false;
    }

    const payload = captureFormDraft();
    if (!payload) {
        return false;
    }

    const requestBody = options.markReviewed === false ? { ...payload, markReviewed: false } : payload;

    if (state.saveInFlightByGroup[group.groupKey]) {
        state.pendingAutosaveByGroup[group.groupKey] = true;
        return false;
    }

    state.saveInFlightByGroup[group.groupKey] = true;
    if (!options.silent) {
        renderStatus(options.statusMessage || 'Saving group…');
    }

    try {
        const response = await fetch('/api/manual/groups/save', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(requestBody)
        });

        if (!response.ok) {
            const errorPayload = await response.json().catch(() => null);
            throw new Error(errorPayload?.message || `HTTP ${response.status}`);
        }

        const savedEvent = await response.json();
        if (payload.reviewerName !== undefined) {
            state.lastSavedReviewerName = payload.reviewerName;
            try {
                if (payload.reviewerName) {
                    localStorage.setItem('snapfuel_last_reviewer_name', payload.reviewerName);
                } else {
                    localStorage.removeItem('snapfuel_last_reviewer_name');
                }
            } catch {
                // ignore
            }
        }
        updateLocalGroupFromPayload(group.groupKey, payload, savedEvent);
        state.drafts[group.groupKey] = { ...payload, fuelEventId: savedEvent?.fuelEventId ?? payload.fuelEventId };
        renderQueue();
        if (!options.silent) {
            renderWorkspace();
            renderStatus(options.successMessage || 'Group saved.');
        }

        if (options.reloadAfterSave) {
            await loadData(group.groupKey, state.activeImageId);
        }

        return true;
    } catch (error) {
        console.error(error);
        renderStatus(error.message || 'Unable to save the selected group.');
        return false;
    } finally {
        state.saveInFlightByGroup[group.groupKey] = false;
        if (state.pendingAutosaveByGroup[group.groupKey]) {
            state.pendingAutosaveByGroup[group.groupKey] = false;
            await persistCurrentGroup({ silent: true });
        }
    }
}

async function saveGroup(event) {
    event.preventDefault();
    const currentGroupKey = state.selectedGroupKey;
    const saved = await persistCurrentGroup({ statusMessage: 'Saving group…', successMessage: 'Group saved.' });
    if (!saved || !currentGroupKey) {
        return;
    }

    const nextGroupKey = getNextPendingGroupKey(currentGroupKey);
    if (nextGroupKey && nextGroupKey !== currentGroupKey) {
        state.selectedGroupKey = nextGroupKey;
        const nextGroup = activeGroup();
        state.activeImageId = getDefaultImageId(nextGroup);
        state.hoverPreview = null;
        renderQueue();
        renderWorkspace();

        const field = document.getElementById('totalPrice');
        if (field instanceof HTMLInputElement || field instanceof HTMLTextAreaElement || field instanceof HTMLSelectElement) {
            field.focus();
            if (field instanceof HTMLInputElement) {
                field.select();
            }
        }
    } else {
        renderQueue();
        renderWorkspace();
    }
}

async function autosaveCurrentField() {
    const group = activeGroup();
    if (!group) {
        return;
    }

    const baseline = getGroupFormState(group);
    const draft = captureFormDraft();
    if (!draft) {
        return;
    }

    if (serializeFormState(draft) === serializeFormState(baseline)) {
        return;
    }

    await persistCurrentGroup({ silent: true });
}

async function splitSelectedImages() {
    const group = activeGroup();
    if (!group) {
        renderStatus('Select a group first.');
        return;
    }

    const imageIds = getSplitSelection(group.groupKey);
    if (imageIds.length === 0) {
        renderStatus('Select one or more thumbnails to split out.');
        return;
    }

    await persistCurrentGroup({ silent: true, markReviewed: false });
    renderStatus('Splitting selected images…');

    try {
        const response = await fetch('/api/manual/groups/split', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ groupKey: group.groupKey, imageIds })
        });

        if (!response.ok) {
            const payload = await response.json().catch(() => null);
            throw new Error(payload?.message || `HTTP ${response.status}`);
        }

        const payload = await response.json();
        delete state.drafts[group.groupKey];
        delete state.splitSelections[group.groupKey];
        state.hoverPreview = null;
        await loadData(payload.groupKey, imageIds[0]);
        renderStatus('Created a new group from the selected images.');
    } catch (error) {
        console.error(error);
        renderStatus(error.message || 'Unable to split the selected images.');
    }
}

async function mergeIntoSelected(sourceGroupKey) {
    const targetGroupKey = state.selectedGroupKey;
    if (!sourceGroupKey || !targetGroupKey) {
        renderStatus('Select a target group first.');
        return;
    }

    await persistCurrentGroup({ silent: true });
    renderStatus('Merging groups…');

    try {
        const response = await fetch('/api/manual/groups/merge', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ sourceGroupKey, targetGroupKey })
        });

        if (!response.ok) {
            const payload = await response.json().catch(() => null);
            throw new Error(payload?.message || `HTTP ${response.status}`);
        }

        const payload = await response.json();
        delete state.drafts[sourceGroupKey];
        state.hoverPreview = null;
        await loadData(payload.groupKey || targetGroupKey, state.activeImageId);
        renderStatus('Groups merged.');
    } catch (error) {
        console.error(error);
        renderStatus(error.message || 'Unable to merge groups.');
    }
}

entryForm.addEventListener('submit', saveGroup);
entryForm.querySelectorAll('input, select, textarea').forEach((field) => {
    if (field.id === 'volumeUnit') {
        field.dataset.previousUnit = field.value;
    }

    field.addEventListener('input', () => {
        if (field.id === 'volumeUnit') {
            convertVolumeOnUnitChange(field);
        }

        captureFormDraft();
        if (field.id === 'volume' || field.id === 'volumeUnit' || field.id === 'totalPrice') {
            renderPricePerGallon();
        }

        if (field.id === 'volume' || field.id === 'volumeUnit' || field.id === 'odometer' || field.id === 'vehicleId') {
            validateCurrentGroup();
        }
    });

    field.addEventListener('blur', async () => {
        if ((field.id === 'volume' || field.id === 'totalPrice') && field instanceof HTMLInputElement) {
            unifyMathInputValue(field);
        }

        if (field.id === 'volume' || field.id === 'volumeUnit' || field.id === 'odometer' || field.id === 'vehicleId') {
            await validateCurrentGroup();
        }
        await autosaveCurrentField();
    });
});

function convertVolumeOnUnitChange(select) {
    const newUnit = select.value;
    const previousUnit = select.dataset.previousUnit || 'gallons';
    if (newUnit === previousUnit) {
        return;
    }

    const volumeInput = document.getElementById('volume');
    if (volumeInput instanceof HTMLInputElement) {
        const value = numberOrNull(volumeInput.value);
        if (value != null) {
            volumeInput.value = newUnit === 'liters' ? gallonsToLiters(value) : litersToGallons(value);
        }
    }

    select.dataset.previousUnit = newUnit;
}

const odometerField = document.getElementById('odometer');
if (odometerField instanceof HTMLInputElement) {
    odometerField.addEventListener('focus', () => {
        selectLastImageForOdometerFocus();
    });
}

if (scanButton) {
    scanButton.addEventListener('click', async () => {
        scanButton.disabled = true;
        renderStatus('Scanning photo folder for new images…');
        try {
            const response = await fetch('/api/import/scan', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({})
            });

            const result = await response.json();
            if (!response.ok) {
                throw new Error(result?.errorMessage || result?.message || `HTTP ${response.status}`);
            }

            renderStatus(`Scan complete: ${result.discoveredCount} files found (${result.newImagesCount} new, ${result.existingImagesCount} existing).`);
            await loadData(state.selectedGroupKey, state.activeImageId);
        } catch (error) {
            console.error(error);
            renderStatus(`Scan failed: ${error.message}`);
        } finally {
            scanButton.disabled = false;
        }
    });
}

refreshButton.addEventListener('click', async () => {
    captureFormDraft();
    await loadData(state.selectedGroupKey, state.activeImageId);
});

if (autoDetectButton) {
    autoDetectButton.addEventListener('click', async () => {
        const eligibleCount = state.groups.filter((group) => group.fuelEventId == null).length;
        if (eligibleCount === 0) {
            renderStatus('No pending groups to auto detect.');
            return;
        }

        const confirmed = window.confirm(`Send ${eligibleCount} pending group${eligibleCount === 1 ? '' : 's'} to the vision model? Use "Auto detect this group" to test a single group first.`);
        if (!confirmed) {
            renderStatus('Auto detect cancelled.');
            return;
        }

        const result = await runAutoDetect({}, autoDetectButton, 'Auto detecting values from photos…');
        if (!result) {
            return;
        }

        state.queueFilter = 'auto';
        updateQueueFilterButtons();
        updateAutoSnapshot();
        renderQueue();
        const splitNote = result.groupsSplit > 0 ? `, ${result.groupsSplit} split` : '';
        renderStatus(`Auto detect complete: ${result.groupsDetected} of ${result.groupsExamined} group(s) detected${splitNote}, ${result.groupsFailed} skipped.`);
    });
}

if (autoDetectGroupButton) {
    autoDetectGroupButton.addEventListener('click', () => {
        const group = activeGroup();
        if (!group) {
            renderStatus('Select a group first.');
            return;
        }

        enqueueAutoDetect(group.groupKey);
    });
}

function getAutoDetectStatus(groupKey) {
    return groupKey ? state.autoDetectStatusByGroup[groupKey] ?? null : null;
}

function enqueueAutoDetect(groupKey) {
    if (!groupKey || getAutoDetectStatus(groupKey)) {
        renderStatus('That group is already in the auto detect queue.');
        return;
    }

    captureFormDraft();
    state.autoDetectStatusByGroup[groupKey] = 'queued';
    state.autoDetectQueue.push(groupKey);

    renderQueue();
    updateAutoDetectGroupButton();
    renderStatus(state.autoDetectQueue.length > 1 || state.autoDetectWorkerActive
        ? `Queued for auto detect (${state.autoDetectQueue.length} waiting).`
        : 'Auto detecting this group…');

    void processAutoDetectQueue();
}

async function processAutoDetectQueue() {
    if (state.autoDetectWorkerActive) {
        return;
    }

    state.autoDetectWorkerActive = true;

    try {
        while (state.autoDetectQueue.length > 0) {
            const groupKey = state.autoDetectQueue.shift();
            state.autoDetectStatusByGroup[groupKey] = 'running';
            renderQueue();
            updateAutoDetectGroupButton();

            try {
                await detectSingleGroup(groupKey);
            } finally {
                delete state.autoDetectStatusByGroup[groupKey];
                renderQueue();
                updateAutoDetectGroupButton();
            }
        }
    } finally {
        state.autoDetectWorkerActive = false;
    }
}

async function detectSingleGroup(groupKey) {
    try {
        const response = await fetch('/api/autodetect/run', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ groupKey, redetectExisting: true })
        });

        const result = await response.json();
        if (!response.ok) {
            throw new Error(result?.errorMessage || result?.message || `HTTP ${response.status}`);
        }

        delete state.drafts[groupKey];
        await refreshGroupsQuietly(groupKey);

        const detail = result.groups?.[0];
        renderStatus(detail?.message
            ? `Auto detect: ${detail.message}`
            : 'Auto detect found nothing to update for that group.');
    } catch (error) {
        console.error(error);
        renderStatus(`Auto detect failed: ${error.message}`);
    }
}

/// Refreshes queue data without stealing the selection or clobbering a form the user is filling in.
async function refreshGroupsQuietly(detectedGroupKey) {
    const response = await fetch('/api/manual/groups');
    if (!response.ok) {
        return;
    }

    const groups = await response.json();
    const selectedGroupKey = state.selectedGroupKey;
    state.groups = groups;

    groups
        .filter((group) => isAwaitingApproval(group))
        .forEach((group) => state.autoSnapshotGroupKeys.add(group.groupKey));

    renderQueue();

    if (detectedGroupKey === selectedGroupKey) {
        const stillPresent = groups.some((group) => group.groupKey === selectedGroupKey);
        if (!stillPresent) {
            state.selectedGroupKey = getVisibleGroups()[0]?.groupKey ?? groups[0]?.groupKey ?? null;
            state.activeImageId = getDefaultImageId(activeGroup());
        }

        renderWorkspace();
    }
}

function updateAutoDetectGroupButton() {
    if (!(autoDetectGroupButton instanceof HTMLButtonElement)) {
        return;
    }

    const status = getAutoDetectStatus(state.selectedGroupKey);
    if (status === 'running') {
        autoDetectGroupButton.disabled = true;
        autoDetectGroupButton.setAttribute('aria-busy', 'true');
        autoDetectGroupButton.innerHTML = '<span class="spinner"></span>Detecting…';
        return;
    }

    if (status === 'queued') {
        autoDetectGroupButton.disabled = true;
        autoDetectGroupButton.setAttribute('aria-busy', 'true');
        autoDetectGroupButton.innerHTML = '<span class="spinner"></span>Queued…';
        return;
    }

    autoDetectGroupButton.disabled = false;
    autoDetectGroupButton.removeAttribute('aria-busy');
    autoDetectGroupButton.textContent = 'Auto detect this group';
}

function setButtonBusy(button, busyLabel) {
    if (!(button instanceof HTMLButtonElement)) {
        return () => { };
    }

    const originalHtml = button.innerHTML;
    button.disabled = true;
    button.setAttribute('aria-busy', 'true');
    button.innerHTML = `<span class="spinner"></span>${escapeHtml(busyLabel)}`;

    return () => {
        button.disabled = false;
        button.removeAttribute('aria-busy');
        button.innerHTML = originalHtml;
    };
}

async function runAutoDetect(body, button, statusMessage, preferredGroupKey) {
    const restoreButton = setButtonBusy(button, 'Detecting…');

    captureFormDraft();
    renderStatus(statusMessage);

    try {
        const response = await fetch('/api/autodetect/run', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(body)
        });

        const result = await response.json();
        if (!response.ok) {
            throw new Error(result?.errorMessage || result?.message || `HTTP ${response.status}`);
        }

        const groupKey = preferredGroupKey ?? state.selectedGroupKey;
        delete state.drafts[groupKey];
        await loadData(groupKey, state.activeImageId);
        return result;
    } catch (error) {
        console.error(error);
        renderStatus(`Auto detect failed: ${error.message}`);
        return null;
    } finally {
        restoreButton();
        setAutoDetectEnabled(true);
    }
}

function updateQueueFilterButtons() {
    document.querySelectorAll('.queue-filter-btn').forEach((btn) => {
        if (btn instanceof HTMLElement) {
            const isIncompleteToggle = btn.dataset.filter === 'incomplete';
            btn.classList.toggle('active', isIncompleteToggle ? state.incompleteOnly : btn.dataset.filter === state.queueFilter);
        }
    });
}

function toggleDateFilter(dayKey) {
    state.dateFilter = state.dateFilter === dayKey ? null : dayKey;
    renderQueue();
    const visible = getVisibleGroups();
    if (state.selectedGroupKey && !visible.some((group) => group.groupKey === state.selectedGroupKey)) {
        state.selectedGroupKey = visible[0]?.groupKey ?? null;
        state.activeImageId = getDefaultImageId(activeGroup());
        renderWorkspace();
    }
}

function setQueueFilter(filter) {
    if (filter === 'incomplete') {
        state.incompleteOnly = !state.incompleteOnly;
    } else {
        state.queueFilter = filter;
    }

    state.dateFilter = null;
    updateQueueFilterButtons();
    if (state.queueFilter === 'pending') {
        updatePendingSnapshot();
    }

    if (state.queueFilter === 'auto') {
        updateAutoSnapshot();
    }
    renderQueue();
    const visible = getVisibleGroups();
    if (state.selectedGroupKey && !visible.some((group) => group.groupKey === state.selectedGroupKey)) {
        state.selectedGroupKey = visible[0]?.groupKey ?? null;
        state.activeImageId = getDefaultImageId(activeGroup());
        renderWorkspace();
    }
}

document.querySelectorAll('.queue-filter-btn').forEach((btn) => {
    btn.addEventListener('click', () => {
        if (btn instanceof HTMLElement && btn.dataset.filter) {
            setQueueFilter(btn.dataset.filter);
        }
    });
});

loadData();
