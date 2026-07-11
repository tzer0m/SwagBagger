// Explicitly renders the Cloudflare Turnstile widget into the given element, avoiding races with Blazor's own DOM diffing.
window.renderTurnstile = (elementId, siteKey) => {
    if (window.turnstile) {
        window.turnstile.render(`#${elementId}`, {
            sitekey: siteKey
        });
    }
};