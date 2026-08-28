// Browser-side helpers for the Repos screen: Escape-to-dismiss for its overlays, and the file
// handling the Add-source dialog's "Import metric files" mode needs (REQ-UI-040).
//
// Why the upload lives here rather than in an <InputFile>: the bundle is posted straight to
// /api/import/preview and /api/import/commit as multipart, so a 25 MB zip never travels over the
// SignalR circuit, and the endpoints' own size gate (which runs before a byte of the body is read)
// is the thing that bounds it. The circuit only ever sees counts and messages.
//
// Why this file exists: TrBlazeUI 2.0.0's `AlertDialog` ships no Escape handling at all — its only
// parameters are Open / OpenChanged / DefaultOpen / OnOpenChange, and unlike `Dialog` it is not built
// on `TrBlazeUI.Primitives.Dialog.DialogContent`, so it never gets that primitive's `CloseOnEscape`.
// The remove confirmation therefore could only be dismissed with Cancel (see TR-014 in
// docs/TfLens-TrBlazeUI-Feedback.md). The same document-level listener also covers the Connect
// `Dialog`, whose own Escape handling stops firing once a validation result has re-rendered its
// content.
//
// The listener is deliberately dumb: it reports the key press and lets the circuit decide which
// overlay — if any — should close. The one thing it decides for itself is the `role="listbox"` case,
// where an open Select/Combobox popup owns the Escape and the dialog behind it must stay put.

let escapeHandler = null;

/**
 * Starts reporting Escape presses to the page.
 * @param {object} dotNetRef - Reference to the Repos component.
 */
export function watchEscape(dotNetRef) {
    stopWatchingEscape();

    if (!dotNetRef) {
        return;
    }

    escapeHandler = (event) => {
        if (event.key !== 'Escape') {
            return;
        }

        // An open Select / Combobox popup consumes its own Escape; the dialog behind it stays open.
        if (document.querySelector('[role="listbox"]')) {
            return;
        }

        dotNetRef.invokeMethodAsync('DismissOpenDialogAsync').catch(() => {
            // The circuit went away between the key press and the call; nothing to dismiss.
        });
    };

    document.addEventListener('keydown', escapeHandler, true);
}

/** Stops reporting Escape presses. */
export function stopWatchingEscape() {
    if (escapeHandler) {
        document.removeEventListener('keydown', escapeHandler, true);
        escapeHandler = null;
    }
}

// ---------------------------------------------------------------------------------------------
// Import metric files (REQ-UI-040)
// ---------------------------------------------------------------------------------------------
//
// The drop zone and the file input live inside a portalled dialog that Blazor re-renders on every
// keystroke, so nothing here holds an element reference. Both listeners are delegated from
// `document` and match on the dialog's own data-testid / id, which survives any number of renders.

const DROP_SELECTOR = '[data-testid="import-drop"]';
const FILE_INPUT_ID = 'tflens-import-file';
const TOKEN_SELECTOR = 'input[name="__RequestVerificationToken"]';

/** The files the user last chose or dropped. Never sent anywhere but the two import endpoints. */
let selectedFiles = [];

let importHandlers = null;

/**
 * Starts watching the dialog's drop zone and file input.
 * @param {object} dotNetRef - Reference to the Repos component.
 */
export function watchImport(dotNetRef) {
    stopWatchingImport();

    if (!dotNetRef) {
        return;
    }

    const announce = (files) => {
        selectedFiles = Array.from(files || []);

        if (selectedFiles.length === 0) {
            return;
        }

        const names = selectedFiles.map(f => f.name);
        const bytes = selectedFiles.reduce((sum, f) => sum + f.size, 0);

        dotNetRef.invokeMethodAsync('OnBundleSelectedAsync', names, bytes).catch(() => {
            // The circuit went away between the drop and the call; nothing to preview.
        });
    };

    const onChange = (event) => {
        const input = event.target;

        if (input && input.id === FILE_INPUT_ID) {
            announce(input.files);
        }
    };

    const onDragOver = (event) => {
        const zone = event.target && event.target.closest ? event.target.closest(DROP_SELECTOR) : null;

        if (zone) {
            event.preventDefault();
            zone.setAttribute('data-drop-active', 'true');
        }
    };

    const onDragLeave = (event) => {
        const zone = event.target && event.target.closest ? event.target.closest(DROP_SELECTOR) : null;

        if (zone) {
            zone.removeAttribute('data-drop-active');
        }
    };

    const onDrop = (event) => {
        const zone = event.target && event.target.closest ? event.target.closest(DROP_SELECTOR) : null;

        if (!zone) {
            return;
        }

        event.preventDefault();
        zone.removeAttribute('data-drop-active');
        announce(event.dataTransfer ? event.dataTransfer.files : []);
    };

    importHandlers = { onChange, onDragOver, onDragLeave, onDrop };

    document.addEventListener('change', onChange, true);
    document.addEventListener('dragover', onDragOver, true);
    document.addEventListener('dragleave', onDragLeave, true);
    document.addEventListener('drop', onDrop, true);
}

/** Stops watching the drop zone and forgets whatever was chosen. */
export function stopWatchingImport() {
    if (importHandlers) {
        document.removeEventListener('change', importHandlers.onChange, true);
        document.removeEventListener('dragover', importHandlers.onDragOver, true);
        document.removeEventListener('dragleave', importHandlers.onDragLeave, true);
        document.removeEventListener('drop', importHandlers.onDrop, true);
        importHandlers = null;
    }

    clearBundle();
}

/** Opens the native file picker from the drop zone's button. */
export function openFilePicker() {
    const input = document.getElementById(FILE_INPUT_ID);

    if (input) {
        input.value = '';
        input.click();
    }
}

