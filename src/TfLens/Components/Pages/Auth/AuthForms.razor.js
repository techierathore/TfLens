// Helpers the anonymous auth pages need that a Blazor circuit cannot do on its own.
//
// Two of them, both small on purpose:
//   readForm  — hands the circuit exactly what the browser would post, so a local rule check or a
//               pre-flight credential check never runs against a value the binding has not caught up
//               with yet, and so a re-render after a failure puts the user's own text back.
//   submitForm — performs the real HTTP POST. Cookie sign-in needs a real response, which an
//               interactive component does not have, so the endpoints in AuthEndpoints own it.

export function readForm(formId) {
    const form = document.getElementById(formId);
    if (!form) {
        return null;
    }

    const values = {};
    for (const [key, value] of new FormData(form).entries()) {
        if (typeof value === 'string') {
            values[key] = value;
        }
    }

    return values;
}

export function submitForm(formId) {
    const form = document.getElementById(formId);
    if (form) {
        form.submit();
    }
}
