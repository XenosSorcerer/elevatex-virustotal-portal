// Blazor JS initializer (auto-loaded by blazor.web.js — see the "JS initializers" convention:
// a wwwroot/{assembly name}.lib.module.js file needs no manual <script> tag).
//
// Connects to the dedicated ScanHub (Hubs/ScanHub.cs) over a real SignalR connection, separate
// from Blazor Server's own render circuit — the "Live" badge in the header updates purely via
// DOM manipulation, so it keeps working even while this tab's Blazor circuit reconnects.
// The signalr.min.js UMD client (wwwroot/lib/signalr/...) is loaded as a classic <script> in
// App.razor's <head>, ahead of Blazor startup, so window.signalR is already defined here.

function statusEl() { return document.getElementById('scan-hub-status'); }
function dotEl() { return document.getElementById('scan-hub-dot'); }

function setState(text, cls) {
    const status = statusEl();
    if (status) status.textContent = text;

    const dot = dotEl();
    if (dot) dot.className = 'hub-dot ' + cls;
}

function pulse() {
    const dot = dotEl();
    if (!dot) return;
    dot.classList.add('pulse');
    setTimeout(() => dot.classList.remove('pulse'), 600);
}

async function connect() {
    if (!window.signalR) {
        setState('Live: unavailable', 'off');
        return;
    }

    const connection = new window.signalR.HubConnectionBuilder()
        .withUrl('/hubs/scan')
        .withAutomaticReconnect()
        .build();

    let eventCount = 0;

    connection.on('scanChanged', () => {
        eventCount++;
        setState(`Live: ${eventCount} update${eventCount === 1 ? '' : 's'}`, 'on');
        pulse();
    });

    connection.onreconnecting(() => setState('Live: reconnecting…', 'warn'));
    connection.onreconnected(() => setState(`Live: ${eventCount} update${eventCount === 1 ? '' : 's'}`, 'on'));
    connection.onclose(() => setState('Live: disconnected', 'off'));

    try {
        await connection.start();
        setState('Live: connected', 'on');
    } catch {
        setState('Live: connection failed', 'off');
    }
}

// This app only registers Interactive Server components (Program.cs: AddInteractiveServerComponents),
// so afterServerStarted — fired once that circuit is actually established — is the correct hook.
// afterWebStarted fires earlier, before the circuit finishes hydrating MainLayout; connecting
// there raced the circuit's own first render pass and got its DOM update silently clobbered.
export function afterServerStarted() {
    connect();
}