/** Forgets the chosen files — what Cancel and a reopened dialog do. */
export function clearBundle() {
    selectedFiles = [];

    const input = document.getElementById(FILE_INPUT_ID);

    if (input) {
        input.value = '';
    }
}

/**
 * Posts the chosen bundle to one of the two import endpoints.
 * @param {string} url - The endpoint route.
 * @param {string|null} source - `owner/name` for the commit route; omitted for preview.
 * @returns {Promise<string>} `{"status":n,"body":"<raw response text>"}` as JSON.
 */
export async function postBundle(url, source) {
    if (selectedFiles.length === 0) {
        return JSON.stringify({
            status: 0,
            body: JSON.stringify({ accepted: false, reason: 'Empty', message: 'No file was attached.' })
        });
    }

    const token = document.querySelector(TOKEN_SELECTOR);
    const form = new FormData();

    // One file goes as itself; several loose stream files are bundled into one stored archive so
    // the whole upload has a single sha256 — a dataset has exactly one identity (ADR-022).
    if (selectedFiles.length === 1) {
        form.append('file', selectedFiles[0], selectedFiles[0].name);
    } else {
        const zipped = await buildStoredZip(selectedFiles);
        form.append('file', zipped, 'bundle.zip');
    }

    if (source) {
        form.append('source', source);
    }

    if (token) {
        form.append('__RequestVerificationToken', token.value);
    }

    try {
        const response = await fetch(url, { method: 'POST', body: form, credentials: 'same-origin' });
        const text = await response.text();
        return JSON.stringify({ status: response.status, body: text });
    } catch (error) {
        return JSON.stringify({
            status: 0,
            body: JSON.stringify({
                accepted: false,
                reason: 'Network',
                message: 'The upload could not be sent: ' + String(error)
            })
        });
    }
}

// --- a minimal stored (uncompressed) zip writer -------------------------------------------------
//
// Deliberately stored rather than deflated: the archived bytes must be the framework's own bytes,
// verbatim (BRD-133), and a stored entry is exactly that with a header in front of it. The fixed
// 1980-01-01 timestamp keeps the same set of files hashing to the same bundle sha every time.

let crcTable = null;

/**
 * Builds (once) the CRC-32 lookup table the zip format needs.
 * @returns {Uint32Array} The table.
 */
function crc32Table() {
    if (crcTable) {
        return crcTable;
    }

    crcTable = new Uint32Array(256);

    for (let n = 0; n < 256; n++) {
        let c = n;

        for (let k = 0; k < 8; k++) {
            c = (c & 1) ? (0xEDB88320 ^ (c >>> 1)) : (c >>> 1);
        }

        crcTable[n] = c >>> 0;
    }

    return crcTable;
}

/**
 * CRC-32 of a byte array.
 * @param {Uint8Array} bytes - The bytes.
 * @returns {number} The checksum.
 */
function crc32(bytes) {
    const table = crc32Table();
    let c = 0xFFFFFFFF;

    for (let i = 0; i < bytes.length; i++) {
        c = (c >>> 8) ^ table[(c ^ bytes[i]) & 0xFF];
    }

    return (c ^ 0xFFFFFFFF) >>> 0;
}

/**
 * Packs several files into one stored zip.
 * @param {File[]} files - The chosen files.
 * @returns {Promise<Blob>} The archive.
 */
async function buildStoredZip(files) {
    const encoder = new TextEncoder();
    const parts = [];
    const central = [];
    let offset = 0;

    for (const file of files) {
        const name = encoder.encode(file.name.split(/[\\/]/).pop());
        const bytes = new Uint8Array(await file.arrayBuffer());
        const sum = crc32(bytes);

        const local = new DataView(new ArrayBuffer(30));
        local.setUint32(0, 0x04034B50, true);
        local.setUint16(4, 20, true);
        local.setUint16(6, 0, true);
        local.setUint16(8, 0, true);
        local.setUint16(10, 0, true);
        local.setUint16(12, 33, true);
        local.setUint32(14, sum, true);
        local.setUint32(18, bytes.length, true);
        local.setUint32(22, bytes.length, true);
        local.setUint16(26, name.length, true);
        local.setUint16(28, 0, true);

        parts.push(new Uint8Array(local.buffer), name, bytes);

        const entry = new DataView(new ArrayBuffer(46));
        entry.setUint32(0, 0x02014B50, true);
        entry.setUint16(4, 20, true);
        entry.setUint16(6, 20, true);
        entry.setUint16(8, 0, true);
        entry.setUint16(10, 0, true);
        entry.setUint16(12, 0, true);
        entry.setUint16(14, 33, true);
        entry.setUint32(16, sum, true);
        entry.setUint32(20, bytes.length, true);
        entry.setUint32(24, bytes.length, true);
        entry.setUint16(28, name.length, true);
        entry.setUint16(30, 0, true);
        entry.setUint16(32, 0, true);
        entry.setUint16(34, 0, true);
        entry.setUint16(36, 0, true);
        entry.setUint32(38, 0, true);
        entry.setUint32(42, offset, true);

        central.push(new Uint8Array(entry.buffer), name);
        offset += 30 + name.length + bytes.length;
    }

    const directorySize = central.reduce((sum, part) => sum + part.length, 0);

    const end = new DataView(new ArrayBuffer(22));
    end.setUint32(0, 0x06054B50, true);
    end.setUint16(4, 0, true);
    end.setUint16(6, 0, true);
    end.setUint16(8, files.length, true);
    end.setUint16(10, files.length, true);
    end.setUint32(12, directorySize, true);
    end.setUint32(16, offset, true);
    end.setUint16(20, 0, true);

    return new Blob([...parts, ...central, new Uint8Array(end.buffer)], { type: 'application/zip' });
}
