const FLOW_URLS = {
    'buy-insurance': 'https://eservices.mutakamela.sa/myInsurance/buy-insurance',
    'buy-motor-insurance': 'https://eservices.mutakamela.sa/myInsurance/buy-Motorinsurance',
    'personal-info': 'https://eservices.mutakamela.sa/myInsurance/personalinfo',
    'make-a-claim': 'https://eservices.mutakamela.sa/myInsurance/make-a-claim',
    'track-a-claim': 'https://mutakamela.sa/claim-center/',
    'submit-complaint': 'https://mutakamela.sa/submit-your-complaints/'
};

const CHAT_ORIGINS = new Set([
    'http://localhost:5001',
    'http://127.0.0.1:5001'
]);
const PORTAL_ORIGIN = 'https://eservices.mutakamela.sa';
const COMPLAINT_ORIGIN = 'https://mutakamela.sa';
const COMPLAINT_PATH = '/submit-your-complaints/';
const CLAIM_CENTER_PATH = '/claim-center/';
const PENDING_PREFIX = 'portal-autofill:';
const COMPLAINT_JOB_PREFIX = 'complaint-attachments:';
const ATTACHMENT_ALARM_PREFIX = 'complaint-attachment-expiry:';
const COMPLAINT_HANDOFF_PREFIX = 'complaint-handoff:';
const COMPLAINT_HANDOFF_ALARM_PREFIX = 'complaint-handoff-expiry:';
const SUPPORTED_TYPES = new Set(['text', 'date', 'select', 'radio', 'checkbox']);
const PENDING_TTL_MS = 30 * 60 * 1000;
const ATTACHMENT_TTL_MS = 30 * 60 * 1000;
const DOCUMENT_TYPES = {
    pdf: 'application/pdf',
    jpg: 'image/jpeg',
    jpeg: 'image/jpeg',
    doc: 'application/msword',
    docx: 'application/vnd.openxmlformats-officedocument.wordprocessingml.document'
};
let attachmentDatabasePromise;

async function getComplaintProducts() {
    const response = await fetch(
        'https://mutakamela.sa/wp-admin/admin-ajax.php?action=get_insurance_products&Language=1&lang=en'
    );
    if (!response.ok) throw new Error(`Product list request failed (${response.status}).`);
    const result = await response.json();
    if (result?.success !== true || !Array.isArray(result.data)) {
        throw new Error('Mutakamela returned an invalid product list.');
    }
    return result.data
        .filter(product => product && Number.isSafeInteger(Number(product.ProductID)) &&
            Number(product.ProductID) > 0 &&
            typeof product.PolicyName === 'string' && product.PolicyName.trim() &&
            typeof product.Abbreviation === 'string' && product.Abbreviation.trim())
        .slice(0, 200)
        .map(product => ({
            id: String(product.ProductID),
            name: product.PolicyName.trim().slice(0, 200),
            code: product.Abbreviation.trim().slice(0, 30)
        }));
}

function attachmentDatabase() {
    if (!attachmentDatabasePromise) {
        attachmentDatabasePromise = new Promise((resolve, reject) => {
            const request = indexedDB.open('mutakamela-complaint-attachments', 1);
            request.onupgradeneeded = () => {
                const store = request.result.createObjectStore('files', { keyPath: 'id' });
                store.createIndex('jobId', 'jobId', { unique: false });
            };
            request.onsuccess = () => resolve(request.result);
            request.onerror = () => reject(request.error || new Error('Could not open local attachment storage.'));
        });
    }
    return attachmentDatabasePromise;
}

async function storeComplaintFile(file) {
    const db = await attachmentDatabase();
    await new Promise((resolve, reject) => {
        const transaction = db.transaction('files', 'readwrite');
        transaction.objectStore('files').put(file);
        transaction.oncomplete = resolve;
        transaction.onerror = () => reject(transaction.error || new Error('Could not store the attachment locally.'));
        transaction.onabort = () => reject(transaction.error || new Error('Attachment storage was interrupted.'));
    });
}

async function listComplaintFiles(jobId) {
    const db = await attachmentDatabase();
    return new Promise((resolve, reject) => {
        const request = db.transaction('files', 'readonly').objectStore('files')
            .index('jobId').getAll(jobId);
        request.onsuccess = () => resolve(request.result);
        request.onerror = () => reject(request.error || new Error('Could not read local complaint attachments.'));
    });
}

