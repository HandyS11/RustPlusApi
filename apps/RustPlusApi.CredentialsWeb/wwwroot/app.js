"use strict";

const SESSION_KEY = "rustplus-credentials-session";
const PAIRING_KEY = "rustplus-credentials-pairing";

const view = {
    intro: document.getElementById("intro"),
    paste: document.getElementById("paste"),
    progress: document.getElementById("progress"),
    ready: document.getElementById("ready"),
    waiting: document.getElementById("waiting"),
    paired: document.getElementById("paired"),
    failed: document.getElementById("failed")
};

let sessionId = null;
let configJson = null;
let callbackMode = "redirect";
let pairingAvailable = true;

function show(name) {
    for (const [key, element] of Object.entries(view)) {
        element.hidden = key !== name;
    }
}

function fail(message) {
    document.getElementById("error").textContent = message;
    show("failed");
}

function readSessionId() {
    const match = /^#session=([0-9a-f]{32})$/.exec(location.hash);
    if (match) {
        // Drop the fragment so a shared or bookmarked URL does not carry the session handle.
        history.replaceState({}, "", location.pathname);
        sessionStorage.setItem(SESSION_KEY, match[1]);
        return match[1];
    }
    return sessionStorage.getItem(SESSION_KEY);
}

function download(name, text) {
    const url = URL.createObjectURL(new Blob([text], { type: "application/json" }));
    const link = document.createElement("a");
    link.href = url;
    link.download = name;
    link.click();
    URL.revokeObjectURL(url);
}

function flash(button, text) {
    const original = button.textContent;
    button.textContent = text;
    setTimeout(() => { button.textContent = original; }, 2000);
}

function toggleJson(force) {
    const pre = document.getElementById("config-json");
    const toggle = document.getElementById("show-json");
    const shown = force === undefined ? pre.hidden : force;
    pre.hidden = !shown;
    toggle.setAttribute("aria-expanded", String(shown));
    toggle.textContent = shown ? "Hide JSON" : "Show JSON";
}

function selectElement(element) {
    const range = document.createRange();
    range.selectNodeContents(element);
    const selection = getSelection();
    selection.removeAllRanges();
    selection.addRange(range);
}

async function copyText(text, button, fallback) {
    // The status line is per-section: the Ready and Paired screens each have their own, and writing
    // into a hidden one would render nothing and announce nothing.
    const status = button.closest("section")?.querySelector(".status");
    try {
        await navigator.clipboard.writeText(text);
        flash(button, "Copied");
        if (status) { status.textContent = "Copied to the clipboard."; }
    } catch {
        // Clipboard access can be refused even in a secure context, for instance when the document
        // does not have focus. A selection lets the visitor finish with their own shortcut.
        flash(button, "Copy failed");
        if (status) {
            status.textContent = "Copying was blocked. The text is selected — press Ctrl+C or Cmd+C.";
        }
        if (fallback) {
            // Unhiding is only correct for the config-JSON block: a pairing value or the snippet
            // is never hidden by its own section being collapsed, so toggleJson would be wrong there.
            if (fallback === document.getElementById("config-json") && fallback.hidden) {
                toggleJson(true);
            }
            selectElement(fallback);
        }
    }
}

function applyPairingAvailability() {
    document.getElementById("pair-offer").hidden = !pairingAvailable;
    document.getElementById("pair-unavailable").hidden = pairingAvailable;
}

function showPaste() {
    // Reached either straight after creating a session, when the login link is known, or from the
    // progress screen as a rescue, when it is not.
    document.getElementById("paste-intro").hidden = !document.getElementById("login-link").getAttribute("href");
    // Every entry into this section must start usable: submitPaste disables the button and only
    // re-enables it on failure, so a visitor returning here after a successful paste would
    // otherwise find it dead.
    document.getElementById("submit-pasted").disabled = false;
    document.getElementById("paste-error").textContent = "";
    show("paste");
}

async function submitPaste() {
    const input = document.getElementById("pasted-url");
    const button = document.getElementById("submit-pasted");
    const error = document.getElementById("paste-error");
    const url = input.value.trim();

    if (!url) {
        error.textContent = "Paste the address from the page that failed to load.";
        return;
    }

    button.disabled = true;
    error.textContent = "";

    const response = await fetch("/api/callback", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ url })
    });

    if (response.ok) {
        // The address carries the Steam token, so it does not linger in the field.
        input.value = "";
        show("progress");
        return;
    }

    const body = await response.json().catch(() => ({
        message: "That address could not be read. Copy the whole address and try again."
    }));
    error.textContent = body.message;
    button.disabled = false;
}

function onStep(payload) {
    if (payload.state === "AwaitingPairing") {
        show("waiting");
    } else if (payload.state === "Registering" || payload.state === "Authenticated") {
        show("progress");
    }
}

function onCredentials(payload) {
    configJson = payload.configJson;
    document.getElementById("config-json").textContent = configJson;
    document.getElementById("steam-id").textContent = payload.steamId;
    show("ready");
}

