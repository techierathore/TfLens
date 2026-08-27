// Escape-to-dismiss for the Repos screen's two overlays.
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