async function removeComplaintFiles(jobId) {
    const db = await attachmentDatabase();
    const files = await listComplaintFiles(jobId);
    if (!files.length) return;
    await new Promise((resolve, reject) => {
        const transaction = db.transaction('files', 'readwrite');
        const store = transaction.objectStore('files');
        files.forEach(file => store.delete(file.id));
        transaction.oncomplete = resolve;
        transaction.onerror = () => reject(transaction.error || new Error('Could not remove local complaint attachments.'));
        transaction.onabort = () => reject(transaction.error || new Error('Attachment cleanup was interrupted.'));
    });
}

async function clearComplaintJob(jobId) {
    await removeComplaintFiles(jobId);
    await chrome.storage.session.remove(`${COMPLAINT_JOB_PREFIX}${jobId}`);
    await chrome.alarms.clear(`${ATTACHMENT_ALARM_PREFIX}${jobId}`);
}

function bytesToBase64(buffer) {
    const bytes = new Uint8Array(buffer);
    let binary = '';
    const chunkSize = 0x8000;
    for (let offset = 0; offset < bytes.length; offset += chunkSize) {
        binary += String.fromCharCode(...bytes.subarray(offset, offset + chunkSize));
    }
    return btoa(binary);
}

async function clearAllComplaintFiles() {
    const db = await attachmentDatabase();
    await new Promise((resolve, reject) => {
        const transaction = db.transaction('files', 'readwrite');
        transaction.objectStore('files').clear();
        transaction.oncomplete = resolve;
        transaction.onerror = () => reject(transaction.error || new Error('Could not clear local complaint attachments.'));
        transaction.onabort = () => reject(transaction.error || new Error('Attachment cleanup was interrupted.'));
    });
}

function attachmentMetadata(message) {
    const attachment = message?.attachment;
    if (!attachment || typeof attachment !== 'object' ||
        typeof attachment.id !== 'string' || !/^[a-zA-Z0-9-]{1,80}$/.test(attachment.id) ||
        typeof attachment.name !== 'string' || attachment.name.length > 255 ||
        /[\\/\u0000-\u001f]/.test(attachment.name) ||
        typeof attachment.size !== 'number' || !Number.isInteger(attachment.size) ||
        attachment.size < 0 || attachment.size > 2 * 1024 * 1024 ||
        typeof attachment.contentBase64 !== 'string') return null;
    const extension = attachment.name.split('.').pop()?.toLowerCase();
    const type = DOCUMENT_TYPES[extension];
    if (!type || attachment.contentBase64.length > 2_800_000 ||
        !/^(?:[A-Za-z0-9+/]{4})*(?:[A-Za-z0-9+/]{2}==|[A-Za-z0-9+/]{3}=)?$/.test(attachment.contentBase64)) return null;
    let bytes;
    try {
        bytes = Uint8Array.from(atob(attachment.contentBase64), character => character.charCodeAt(0));
    } catch {
        return null;
    }
    if (bytes.byteLength !== attachment.size) return null;
    return {
        id: attachment.id,
        name: attachment.name,
        type,
        size: attachment.size,
        bytes: bytes.buffer
    };
}

function isTrustedChat(sender) {
    try {
        return sender.tab?.id !== undefined && sender.frameId === 0 &&
            sender.url && CHAT_ORIGINS.has(new URL(sender.url).origin);
    } catch {
        return false;
    }
}

function isPortalPage(sender) {
    try {
        if (!sender.url || sender.frameId !== 0) return false;
        const url = new URL(sender.url);
        return url.origin === PORTAL_ORIGIN ||
            (url.origin === COMPLAINT_ORIGIN &&
                [COMPLAINT_PATH, CLAIM_CENTER_PATH].includes(url.pathname));
    } catch {
        return false;
    }
}

