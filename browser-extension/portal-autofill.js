const PORTAL_ORIGIN = 'https://eservices.mutakamela.sa';
const COMPLAINT_ORIGIN = 'https://mutakamela.sa';
const COMPLAINT_PATH = '/submit-your-complaints/';
const CLAIM_CENTER_PATH = '/claim-center/';
let activePayload = null;
let autofillObserver = null;
const filledIds = new Set();
const reportedIds = new Set();
const complaintFilesByJob = new Map();
let complaintProductsPromise = null;
let autofillRunning = false;
let autofillAgain = false;
let autofillScheduleTimer = null;
let complaintProductVerificationTimer = null;
let complaintSubmissionObserver = null;
let complaintSubmissionJobId = '';
let complaintSubmissionNotificationInFlight = false;
let complaintSubmissionCheckTimer = null;
const DOCUMENT_TYPES = {
    pdf: 'application/pdf',
    jpg: 'image/jpeg',
    jpeg: 'image/jpeg',
    doc: 'application/msword',
    docx: 'application/vnd.openxmlformats-officedocument.wordprocessingml.document'
};

function normalizeText(value) {
    return String(value || '').replace(/\s+/g, ' ').trim().toLocaleLowerCase();
}

function scheduleAutofill() {
    if (autofillScheduleTimer !== null) return;
    autofillScheduleTimer = window.setTimeout(() => {
        autofillScheduleTimer = null;
        void attemptAutofill();
    }, 200);
}

function isVisible(element) {
    return element instanceof HTMLElement &&
        element.getClientRects().length > 0 &&
        !element.matches(':disabled,[readonly]');
}

