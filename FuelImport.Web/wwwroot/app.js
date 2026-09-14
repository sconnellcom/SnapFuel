const statusEl = document.getElementById('status');
const queueSummaryEl = document.getElementById('queueSummary');
const groupListEl = document.getElementById('groupList');
const viewerShellEl = document.getElementById('viewerShell');
const entryForm = document.getElementById('entryForm');
const vehicleSelect = document.getElementById('vehicleId');
const groupMetaEl = document.getElementById('groupMeta');
const pricePerGallonDisplayEl = document.getElementById('pricePerGallonDisplay');
const refreshButton = document.getElementById('refreshButton');

const state = {
    groups: [],
    vehicles: [],
    selectedGroupKey: null,
    activeImageId: null,
    hoverPreview: null,
    drafts: {},
    splitSelections: {},
    saveInFlightByGroup: {},
    pendingAutosaveByGroup: {}
};

function renderStatus(message) {
    statusEl.textContent = message;
}

function numberOrNull(value) {
    return value === '' ? null : Number(value);
}

function formatDate(value) {
    if (!value) return 'Unknown time';
    return new Date(value).toLocaleString();
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
    const minutes = Math.max(0, Math.round((end - start) / 60000));
    return `${minutes} min`;
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

function hasQueueGreenCheck(group) {
    const formState = getGroupFormState(group);
    const vehicle = findVehicleById(formState.vehicleId);
    const odometerRequired = !(vehicle?.noOdometer ?? false);

    if (formState.totalPrice == null || formState.gallons == null) {
        return false;
    }

    if (!odometerRequired) {
        return true;
    }

    return formState.odometer != null;
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

function getNextPendingGroupKey(currentGroupKey) {
    const currentIndex = state.groups.findIndex((group) => group.groupKey === currentGroupKey);
    if (currentIndex === -1) {
        return null;
    }

    for (let index = currentIndex + 1; index < state.groups.length; index++) {
        if (state.groups[index].fuelEventId == null) {
            return state.groups[index].groupKey;
        }
    }

    for (let index = 0; index < currentIndex; index++) {
        if (state.groups[index].fuelEventId == null) {
            return state.groups[index].groupKey;
        }
    }

    return currentGroupKey;
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

function getGroupFormState(group) {
    return getDraft(group.groupKey) ?? {
        fuelEventId: group.fuelEventId,
        imageIds: group.images.map((image) => image.sourceImageId),
        vehicleId: group.vehicleId ?? null,
        odometer: group.odometer ?? null,
        gallons: group.gallons ?? null,
        totalPrice: group.totalPrice ?? null,
        locationName: group.locationName ?? null,
        notes: group.notes ?? null,
        reviewerName: 'Manual UI'
    };
}

function serializeFormState(formState) {
    return JSON.stringify({
        fuelEventId: formState.fuelEventId ?? null,
        imageIds: [...(formState.imageIds ?? [])].sort((left, right) => left - right),
        vehicleId: formState.vehicleId ?? null,
        odometer: formState.odometer ?? null,
        gallons: formState.gallons ?? null,
        totalPrice: formState.totalPrice ?? null,
        locationName: formState.locationName ?? null,
        notes: formState.notes ?? null,
        reviewerName: formState.reviewerName ?? 'Manual UI'
    });
}

function captureFormDraft() {
    const group = activeGroup();
    if (!group) {
        return null;
    }

    const draft = {
        fuelEventId: group.fuelEventId,
        imageIds: group.images.map((image) => image.sourceImageId),
        vehicleId: numberOrNull(vehicleSelect.value),
        odometer: numberOrNull(document.getElementById('odometer').value),
        gallons: numberOrNull(document.getElementById('gallons').value),
        totalPrice: numberOrNull(document.getElementById('totalPrice').value),
        locationName: document.getElementById('locationName').value || null,
        notes: document.getElementById('notes').value || null,
        reviewerName: document.getElementById('reviewerName').value || 'Manual UI'
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
        document.getElementById('gallons'),
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

async function loadData(preferredGroupKey, preferredImageId) {
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
        state.selectedGroupKey = preferredGroupKey && state.groups.some((group) => group.groupKey === preferredGroupKey)
            ? preferredGroupKey
            : state.groups[0]?.groupKey ?? null;

        const selectedGroup = activeGroup();
        state.activeImageId = preferredImageId && selectedGroup?.images.some((image) => image.sourceImageId === preferredImageId)
            ? preferredImageId
            : getDefaultImageId(selectedGroup);
        state.hoverPreview = null;

        renderVehicleOptions();
        renderQueue();
        renderWorkspace();
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

function renderQueue() {
    const savedCount = state.groups.filter((group) => group.fuelEventId != null).length;
    queueSummaryEl.textContent = `${savedCount}/${state.groups.length} saved`;

    if (!state.groups.length) {
        groupListEl.innerHTML = '<div class="empty-state">No imported images found yet.</div>';
        return;
    }

    groupListEl.innerHTML = state.groups.map((group) => {
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

        return `
    <div class="queue-item ${group.groupKey === state.selectedGroupKey ? 'active' : ''}" data-group-key="${group.groupKey}">
      <div class="queue-title">
                <strong class="queue-title-main">${showGreenCheck ? '<span class="queue-check">✓</span>' : ''}<span>${escapeHtml(title)}</span></strong>
        <span class="badge ${group.fuelEventId ? 'badge-saved' : 'badge-pending'}">${group.fuelEventId ? 'Saved' : 'Pending'}</span>
      </div>
                        <div class="queue-meta">${formatDate(earliestPhotoTimestamp)}</div>
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

    groupListEl.querySelectorAll('[data-action="merge-into-selected"]').forEach((button) => {
        button.addEventListener('click', async (event) => {
            event.stopPropagation();
            await mergeIntoSelected(button.dataset.sourceGroupKey);
        });
    });
}

function renderWorkspace() {
    const group = activeGroup();
    if (!group) {
        viewerShellEl.innerHTML = '<div class="empty-state">Select a group after importing images.</div>';
        entryForm.reset();
        groupMetaEl.textContent = '';
        renderPricePerGallon();
        return;
    }

    const image = activeImage();
    if (image) {
        state.activeImageId = image.sourceImageId;
    }

    const orderedImages = getOrderedImages(group);

    const splitSelection = getSplitSelection(group.groupKey);

    viewerShellEl.innerHTML = `
    <div class="viewer-top">
      <div>
        <strong>${escapeHtml(group.locationName || 'Manual review group')}</strong>
        <div class="group-meta">${formatDate(group.startedAtUtc)}${group.endedAtUtc && group.endedAtUtc !== group.startedAtUtc ? ` to ${formatDate(group.endedAtUtc)}` : ''}</div>
        <div class="group-meta">${formatCoordinate(group.latitude, group.longitude)} · Spread ${formatDistance(group.maxDistanceKilometers)}</div>
      </div>
      <div class="viewer-actions">
        <div>
          <button id="splitSelectedButton" type="button" class="secondary">Split selected</button>
          <button id="clearSplitSelectionButton" type="button" class="secondary">Clear selection</button>
        </div>
      </div>
      <div class="thumb-strip">
                ${orderedImages.map((item) => `
          <div class="thumb-card ${item.sourceImageId === state.activeImageId ? 'active' : ''}" data-role="thumb-card" data-image-id="${item.sourceImageId}">
            <div class="thumb-actions">
              <label class="thumb-check">
                <input type="checkbox" data-role="split-checkbox" data-image-id="${item.sourceImageId}" ${splitSelection.includes(item.sourceImageId) ? 'checked' : ''} />
              </label>
            </div>
            <div class="thumb-image-frame" data-role="thumb-frame" data-image-id="${item.sourceImageId}">
              <img src="${item.imageUrl}" alt="${escapeHtml(item.fileName)}" data-role="thumb-image" data-image-id="${item.sourceImageId}" />
              <div class="thumb-focus" data-role="thumb-focus" data-image-id="${item.sourceImageId}"></div>
            </div>
            <div><span class="badge badge-type">${escapeHtml(item.imageTypeCandidate)}</span></div>
            <div class="image-meta">${escapeHtml(item.fileName)}</div>
            <div class="thumb-time">${formatDate(item.capturedAtUtc)}</div>
                        ${formatDistance(item.distanceFromPreviousKilometers) ? `<div class="thumb-time">${formatDistance(item.distanceFromPreviousKilometers)}</div>` : ''}
          </div>
        `).join('')}
      </div>
    </div>
    <div class="hero-image" id="heroImage">${image ? `<img id="heroImageTag" src="${image.imageUrl}" alt="${escapeHtml(image.fileName)}" /><div id="magnifier" class="magnifier"></div>` : '<div class="helper">No image selected.</div>'}</div>
    <div>
      <div class="image-meta">${image ? `${escapeHtml(image.fileName)} · ${escapeHtml(image.imageTypeCandidate)} · ${(image.imageTypeConfidence * 100).toFixed(0)}% heuristic confidence` : 'No image selected.'}</div>
      <div class="image-meta">Captured: ${image ? formatDate(image.capturedAtUtc) : 'Unknown time'}</div>
    </div>
  `;

    viewerShellEl.querySelectorAll('[data-role="thumb-card"]').forEach((card) => {
        card.addEventListener('click', () => {
            const focusState = captureFocusState();
            captureFormDraft();
            state.activeImageId = Number(card.dataset.imageId);
            state.hoverPreview = null;
            renderWorkspace();
            restoreFocusState(focusState);
        });
    });

    viewerShellEl.querySelectorAll('[data-role="thumb-frame"]').forEach((frame) => {
        frame.addEventListener('mousemove', handleThumbnailHover);
        frame.addEventListener('mouseenter', handleThumbnailHover);
        frame.addEventListener('mouseleave', () => {
            const focus = frame.querySelector('[data-role="thumb-focus"]');
            if (focus instanceof HTMLElement) {
                focus.classList.remove('visible');
            }

            state.hoverPreview = null;
            renderHeroPreview();
        });
    });

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
        });
    });

    document.getElementById('splitSelectedButton').addEventListener('click', splitSelectedImages);
    document.getElementById('clearSplitSelectionButton').addEventListener('click', () => {
        setSplitSelection(group.groupKey, []);
        renderWorkspace();
    });

    const formState = getGroupFormState(group);
    vehicleSelect.value = formState.vehicleId ?? '';
    document.getElementById('odometer').value = formState.odometer ?? '';
    document.getElementById('gallons').value = formState.gallons ?? '';
    document.getElementById('totalPrice').value = formState.totalPrice ?? '';
    document.getElementById('locationName').value = formState.locationName ?? '';
    document.getElementById('notes').value = formState.notes ?? '';
    document.getElementById('reviewerName').value = formState.reviewerName ?? 'Manual UI';
    groupMetaEl.textContent = group.fuelEventId ? `Editing event #${group.fuelEventId}` : `${group.images.length} linked image${group.images.length === 1 ? '' : 's'}`;
    renderPricePerGallon(group.pricePerGallon);
    renderHeroPreview();
}

function handleThumbnailHover(event) {
    const frame = event.currentTarget;
    if (!(frame instanceof HTMLElement)) {
        return;
    }

    const group = activeGroup();
    const imageId = Number(frame.dataset.imageId);
    const item = group?.images.find((entry) => entry.sourceImageId === imageId);
    const thumbImage = frame.querySelector('[data-role="thumb-image"]');
    const focus = frame.querySelector('[data-role="thumb-focus"]');
    const hero = document.getElementById('heroImage');
    if (!item || !(thumbImage instanceof HTMLImageElement) || !(focus instanceof HTMLElement) || !(hero instanceof HTMLElement)) {
        return;
    }

    const frameRect = frame.getBoundingClientRect();
    const imageRect = thumbImage.getBoundingClientRect();
    const pointerX = event.clientX;
    const pointerY = event.clientY;

    if (pointerX < imageRect.left || pointerX > imageRect.right || pointerY < imageRect.top || pointerY > imageRect.bottom) {
        focus.classList.remove('visible');
        state.hoverPreview = null;
        renderHeroPreview();
        return;
    }

    const relativeX = (pointerX - imageRect.left) / imageRect.width;
    const relativeY = (pointerY - imageRect.top) / imageRect.height;
    const focusSize = Math.min(52, imageRect.width, imageRect.height);
    const left = Math.min(
        Math.max(pointerX - frameRect.left - focusSize / 2, imageRect.left - frameRect.left),
        imageRect.right - frameRect.left - focusSize);
    const top = Math.min(
        Math.max(pointerY - frameRect.top - focusSize / 2, imageRect.top - frameRect.top),
        imageRect.bottom - frameRect.top - focusSize);

    focus.classList.add('visible');
    focus.style.width = `${focusSize}px`;
    focus.style.height = `${focusSize}px`;
    focus.style.left = `${left}px`;
    focus.style.top = `${top}px`;

    const zoom = 1.35;
    state.hoverPreview = {
        imageUrl: item.imageUrl,
        backgroundWidth: hero.clientWidth * zoom,
        backgroundHeight: hero.clientHeight * zoom,
        backgroundX: -(relativeX * hero.clientWidth * zoom - hero.clientWidth / 2),
        backgroundY: -(relativeY * hero.clientHeight * zoom - hero.clientHeight / 2)
    };

    renderHeroPreview();
}

function renderHeroPreview() {
    const image = document.getElementById('heroImageTag');
    const lens = document.getElementById('magnifier');
    if (!(image instanceof HTMLImageElement) || !(lens instanceof HTMLElement)) {
        return;
    }

    if (!state.hoverPreview) {
        image.style.opacity = '1';
        lens.classList.remove('visible');
        lens.style.backgroundImage = 'none';
        return;
    }

    image.style.opacity = '0';
    lens.classList.add('visible');
    lens.style.backgroundImage = `url("${state.hoverPreview.imageUrl}")`;
    lens.style.backgroundSize = `${state.hoverPreview.backgroundWidth}px ${state.hoverPreview.backgroundHeight}px`;
    lens.style.backgroundPosition = `${state.hoverPreview.backgroundX}px ${state.hoverPreview.backgroundY}px`;
}

function renderPricePerGallon(existingValue) {
    const gallons = numberOrNull(document.getElementById('gallons').value);
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
    group.totalPrice = payload.totalPrice;
    group.locationName = payload.locationName;
    group.notes = payload.notes;
    group.pricePerGallon = savedEvent?.pricePerGallon ?? (payload.gallons && payload.totalPrice ? payload.totalPrice / payload.gallons : null);
}

async function persistCurrentGroup(options = {}) {
    const group = activeGroup();
    if (!group) {
        return false;
    }

    const payload = captureFormDraft();
    if (!payload) {
        return false;
    }

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
            body: JSON.stringify(payload)
        });

        if (!response.ok) {
            const errorPayload = await response.json().catch(() => null);
            throw new Error(errorPayload?.message || `HTTP ${response.status}`);
        }

        const savedEvent = await response.json();
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
    await loadData(nextGroupKey, null);
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

    await persistCurrentGroup({ silent: true });
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
    field.addEventListener('input', () => {
        captureFormDraft();
        if (field.id === 'gallons' || field.id === 'totalPrice') {
            renderPricePerGallon();
        }
    });

    field.addEventListener('blur', async () => {
        await autosaveCurrentField();
    });
});

const odometerField = document.getElementById('odometer');
if (odometerField instanceof HTMLInputElement) {
    odometerField.addEventListener('focus', () => {
        selectLastImageForOdometerFocus();
    });
}

refreshButton.addEventListener('click', async () => {
    captureFormDraft();
    await loadData(state.selectedGroupKey, state.activeImageId);
});

loadData();
