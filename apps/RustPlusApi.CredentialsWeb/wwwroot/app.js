"use strict";

const SESSION_KEY = "rustplus-credentials-session";

const view = {
    intro: document.getElementById("intro"),
    progress: document.getElementById("progress"),
    ready: document.getElementById("ready"),
    waiting: document.getElementById("waiting"),
    paired: document.getElementById("paired"),
    failed: document.getElementById("failed")
};

let sessionId = null;
let configJson = null;

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

function onStep(payload) {
    if (payload.state === "AwaitingPairing") {
        show("waiting");
    } else if (payload.state === "Registering" || payload.state === "Authenticated") {
        show("progress");
    }
}

function onCredentials(payload) {
    configJson = payload.configJson;
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
    sessionStorage.setItem(SESSION_KEY, body.sessionId);
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
    location.href = "/";
});

sessionId = readSessionId();
if (sessionId) {
    show("progress");
    listen(sessionId);
} else {
    show("intro");
}