function controlsForLabel(selector) {
    const text = selector.slice('label='.length).replace(/^["']|["']$/g, '');
    const wanted = normalizeText(text);
    return Array.from(document.querySelectorAll('label'))
        .filter(label => normalizeText(label.textContent) === wanted)
        .flatMap(label => {
            const control = label.control || label.querySelector('input,select,textarea');
            return control ? [control] : [];
        });
}

function selectorCandidates(field) {
    const candidates = [];
    for (const selector of field.selectors || []) {
        if (selector.startsWith('label=')) {
            candidates.push(...controlsForLabel(selector));
            continue;
        }
        if (/^(input|select|textarea)(\[|\.|#|$)/i.test(selector) ||
            selector.startsWith('[data-') || selector.startsWith('#')) {
            try {
                candidates.push(...document.querySelectorAll(selector));
            } catch {
                // Ignore invalid portal CSS selectors; later verified selectors may work.
            }
        }
    }
    return [...new Set(candidates.filter(element =>
        isVisible(element) ||
        (field.type === 'select' && element instanceof HTMLSelectElement && element.hidden)))];
}

function inputValueEquals(input, value) {
    return normalizeText(input.value) === normalizeText(value);
}

function setControlValue(control, value) {
    const prototype = control instanceof HTMLTextAreaElement
        ? HTMLTextAreaElement.prototype
        : control instanceof HTMLSelectElement
            ? HTMLSelectElement.prototype
            : HTMLInputElement.prototype;
    const setter = Object.getOwnPropertyDescriptor(prototype, 'value')?.set;
    if (!setter) return false;
    setter.call(control, value);
    control.dispatchEvent(new Event('input', { bubbles: true }));
    control.dispatchEvent(new Event('change', { bubbles: true }));
    return true;
}

function selectOption(control, value) {
    const matchesValue = item => normalizeText(item.value) === normalizeText(value);
    const matchesLabel = item => {
        const segments = normalizeText(item.textContent).split(/\s+[–—-]\s+/);
        return segments.includes(normalizeText(value));
    };
    const option = Array.from(control.options).find(item =>
        matchesValue(item) ||
        normalizeText(item.textContent) === normalizeText(value) ||
        matchesLabel(item));
    if (!option || !option.value) return false;

    const choicesInstance = control._mutakamelaChoices;
    if (choicesInstance && typeof choicesInstance.setChoiceByValue === 'function') {
        choicesInstance.setChoiceByValue(option.value);
        const visibleItem = control.closest('.choices')
            ?.querySelector('.choices__list--single .choices__item--selectable');
        return control.value === option.value &&
            visibleItem instanceof HTMLElement &&
            normalizeText(visibleItem.textContent) === normalizeText(option.textContent);
    }

    if (control.value === option.value) return true;
    if (control.hidden || !isVisible(control)) return false;
    setControlValue(control, option.value);
    return control.value === option.value;
}

function complaintProductMatches(control, value) {
    if (!(control instanceof HTMLSelectElement) || !value) return false;
    const selected = control.selectedOptions[0];
    if (!selected?.value) return false;
    const wanted = normalizeText(value);
    const labels = normalizeText(selected.textContent).split(/\s+[–—-]\s+/);
    const valueMatches = normalizeText(selected.value) === wanted || labels.includes(wanted) ||
        normalizeText(selected.textContent) === wanted;
    if (!valueMatches) return false;

    const choicesInstance = control._mutakamelaChoices;
    if (!choicesInstance) return true;
    const visibleItem = control.closest('.choices')
        ?.querySelector('.choices__list--single .choices__item--selectable');
    return visibleItem instanceof HTMLElement &&
        normalizeText(visibleItem.textContent) === normalizeText(selected.textContent);
}

function verifyComplaintProductSelection(field, value) {
    if (complaintProductVerificationTimer) {
        clearInterval(complaintProductVerificationTimer);
        complaintProductVerificationTimer = null;
    }

    let attempts = 0;
    complaintProductVerificationTimer = setInterval(() => {
        const control = selectorCandidates(field)[0];
        if (complaintProductMatches(control, value)) {
            if (++attempts >= 20) {
                clearInterval(complaintProductVerificationTimer);
                complaintProductVerificationTimer = null;
            }
            return;
        }

        attempts++;
        if (attempts >= 20) {
            clearInterval(complaintProductVerificationTimer);
            complaintProductVerificationTimer = null;
            showProductSelectionFailure('The portal reset the selected product. Please select it on the form.');
            return;
        }

        filledIds.delete(field.id);
        activePayload.completedIds = activePayload.completedIds.filter(id => id !== field.id);
        void attemptAutofill();
    }, 750);
}

async function selectComplaintProduct(control, value) {
    if (!(control instanceof HTMLSelectElement) || !value) return false;
    if (selectOption(control, value)) return true;

    complaintProductsPromise ??= chrome.runtime.sendMessage({
        type: 'GET_PORTAL_COMPLAINT_PRODUCTS'
    }).then(result => {
        if (result?.error || !Array.isArray(result?.products)) {
            throw new Error(result?.error || 'Mutakamela returned an invalid product list.');
        }
        return result.products;
    }).catch(error => {
        complaintProductsPromise = null;
        throw error;
    });

    let products;
    try {
        products = await complaintProductsPromise;
    } catch (error) {
        console.error('Could not load complaint product choices:', error);
        showProductSelectionFailure('Could not load the product choices. Select the product manually.');
        return false;
    }

    const wanted = normalizeText(value);
    const product = products.find(item =>
        [item.id, item.name, item.code, `${item.code} - ${item.name}`,
            `${item.name} (${item.code})`, `${item.name} - ${item.code}`]
            .some(candidate => normalizeText(candidate) === wanted));
    if (!product) {
        showProductSelectionFailure('The requested product was not found. Select it manually.');
        return false;
    }

    const choices = products.map(item => ({
        value: item.id,
        label: `${item.code} - ${item.name}`
    }));
    const choicesInstance = control._mutakamelaChoices;
    if (choicesInstance && typeof choicesInstance.setChoices === 'function' &&
        typeof choicesInstance.setChoiceByValue === 'function') {
        choicesInstance.setChoices(choices, 'value', 'label', true);
        choicesInstance.setChoiceByValue(product.id);
    } else {
        for (const choice of choices) {
            if (!Array.from(control.options).some(option => option.value === choice.value)) {
                control.add(new Option(choice.label, choice.value));
            }
        }
        setControlValue(control, product.id);
    }

    const selected = control.value === product.id &&
        complaintProductMatches(control, value);
    if (selected) {
        document.getElementById('mutakamela-product-selection-warning')?.remove();
    } else {
        showProductSelectionFailure('The product could not be selected. Select it manually.');
    }
    return selected;
}

function showProductSelectionFailure(message) {
    let notice = document.getElementById('mutakamela-product-selection-warning');
    if (!notice) {
        notice = document.createElement('div');
        notice.id = 'mutakamela-product-selection-warning';
        Object.assign(notice.style, {
            position: 'fixed',
            zIndex: '2147483647',
            insetBlockStart: '12px',
            insetInlineStart: '12px',
            maxWidth: '360px',
            padding: '12px 16px',
            color: '#7a271a',
            background: '#fff',
            border: '1px solid #fda29b',
            borderRadius: '8px',
            boxShadow: '0 4px 16px rgba(0,0,0,.18)',
            font: '14px/1.4 sans-serif'
        });
        document.documentElement.appendChild(notice);
    }
    notice.textContent = message;
}

function applyField(field, value) {
    const candidates = selectorCandidates(field);
    for (const control of candidates) {
        if (control instanceof HTMLInputElement && ['password', 'file', 'hidden'].includes(control.type)) continue;

        if (control instanceof HTMLInputElement && control.type === 'radio') {
            const group = Array.from(document.querySelectorAll('input[type="radio"]'))
                .filter(item => item.name && item.name === control.name);
            const match = group.find(item => normalizeText(item.value) === normalizeText(value) ||
                normalizeText(item.labels?.[0]?.textContent) === normalizeText(value));
            if (!match) continue;
            if (match.checked) return true;
            match.click();
            return match.checked;
        }

        if (control instanceof HTMLInputElement && control.type === 'checkbox') {
            const wantsChecked = /^(true|yes|1|نعم)$/i.test(value.trim());
            const wantsUnchecked = /^(false|no|0|لا)$/i.test(value.trim());
            if (!wantsChecked && !wantsUnchecked) continue;
            if (control.checked !== wantsChecked) control.click();
            return control.checked === wantsChecked;
        }

        if (control instanceof HTMLSelectElement) {
            if (selectOption(control, value)) return true;
            continue;
        }

        if (control instanceof HTMLInputElement || control instanceof HTMLTextAreaElement) {
            if (control.value && !inputValueEquals(control, value)) return false;
            if (!inputValueEquals(control, value) && !setControlValue(control, value)) return false;
            return inputValueEquals(control, value);
        }
    }
    return false;
}

function showSummary(filledCount) {
    let notice = document.getElementById('mutakamela-autofill-notice');
    if (!notice) {
        notice = document.createElement('div');
        notice.id = 'mutakamela-autofill-notice';
        Object.assign(notice.style, {
            position: 'fixed',
            zIndex: '2147483647',
            insetBlockStart: '12px',
            insetInlineEnd: '12px',
            maxWidth: '360px',
            padding: '12px 16px',
            color: '#17324d',
            background: '#fff',
            border: '1px solid #8db6e3',
            borderRadius: '8px',
            boxShadow: '0 4px 16px rgba(0,0,0,.18)',
            font: '14px/1.4 sans-serif'
        });
        document.documentElement.appendChild(notice);
    }
    notice.textContent = `Mutakamela filled ${filledCount} field(s). Review the values before continuing; nothing was submitted.`;
}

function attachComplaintFile(jobId, attachment) {
    if (location.origin !== COMPLAINT_ORIGIN || location.pathname !== COMPLAINT_PATH ||
        typeof jobId !== 'string' || !attachment || typeof attachment !== 'object') {
        return { attached: false, error: 'Attachments are only supported on the official complaint form.' };
    }
    const extension = typeof attachment.name === 'string' ? attachment.name.split('.').pop()?.toLowerCase() : '';
    const type = DOCUMENT_TYPES[extension];
    if (!type || !/^[a-zA-Z0-9-]{1,80}$/.test(attachment.id || '') ||
        !Number.isInteger(attachment.size) || attachment.size < 0 || attachment.size > 2 * 1024 * 1024 ||
        typeof attachment.contentBase64 !== 'string' ||
        !/^(?:[A-Za-z0-9+/]{4})*(?:[A-Za-z0-9+/]{2}==|[A-Za-z0-9+/]{3}=)?$/.test(attachment.contentBase64)) {
        return { attached: false, error: 'The complaint document does not meet the accepted file rules.' };
    }

    let bytes;
    try {
        bytes = Uint8Array.from(atob(attachment.contentBase64), character => character.charCodeAt(0));
    } catch {
        return { attached: false, error: 'The complaint document could not be read.' };
    }
    if (bytes.byteLength !== attachment.size) {
        return { attached: false, error: 'The complaint document size could not be verified.' };
    }
    const input = document.querySelector('#form-field-field_6d72756');
    if (!(input instanceof HTMLInputElement) || input.type !== 'file' || !input.multiple) {
        return { attached: false, error: 'The verified multi-file control was not found on the complaint form.' };
    }

    let files = complaintFilesByJob.get(jobId);
    if (!files) {
        files = new Map();
        complaintFilesByJob.set(jobId, files);
    }
    if (!files.has(attachment.id)) {
        files.set(attachment.id, new File([bytes], attachment.name, { type }));
    }
    const transfer = new DataTransfer();
    const trackedFiles = new Set(files.values());
    for (const existing of input.files) {
        if (!trackedFiles.has(existing)) transfer.items.add(existing);
    }
    for (const file of files.values()) transfer.items.add(file);
    try {
        input.files = transfer.files;
    } catch {
        return { attached: false, error: 'The browser could not add files to the complaint form.' };
    }
    input.dispatchEvent(new Event('input', { bubbles: true }));
    input.dispatchEvent(new Event('change', { bubbles: true }));

    const attachedNames = new Set(Array.from(input.files, file => `${file.name}:${file.size}`));
    const allAttached = [...files.values()].every(file => attachedNames.has(`${file.name}:${file.size}`));
    if (!allAttached) return { attached: false, error: 'The complaint form did not accept all selected documents.' };
    showAttachmentSummary(files.size);
    return { attached: true, count: files.size };
}

function showAttachmentSummary(count) {
    let notice = document.getElementById('mutakamela-attachment-notice');
    if (!notice) {
        notice = document.createElement('div');
        notice.id = 'mutakamela-attachment-notice';
        Object.assign(notice.style, {
            position: 'fixed',
            zIndex: '2147483647',
            insetBlockStart: '76px',
            insetInlineEnd: '12px',
            maxWidth: '360px',
            padding: '12px 16px',
            color: '#17324d',
            background: '#fff',
            border: '1px solid #8db6e3',
            borderRadius: '8px',
            boxShadow: '0 4px 16px rgba(0,0,0,.18)',
            font: '14px/1.4 sans-serif'
        });
        document.documentElement.appendChild(notice);
    }
    notice.textContent = `Attached ${count} document(s) to the complaint form. Review the files and form; nothing was submitted.`;
}

function startComplaintSubmissionWatcher(jobId) {
    if (typeof jobId !== 'string' || !/^[a-zA-Z0-9-]{1,80}$/.test(jobId) ||
        complaintSubmissionObserver) return;
    complaintSubmissionJobId = jobId;

    const scheduleConfirmationCheck = () => {
        if (complaintSubmissionCheckTimer !== null) return;
        complaintSubmissionCheckTimer = window.setTimeout(() => {
            complaintSubmissionCheckTimer = null;
            checkForConfirmation();
        }, 300);
    };

    const checkForConfirmation = () => {
        if (!complaintSubmissionJobId || complaintSubmissionNotificationInFlight || !document.body) return;
        const text = document.body.textContent || '';
        const confirmed = /your complaint has been successfully received|complaint (?:has been )?successfully submitted|تم استلام شكواك بنجاح|تم تقديم الشكوى بنجاح/i.test(text);
        if (!confirmed) return;

        const reference = text.match(/\bcomplaint\s*(?:number|no\.?)\s*[:#]?\s*([A-Z0-9-]{4,40})\b/i) ||
            text.match(/رقم\s*الشكوى\s*[:：#]?\s*([A-Z0-9-]{4,40})/i);
        complaintSubmissionNotificationInFlight = true;
        chrome.runtime.sendMessage({
            type: 'COMPLAINT_SUBMISSION_CONFIRMED',
            jobId: complaintSubmissionJobId,
            complaintNumber: reference?.[1] || ''
        }).then(result => {
            if (result?.confirmed) {
                complaintSubmissionJobId = '';
                complaintSubmissionObserver?.disconnect();
                complaintSubmissionObserver = null;
                if (complaintSubmissionCheckTimer !== null) {
                    window.clearTimeout(complaintSubmissionCheckTimer);
                    complaintSubmissionCheckTimer = null;
                }
                return;
            }
            complaintSubmissionNotificationInFlight = false;
        }).catch(error => {
            complaintSubmissionNotificationInFlight = false;
            console.error('Could not send the complaint submission confirmation:', error);
        });
    };

    complaintSubmissionObserver = new MutationObserver(scheduleConfirmationCheck);
    complaintSubmissionObserver.observe(document.body, {
        childList: true,
        subtree: true,
        characterData: true
    });
    checkForConfirmation();
}

async function initializeComplaintSubmissionWatcher() {
    if (location.origin !== COMPLAINT_ORIGIN || location.pathname !== COMPLAINT_PATH) return;
    for (let attempt = 0; attempt < 20; attempt++) {
        try {
            const handoff = await chrome.runtime.sendMessage({ type: 'COMPLAINT_FORM_READY' });
            if (handoff?.jobId) {
                startComplaintSubmissionWatcher(handoff.jobId);
                return;
            }
            if (handoff?.error) {
                console.error('Could not initialize complaint confirmation tracking:', handoff.error);
                return;
            }
        } catch (error) {
            if (attempt === 19) {
                console.error('Could not initialize complaint confirmation tracking:', error);
                return;
            }
        }
        await new Promise(resolve => setTimeout(resolve, 500));
    }
}

async function reportResults() {
    const newIds = [...filledIds].filter(id => !reportedIds.has(id));
    if (!newIds.length) return;
    try {
        const result = await chrome.runtime.sendMessage({
            type: 'AUTOFILL_RESULT',
            jobId: activePayload.jobId,
            filledIds: newIds
        });
        if (result?.accepted) {
            newIds.forEach(id => reportedIds.add(id));
            showSummary(filledIds.size);
        }
    } catch (error) {
        console.error('Could not report portal autofill results:', error);
    }
}

async function attemptAutofill() {
    if (!activePayload) return;
    const complaintPage = activePayload.flowId === 'submit-complaint' &&
        location.origin === COMPLAINT_ORIGIN && location.pathname === COMPLAINT_PATH;
    const claimTrackingPage = activePayload.flowId === 'track-a-claim' &&
        location.origin === COMPLAINT_ORIGIN && location.pathname === CLAIM_CENTER_PATH;
    if (!complaintPage && !claimTrackingPage && location.origin !== PORTAL_ORIGIN) return;
    if (autofillRunning) {
        autofillAgain = true;
        return;
    }
    autofillRunning = true;
    try {
        for (const field of activePayload.fields) {
            const value = activePayload.values[field.id];
            const isComplaintProduct = complaintPage && field.id.toLocaleLowerCase() === 'product';
            if (filledIds.has(field.id) || activePayload.completedIds.includes(field.id)) {
                if (!isComplaintProduct || complaintProductMatches(selectorCandidates(field)[0], value)) continue;
                filledIds.delete(field.id);
                activePayload.completedIds = activePayload.completedIds.filter(id => id !== field.id);
            }
            if (complaintPage && field.id.toLocaleLowerCase() === 'product' &&
                (typeof value !== 'string' || !value.trim())) {
                showProductSelectionFailure('Product was not included in the complaint details.');
                continue;
            }
            const filled = isComplaintProduct
                ? await selectComplaintProduct(selectorCandidates(field)[0], value)
                : applyField(field, value);
            if (filled) {
                filledIds.add(field.id);
                if (isComplaintProduct) verifyComplaintProductSelection(field, value);
            }
        }
    } finally {
        autofillRunning = false;
        if (autofillAgain) {
            autofillAgain = false;
            void attemptAutofill();
        }
        void reportResults();
    }
}

async function loadPendingAutofill() {
    try {
        const pending = await chrome.runtime.sendMessage({ type: 'GET_PENDING_AUTOFILL' });
        if (!pending || pending.error || !Array.isArray(pending.fields) || !pending.values) return;
        activePayload = pending;
        pending.completedIds.forEach(id => filledIds.add(id));
        void attemptAutofill();
        if (!autofillObserver) {
            autofillObserver = new MutationObserver(scheduleAutofill);
            autofillObserver.observe(document.documentElement, { childList: true, subtree: true });
        }
    } catch (error) {
        console.error('Could not retrieve pending portal autofill:', error);
    }
}

chrome.runtime.onMessage.addListener((message, sender, sendResponse) => {
    if (message?.type === 'MUTAKAMELA_CLEAR_ATTACHED_COMPLAINT_FILES') {
        complaintFilesByJob.delete(message.jobId);
        sendResponse({ cleared: true });
        return false;
    }
    if (message?.type === 'MUTAKAMELA_ATTACH_COMPLAINT_FILE') {
        sendResponse(attachComplaintFile(message.jobId, message.attachment));
        return false;
    }
    if (message?.type === 'MUTAKAMELA_AUTOFILL' && message.payload) {
        activePayload = message.payload;
        message.payload.completedIds?.forEach(id => filledIds.add(id));
        void attemptAutofill();
        if (!autofillObserver) {
            autofillObserver = new MutationObserver(scheduleAutofill);
            autofillObserver.observe(document.documentElement, { childList: true, subtree: true });
        }
    }
    return false;
});

void loadPendingAutofill();
void initializeComplaintSubmissionWatcher();