function preparePending(payload) {
    if (!payload || typeof payload !== 'object' || payload.verified !== true) return null;
    if (typeof payload.jobId !== 'string' || !/^[a-zA-Z0-9-]{1,80}$/.test(payload.jobId)) return null;
    if (!Object.hasOwn(FLOW_URLS, payload.flowId)) return null;

    const data = payload.data && typeof payload.data === 'object' && !Array.isArray(payload.data)
        ? payload.data
        : {};
    const fieldsById = new Map((Array.isArray(payload.fields) ? payload.fields : [])
        .filter(field => field && typeof field.id === 'string' &&
            field.sensitive !== true && SUPPORTED_TYPES.has(field.type) &&
            Array.isArray(field.selectors))
        .map(field => [field.id.toLocaleLowerCase(), {
            id: field.id,
            label: String(field.label || '').slice(0, 200),
            labelAr: String(field.labelAr || '').slice(0, 200),
            type: field.type,
            selectors: field.selectors.filter(selector =>
                typeof selector === 'string' && selector.length <= 300).slice(0, 12)
        }]));
    const fields = [];
    const values = {};

    for (const [id, value] of Object.entries(data)) {
        const field = fieldsById.get(id.toLocaleLowerCase());
        if (!field || typeof value !== 'string' || !value.trim() || value.length > 1000) continue;
        if (values[field.id] !== undefined) continue;
        fields.push(field);
        values[field.id] = value;
    }
    if (!fields.length) return null;

    return {
        jobId: payload.jobId,
        flowId: payload.flowId,
        fields,
        values,
        completedIds: [],
        expiresAt: Date.now() + PENDING_TTL_MS
    };
}

async function sendPendingToTab(tabId) {
    const key = `${PENDING_PREFIX}${tabId}`;
    const stored = await chrome.storage.session.get(key);
    if (!stored[key]) return;
    if (stored[key].expiresAt <= Date.now()) {
        await chrome.storage.session.remove(key);
        return;
    }
    try {
        await chrome.tabs.sendMessage(tabId, {
            type: 'MUTAKAMELA_AUTOFILL',
            payload: stored[key]
        });
    } catch {
        // A navigation may have completed before the portal content script is ready.
        // The content script also requests pending data when it starts.
    }
}

async function waitForComplaintTab(tabId) {
    const deadline = Date.now() + 15000;
    while (Date.now() < deadline) {
        const tab = await chrome.tabs.get(tabId);
        if (typeof tab.url === 'string' && tab.url) {
            const url = new URL(tab.url);
            if (url.origin === COMPLAINT_ORIGIN && url.pathname === COMPLAINT_PATH) return;
            if (url.href !== 'about:blank') {
                throw new Error('Attachments can only be sent to the official Mutakamela complaint form.');
            }
        }
        await new Promise(resolve => setTimeout(resolve, 250));
    }
    throw new Error('The complaint form is still opening. Please choose the file again in a moment.');
}

