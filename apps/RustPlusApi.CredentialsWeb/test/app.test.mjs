// Runs wwwroot/app.js against a minimal stand-in for the browser, so its pure rendering logic can be
// asserted without a browser or any npm dependency: `node --test test/app.test.mjs`.
import { test } from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import vm from "node:vm";

const source = readFileSync(new URL("../wwwroot/app.js", import.meta.url), "utf8");

function fakeElement() {
    return {
        textContent: "",
        hidden: false,
        disabled: false,
        value: "",
        dataset: {},
        addEventListener() {},
        setAttribute() {},
        removeAttribute() {},
        click() {}
    };
}

/** Loads app.js into a fresh context. It starts on the intro view: no fragment, empty storage. */
function loadApp() {
    const elements = new Map();
    const storage = new Map();
    const context = {
        document: {
            getElementById(id) {
                if (!elements.has(id)) {
                    elements.set(id, fakeElement());
                }
                return elements.get(id);
            },
            createElement: fakeElement,
            addEventListener() {}
        },
        location: { hash: "", pathname: "/", origin: "http://localhost:8080", hostname: "localhost", href: "/" },
        history: { replaceState() {} },
        sessionStorage: {
            getItem: key => storage.get(key) ?? null,
            setItem: (key, value) => storage.set(key, String(value)),
            removeItem: key => storage.delete(key)
        },
        EventSource: class { addEventListener() {} close() {} },
        setTimeout,
        clearTimeout
    };
    vm.createContext(context);
    vm.runInContext(source, context);
    return { context, element: id => context.document.getElementById(id) };
}

const pairing = { name: "Dummy", ip: "10.0.0.1", port: 28082, playerId: "76561198000000001", playerToken: 123456789 };

test("the snippet names the pairing's connection values", () => {
    const app = loadApp();
    app.context.onPaired(pairing);
    assert.equal(
        app.element("snippet").textContent,
        "new RustPlus(new RustPlusConnection(\"10.0.0.1\", 28082, 76561198000000001, 123456789));");
});

test("the snippet escapes the pushed ip as a C# string literal", () => {
    // The ip comes from the game server's pairing push. Spliced in raw, a double quote would close
    // the literal and let the rest of the value become statements in the visitor's program.
    const app = loadApp();
    app.context.onPaired({ ...pairing, ip: "1.2.3.4\", 1, 1, 1)); Evil(); _ = (\"x\\y\nz" });
    assert.equal(
        app.element("snippet").textContent,
        "new RustPlus(new RustPlusConnection(\"1.2.3.4\\\", 1, 1, 1)); Evil(); _ = (\\\"x\\\\y\\u000az\", "
        + "28082, 76561198000000001, 123456789));");
});
