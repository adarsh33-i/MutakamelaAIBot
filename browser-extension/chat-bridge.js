const ALLOWED_CHAT_ORIGINS = new Set([
    'http://localhost:5001',
    'http://127.0.0.1:5001'
]);

if (ALLOWED_CHAT_ORIGINS.has(location.origin)) {
    document.addEventListener('DOMContentLoaded', () => {
        window.postMessage({ type: 'MUTAKAMELA_EXTENSION_READY' }, location.origin);
    }, { once: true });

    window.addEventListener('message', event => {
        if (event.source !== window || event.origin !== location.origin || !event.data) return;

        if (event.data.type === 'MUTAKAMELA_OPEN_PORTAL') {
            chrome.runtime.sendMessage({
                type: 'OPEN_PORTAL',
                payload: event.data.payload
            }).then(result => {
                window.postMessage({
                    type: 'MUTAKAMELA_PORTAL_OPENED',
                    jobId: event.data.payload?.jobId,
                    opened: result?.opened === true,
                    autofillReady: result?.autofillReady === true,
                    error: result?.error
                }, location.origin);
            }).catch(error => {
                window.postMessage({
                    type: 'MUTAKAMELA_PORTAL_OPENED',
                    jobId: event.data.payload?.jobId,
                    opened: false,
                    error: error instanceof Error ? error.message : 'Extension could not open the portal.'
                }, location.origin);
            });
        }

        if (event.data.type === 'MUTAKAMELA_GET_COMPLAINT_PRODUCTS') {
            chrome.runtime.sendMessage({
                type: 'GET_COMPLAINT_PRODUCTS'
            }).then(result => {
                window.postMessage({
                    type: 'MUTAKAMELA_COMPLAINT_PRODUCTS',
                    requestId: event.data.requestId,
                    products: result?.products,
                    error: result?.error
                }, location.origin);
            }).catch(error => {
                window.postMessage({
                    type: 'MUTAKAMELA_COMPLAINT_PRODUCTS',
                    requestId: event.data.requestId,
                    error: error instanceof Error ? error.message : 'Could not load the product list.'
                }, location.origin);
            });
        }

        if (event.data.type === 'MUTAKAMELA_STORE_COMPLAINT_FILE') {
            chrome.runtime.sendMessage({
                type: 'STORE_COMPLAINT_FILE',
                requestId: event.data.requestId,
                jobId: event.data.jobId,
                attachment: event.data.attachment
            }).then(result => {
                window.postMessage({
                    type: 'MUTAKAMELA_COMPLAINT_FILE_STORED',
                    requestId: event.data.requestId,
                    stored: result?.stored === true,
                    error: result?.error
                }, location.origin);
            }).catch(error => {
                window.postMessage({
                    type: 'MUTAKAMELA_COMPLAINT_FILE_STORED',
                    requestId: event.data.requestId,
                    stored: false,
                    error: error instanceof Error ? error.message : 'Extension could not store the complaint file.'
                }, location.origin);
            });
        }

        if (event.data.type === 'MUTAKAMELA_ATTACH_COMPLAINT_FILES') {
            chrome.runtime.sendMessage({
                type: 'ATTACH_COMPLAINT_FILES',
                requestId: event.data.requestId,
                jobId: event.data.jobId
            }).then(result => {
                window.postMessage({
                    type: 'MUTAKAMELA_COMPLAINT_FILES_ATTACHED',
                    requestId: event.data.requestId,
                    attached: result?.attached === true,
                    count: result?.count || 0,
                    error: result?.error
                }, location.origin);
            }).catch(error => {
                window.postMessage({
                    type: 'MUTAKAMELA_COMPLAINT_FILES_ATTACHED',
                    requestId: event.data.requestId,
                    attached: false,
                    error: error instanceof Error ? error.message : 'Extension could not attach the complaint files.'
                }, location.origin);
            });
        }

        if (event.data.type === 'MUTAKAMELA_CANCEL_PORTAL_AUTOFILL') {
            chrome.runtime.sendMessage({
                type: 'CANCEL_AUTOFILL',
                jobId: event.data.jobId
            }).catch(error => console.error('Could not clear pending portal autofill:', error));
        }
    });

    chrome.runtime.onMessage.addListener(message => {
        if (message?.type !== 'MUTAKAMELA_COMPLAINT_SUBMISSION_CONFIRMED' ||
            typeof message.jobId !== 'string' ||
            !/^[a-zA-Z0-9-]{1,80}$/.test(message.jobId)) return;
        window.postMessage({
            type: 'MUTAKAMELA_COMPLAINT_SUBMISSION_CONFIRMED',
            jobId: message.jobId,
            complaintNumber: typeof message.complaintNumber === 'string'
                ? message.complaintNumber
                : ''
        }, location.origin);
    });
}
