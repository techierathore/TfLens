// Small, dependency-free helpers the Blazor circuit calls through IJSRuntime.
// Kept minimal on purpose: TrBlazeUI ships its own interop for every component.
window.tflens = {
    // Flips the dark class on <html> and mirrors the choice into a cookie so the
    // server can render the correct palette on the very first byte of the next request.
    setTheme: function (isDark) {
        document.documentElement.classList.toggle('dark', isDark);
        document.cookie = 'tflens-theme=' + (isDark ? 'dark' : 'light') +
            ';path=/;max-age=' + (60 * 60 * 24 * 365) + ';samesite=lax';
    },
    // Writes any per-user UI preference cookie (framework switch, tab choice).
    setPreference: function (name, value) {
        document.cookie = name + '=' + encodeURIComponent(value) +
            ';path=/;max-age=' + (60 * 60 * 24 * 365) + ';samesite=lax';
    },
    // Reads a preference cookie back. An interactive Blazor Server circuit outlives the request that
    // created it, so IHttpContextAccessor.HttpContext is null inside it and the server genuinely
    // cannot see these cookies — the browser is the only place the persisted choice still exists.
    getPreference: function (name) {
        const prefix = name + '=';
        for (const part of document.cookie.split(';')) {
            const c = part.trim();
            if (c.startsWith(prefix)) {
                return decodeURIComponent(c.substring(prefix.length));
            }
        }
        return null;
    },
    // Performs a real HTTP form POST. A cookie can only be set or cleared on a real response, which
    // an interactive Blazor circuit does not have — sign-out and sign-in therefore leave the circuit.
    // form.submit() (not requestSubmit()) is deliberate: it bypasses the submit event, so a Blazor
    // @onsubmit:preventDefault handler on the same form cannot swallow the navigation.
    submitForm: function (formId) {
        const form = document.getElementById(formId);
        if (form) {
            form.submit();
        }
    },
    // Copies text to the clipboard for the SHA and snapshot-path copy buttons.
    copyText: async function (text) {
        try {
            await navigator.clipboard.writeText(text);
            return true;
        } catch {
            return false;
        }
    },
    // Triggers a browser download of a server-produced file.
    downloadFile: function (url) {
        const a = document.createElement('a');
        a.href = url;
        a.download = '';
        document.body.appendChild(a);
        a.click();
        a.remove();
    }
};