chrome.runtime.onMessage.addListener((message, sender, sendResponse) => {
    if (message?.type === 'OPEN_PORTAL') {
        if (!isTrustedChat(sender) || !sender.tab?.id) {
            sendResponse({ opened: false, error: 'The chat page is not an allowed origin.' });
            return false;
        }
        void (async () => {
            const flowId = message.payload?.flowId;
            const url = FLOW_URLS[flowId];
            if (!url) {
                sendResponse({ opened: false, error: 'Unsupported portal journey.' });
                return;
            }
            if (flowId === 'submit-complaint' &&
                (typeof message.payload?.jobId !== 'string' ||
                    !/^[a-zA-Z0-9-]{1,80}$/.test(message.payload.jobId))) {
                sendResponse({ opened: false, error: 'The complaint journey could not be identified.' });
                return;
            }

            try {
                const tab = await chrome.tabs.create({ url, active: true });
                if (!tab.id) throw new Error('The browser did not return a portal tab id.');
                const pending = preparePending(message.payload);
                if (flowId === 'submit-complaint') {
                    const expiresAt = Date.now() + 24 * 60 * 60 * 1000;
                    await chrome.storage.session.set({
                        [`${COMPLAINT_HANDOFF_PREFIX}${message.payload.jobId}`]: {
                            jobId: message.payload.jobId,
                            portalTabId: tab.id,
                            chatTabId: sender.tab.id,
                            expiresAt,
                            confirmed: false
                        }
                    });
                    await chrome.alarms.create(
                        `${COMPLAINT_HANDOFF_ALARM_PREFIX}${message.payload.jobId}`,
                        { delayInMinutes: 24 * 60 }
                    );
                }
                if (pending) {
                    await chrome.storage.session.set({ [`${PENDING_PREFIX}${tab.id}`]: pending });
                    await sendPendingToTab(tab.id);
                    if (pending.flowId === 'submit-complaint') {
                        const expiresAt = Date.now() + ATTACHMENT_TTL_MS;
                        await chrome.storage.session.set({
                            [`${COMPLAINT_JOB_PREFIX}${pending.jobId}`]: {
                                tabId: tab.id,
                                chatTabId: sender.tab.id,
                                expiresAt
                            }
                        });
                        await chrome.alarms.create(`${ATTACHMENT_ALARM_PREFIX}${pending.jobId}`, { delayInMinutes: 30 });
                    }
                }
                sendResponse({
                    opened: true,
                    autofillReady: Boolean(pending),
                    jobId: message.payload.jobId
                });
            } catch (error) {
                sendResponse({
                    opened: false,
                    error: error instanceof Error ? error.message : 'Could not open the portal tab.'
                });
            }
        })();
        return true;
    }

    if (message?.type === 'STORE_COMPLAINT_FILE') {
        if (!isTrustedChat(sender)) {
            sendResponse({ stored: false, error: 'The chat page is not an allowed origin.' });
            return false;
        }
        void (async () => {
            const jobId = message.jobId;
            if (typeof jobId !== 'string') throw new Error('The complaint journey could not be identified.');
            const key = `${COMPLAINT_JOB_PREFIX}${jobId}`;
            const stored = await chrome.storage.session.get(key);
            const job = stored[key];
            if (!job || job.expiresAt <= Date.now()) throw new Error('The complaint attachment handoff has expired. Reopen the complaint form.');
            const file = attachmentMetadata(message);
            if (!file) throw new Error('Choose a PDF, JPG, JPEG, DOC, or DOCX file no larger than 2 MB.');
            await storeComplaintFile({ ...file, jobId, expiresAt: job.expiresAt });
            sendResponse({ stored: true, attachmentId: file.id });
        })().catch(error => sendResponse({
            stored: false,
            error: error instanceof Error ? error.message : 'Could not store the complaint file.'
        }));
        return true;
    }

    if (message?.type === 'GET_COMPLAINT_PRODUCTS') {
        if (!isTrustedChat(sender)) {
            sendResponse({ error: 'The chat page is not an allowed origin.' });
            return false;
        }
        void (async () => {
            const products = (await getComplaintProducts())
                .map(({ name, code }) => ({ name, code }));
            sendResponse({ products });
        })().catch(error => sendResponse({
            error: error instanceof Error ? error.message : 'Could not load Mutakamela products.'
        }));
        return true;
    }

    if (message?.type === 'GET_PORTAL_COMPLAINT_PRODUCTS') {
        if (!isPortalPage(sender) || !sender.url ||
            new URL(sender.url).origin !== COMPLAINT_ORIGIN ||
            new URL(sender.url).pathname !== COMPLAINT_PATH) {
            sendResponse({ error: 'Product choices are only available to the official complaint form.' });
            return false;
        }
        void getComplaintProducts()
            .then(products => sendResponse({ products }))
            .catch(error => sendResponse({
                error: error instanceof Error ? error.message : 'Could not load Mutakamela products.'
            }));
        return true;
    }

    if (message?.type === 'ATTACH_COMPLAINT_FILES') {
        if (!isTrustedChat(sender)) {
            sendResponse({ attached: false, error: 'The chat page is not an allowed origin.' });
            return false;
        }
        void (async () => {
            const jobId = message.jobId;
            if (typeof jobId !== 'string') throw new Error('The complaint journey could not be identified.');
            const key = `${COMPLAINT_JOB_PREFIX}${jobId}`;
            const stored = await chrome.storage.session.get(key);
            const job = stored[key];
            if (!job || job.expiresAt <= Date.now()) throw new Error('The complaint attachment handoff has expired. Reopen the complaint form.');
            await waitForComplaintTab(job.tabId);
            const files = await listComplaintFiles(jobId);
            if (!files.length) throw new Error('No complaint documents were selected.');
            for (const file of files) {
                if (file.expiresAt <= Date.now()) throw new Error('The complaint attachment handoff has expired.');
                let result;
                for (let attempt = 0; attempt < 8; attempt++) {
                    try {
                        result = await chrome.tabs.sendMessage(job.tabId, {
                            type: 'MUTAKAMELA_ATTACH_COMPLAINT_FILE',
                            jobId,
                            attachment: {
                                id: file.id,
                                name: file.name,
                                type: file.type,
                                size: file.size,
                                contentBase64: bytesToBase64(file.bytes)
                            }
                        });
                        if (result?.attached) break;
                    } catch {
                        // The portal content script may not be ready immediately after the tab opens.
                    }
                    await new Promise(resolve => setTimeout(resolve, 500));
                }
                if (!result?.attached) throw new Error(result?.error || `Could not attach ${file.name} to the complaint form.`);
            }
            try {
                await chrome.tabs.sendMessage(job.tabId, {
                    type: 'MUTAKAMELA_CLEAR_ATTACHED_COMPLAINT_FILES',
                    jobId
                });
            } catch (error) {
                console.warn('Could not clear the in-page complaint attachment cache:', error);
            }
            await clearComplaintJob(jobId);
            sendResponse({ attached: true, count: files.length });
        })().catch(error => sendResponse({
            attached: false,
            error: error instanceof Error ? error.message : 'Could not attach complaint documents.'
        }));
        return true;
    }

    if (message?.type === 'GET_PENDING_AUTOFILL') {
        if (!isPortalPage(sender) || !sender.tab?.id) {
            sendResponse(null);
            return false;
        }
        void chrome.storage.session.get(`${PENDING_PREFIX}${sender.tab.id}`)
            .then(async stored => {
                const key = `${PENDING_PREFIX}${sender.tab.id}`;
                const pending = stored[key];
                if (pending?.expiresAt <= Date.now()) {
                    await chrome.storage.session.remove(key);
                    sendResponse(null);
                    return;
                }
                sendResponse(pending || null);
            })
            .catch(error => sendResponse({ error: error.message }));
        return true;
    }

    if (message?.type === 'COMPLAINT_FORM_READY') {
        if (!isPortalPage(sender) || !sender.tab?.id || !sender.url ||
            new URL(sender.url).origin !== COMPLAINT_ORIGIN ||
            new URL(sender.url).pathname !== COMPLAINT_PATH) {
            sendResponse(null);
            return false;
        }
        void chrome.storage.session.get(null)
            .then(stored => {
                const handoff = Object.values(stored).find(value =>
                    value?.portalTabId === sender.tab.id &&
                    value.expiresAt > Date.now() &&
                    typeof value.jobId === 'string');
                sendResponse(handoff ? { jobId: handoff.jobId } : null);
            })
            .catch(error => sendResponse({ error: error.message }));
        return true;
    }

    if (message?.type === 'COMPLAINT_SUBMISSION_CONFIRMED') {
        if (!isPortalPage(sender) || !sender.tab?.id || !sender.url ||
            new URL(sender.url).origin !== COMPLAINT_ORIGIN ||
            new URL(sender.url).pathname !== COMPLAINT_PATH ||
            typeof message.jobId !== 'string' ||
            (message.complaintNumber != null &&
                (typeof message.complaintNumber !== 'string' ||
                    !/^[A-Za-z0-9-]{4,40}$/.test(message.complaintNumber)))) {
            sendResponse({ confirmed: false });
            return false;
        }
        void (async () => {
            const key = `${COMPLAINT_HANDOFF_PREFIX}${message.jobId}`;
            const stored = await chrome.storage.session.get(key);
            const handoff = stored[key];
            if (!handoff || handoff.expiresAt <= Date.now() ||
                handoff.portalTabId !== sender.tab.id || handoff.confirmed) {
                sendResponse({ confirmed: false });
                return;
            }
            handoff.confirmed = true;
            handoff.complaintNumber = message.complaintNumber || '';
            await chrome.storage.session.set({ [key]: handoff });

            let chatUrl;
            try {
                const chatTab = await chrome.tabs.get(handoff.chatTabId);
                chatUrl = new URL(chatTab.url || '');
            } catch (error) {
                console.warn('Complaint was confirmed, but its chat tab is no longer available:', error);
                sendResponse({ confirmed: true, delivered: false });
                return;
            }
            if (!CHAT_ORIGINS.has(chatUrl.origin)) {
                sendResponse({ confirmed: true, delivered: false });
                return;
            }
            try {
                await chrome.tabs.sendMessage(handoff.chatTabId, {
                    type: 'MUTAKAMELA_COMPLAINT_SUBMISSION_CONFIRMED',
                    jobId: message.jobId,
                    complaintNumber: handoff.complaintNumber
                });
                sendResponse({ confirmed: true, delivered: true });
            } catch (error) {
                console.warn('Complaint was confirmed, but the chat tab could not be notified:', error);
                sendResponse({ confirmed: true, delivered: false });
            }
        })().catch(error => sendResponse({
            confirmed: false,
            error: error instanceof Error ? error.message : 'Could not report the complaint confirmation.'
        }));
        return true;
    }

    if (message?.type === 'AUTOFILL_RESULT') {
        if (!isPortalPage(sender) || !sender.tab?.id) {
            sendResponse({ accepted: false });
            return false;
        }
        void (async () => {
            const key = `${PENDING_PREFIX}${sender.tab.id}`;
            const stored = await chrome.storage.session.get(key);
            const pending = stored[key];
            if (!pending || pending.jobId !== message.jobId || !Array.isArray(message.filledIds)) {
                sendResponse({ accepted: false });
                return;
            }
            pending.completedIds = [...new Set([
                ...pending.completedIds,
                ...message.filledIds.filter(id => pending.values[id] !== undefined)
            ])];
            if (pending.fields.every(field => pending.completedIds.includes(field.id)) &&
                pending.flowId !== 'submit-complaint') {
                await chrome.storage.session.remove(key);
            } else {
                await chrome.storage.session.set({ [key]: pending });
            }
            sendResponse({ accepted: true });
        })().catch(error => sendResponse({ accepted: false, error: error.message }));
        return true;
    }

    if (message?.type === 'CANCEL_AUTOFILL') {
        if (!isTrustedChat(sender)) {
            sendResponse({ cleared: false });
            return false;
        }
        void (async () => {
            const stored = await chrome.storage.session.get(null);
            const keys = Object.entries(stored)
                .filter(([key, pending]) => key.startsWith(PENDING_PREFIX) && pending?.jobId === message.jobId)
                .map(([key]) => key);
            if (keys.length) await chrome.storage.session.remove(keys);
            if (typeof message.jobId === 'string') await clearComplaintJob(message.jobId);
            sendResponse({ cleared: true });
        })().catch(error => sendResponse({ cleared: false, error: error.message }));
        return true;
    }

    return false;
});