function onPaired(payload) {
    document.getElementById("server-name").textContent = payload.name ?? "(unnamed)";
    document.getElementById("pair-ip").textContent = payload.ip;
    document.getElementById("pair-port").textContent = payload.port;
    document.getElementById("pair-player-id").textContent = payload.playerId;
    document.getElementById("pair-player-token").textContent = payload.playerToken;
    document.getElementById("snippet").textContent =
        "new RustPlus(new RustPlusConnection(\"" + payload.ip + "\", " + payload.port +
        ", " + payload.playerId + ", " + payload.playerToken + "));";
    show("paired");
}

function listen(id) {
    const source = new EventSource("/api/sessions/" + id + "/events");

    source.addEventListener("step", e => onStep(JSON.parse(e.data)));
    source.addEventListener("credentials", e => onCredentials(JSON.parse(e.data)));
    source.addEventListener("paired", e => { onPaired(JSON.parse(e.data)); source.close(); });
    source.addEventListener("error", e => {
        // A named "error" event carries our payload; a transport error has no data and
        // EventSource reconnects on its own, so it is not surfaced.
        if (e.data) {
            fail(JSON.parse(e.data).message);
            source.close();
        } else if (source.readyState === EventSource.CLOSED) {
            // A non-2xx response (e.g. a 404 for an unknown or swept session) makes the browser
            // fail the connection permanently rather than retry, leaving readyState CLOSED. A
            // transient network blip leaves it CONNECTING and the browser retries on its own, so
            // only CLOSED means the session is actually gone.
            sessionStorage.removeItem(SESSION_KEY);
            fail("This session has expired. Start over — nothing was saved.");
        }
    });
    source.addEventListener("expired", () => {
        // Not a failure: the session is back in Ready, so the stream stays open and the visitor
        // can start another wait without repeating the Steam login.
        document.getElementById("pair").disabled = false;
        document.getElementById("pair-note").textContent =
            "No pairing arrived in time. Your credentials are still valid — try again when you're in game.";
        show("ready");
    });
}

async function start() {
    const button = document.getElementById("start");
    button.disabled = true;

    const response = await fetch("/api/sessions", { method: "POST" });
    if (!response.ok) {
        const body = await response.json().catch(() => ({ message: "This instance is busy." }));
        fail(body.message);
        return;
    }

    const body = await response.json();
    sessionId = body.sessionId;
    callbackMode = body.callbackMode;
    pairingAvailable = body.pairingAvailable;
    sessionStorage.setItem(SESSION_KEY, body.sessionId);
    sessionStorage.setItem(PAIRING_KEY, String(body.pairingAvailable));
    applyPairingAvailability();

    if (callbackMode === "paste") {
        // Attach the stream before the visitor leaves for Steam: the flow starts the moment they
        // paste, and this tab is where they will be watching it.
        listen(sessionId);
        document.getElementById("login-link").href = body.loginUrl;
        showPaste();
        return;
    }

    location.href = body.loginUrl;
}

async function pair() {
    const button = document.getElementById("pair");
    button.disabled = true;

    const response = await fetch("/api/sessions/" + sessionId + "/pairing", { method: "POST" });
    if (!response.ok) {
        const body = await response.json().catch(() => ({ message: "Could not start the pairing wait." }));
        fail(body.message);
        button.disabled = false;
    }
}

document.getElementById("start").addEventListener("click", start);
document.getElementById("pair").addEventListener("click", pair);
document.getElementById("download").addEventListener("click",
    () => download("rustplus.config.json", configJson));
document.getElementById("download-paired").addEventListener("click",
    () => download("rustplus.config.json", configJson));
document.getElementById("restart").addEventListener("click", () => {
    sessionStorage.removeItem(SESSION_KEY);
    sessionStorage.removeItem(PAIRING_KEY);
    location.href = "/";
});
document.getElementById("submit-pasted").addEventListener("click", submitPaste);
document.getElementById("paste-instead").addEventListener("click", showPaste);
document.getElementById("pasted-url").addEventListener("paste", () => {
    // The field's value is not updated until after this event, so read it on the next tick.
    setTimeout(() => {
        if (document.getElementById("pasted-url").value.includes("/callback/")) {
            submitPaste();
        }
    }, 0);
});
document.getElementById("show-json").addEventListener("click", () => toggleJson());
document.getElementById("copy-json").addEventListener("click", event =>
    copyText(configJson, event.currentTarget, document.getElementById("config-json")));
document.getElementById("copy-snippet").addEventListener("click", event =>
    copyText(document.getElementById("snippet").textContent, event.currentTarget,
             document.getElementById("snippet")));

// Delegated, because the pairing values are filled in after this script runs. The content policy
// forbids inline handlers, so the target is named by a data attribute instead.
document.addEventListener("click", event => {
    const button = event.target.closest("button.copy");
    if (button) {
        const value = document.getElementById(button.dataset.copy);
        copyText(value.textContent, button, value);
    }
});

// The server decides this, and says so in the create-session response. Until then the page guesses
// from its own address, which is right in every ordinary case and only cosmetic when it is not.
const looksLocal = ["localhost", "127.0.0.1", "[::1]"].includes(location.hostname)
    || location.hostname.endsWith(".localhost");
document.getElementById("trust-local").hidden = !looksLocal;
document.getElementById("trust-hosted").hidden = looksLocal;
pairingAvailable = sessionStorage.getItem(PAIRING_KEY) !== "false";
applyPairingAvailability();

sessionId = readSessionId();
if (sessionId) {
    show("progress");
    listen(sessionId);
} else {
    show("intro");
}