chrome.tabs.onUpdated.addListener((tabId, changeInfo) => {
    if (changeInfo.status === 'complete') void sendPendingToTab(tabId);
});

chrome.tabs.onRemoved.addListener(tabId => {
    void (async () => {
        const stored = await chrome.storage.session.get(null);
        const pendingKey = `${PENDING_PREFIX}${tabId}`;
        await chrome.storage.session.remove(pendingKey);
        const jobEntry = Object.entries(stored).find(([key, value]) =>
            key.startsWith(COMPLAINT_JOB_PREFIX) && value?.tabId === tabId);
        if (jobEntry) await clearComplaintJob(jobEntry[0].slice(COMPLAINT_JOB_PREFIX.length));
        const handoffEntry = Object.entries(stored).find(([key, value]) =>
            key.startsWith(COMPLAINT_HANDOFF_PREFIX) &&
            value?.portalTabId === tabId &&
            value?.confirmed !== true);
        if (handoffEntry) {
            const jobId = handoffEntry[0].slice(COMPLAINT_HANDOFF_PREFIX.length);
            await chrome.storage.session.remove(handoffEntry[0]);
            await chrome.alarms.clear(`${COMPLAINT_HANDOFF_ALARM_PREFIX}${jobId}`);
        }
    })().catch(error => console.error('Could not clean up complaint attachments after closing the form:', error));
});

chrome.alarms.onAlarm.addListener(alarm => {
    if (alarm.name.startsWith(ATTACHMENT_ALARM_PREFIX)) {
        const jobId = alarm.name.slice(ATTACHMENT_ALARM_PREFIX.length);
        void clearComplaintJob(jobId).catch(error => console.error('Could not expire complaint attachments:', error));
        return;
    }
    if (alarm.name.startsWith(COMPLAINT_HANDOFF_ALARM_PREFIX)) {
        const jobId = alarm.name.slice(COMPLAINT_HANDOFF_ALARM_PREFIX.length);
        void chrome.storage.session.remove(`${COMPLAINT_HANDOFF_PREFIX}${jobId}`)
            .catch(error => console.error('Could not expire complaint handoff tracking:', error));
    }
});

chrome.runtime.onStartup.addListener(() => {
    void clearAllComplaintFiles().catch(error => console.error('Could not clear stale complaint attachments:', error));
});
