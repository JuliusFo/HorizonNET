// PIN-Sperre des Journals.
//
// WICHTIG, damit hier niemand mehr hineinliest, als drinsteckt: Das ist eine
// BILDSCHIRMSPERRE gegen fremde Blicke am offenen Rechner – keine Zugriffskontrolle.
// Die API ist davon unberührt: Wer sie erreicht, liest das Journal auch ohne PIN.
// Echten Schutz bringt erst die Authentifizierung vor dem Livegang.
//
// Die PIN wird trotzdem nicht im Klartext abgelegt, sondern als SHA-256 über
// Salz + PIN. Das verhindert, dass sie beim Blick in den localStorage direkt
// ablesbar ist. Gegen jemanden, der vierstellige PINs durchprobiert, hilft das
// nicht – das ist bei einer Bildschirmsperre auch nicht der Anspruch.

export function randomSalt() {
    const bytes = new Uint8Array(16);
    crypto.getRandomValues(bytes);
    return toHex(bytes);
}

export async function hash(salt, pin) {
    const data = new TextEncoder().encode(salt + ':' + pin);
    const digest = await crypto.subtle.digest('SHA-256', data);
    return toHex(new Uint8Array(digest));
}

function toHex(bytes) {
    return [...bytes].map(b => b.toString(16).padStart(2, '0')).join('');
}

// ── Aktivitäts-Erkennung für den Auto-Lock ──────────────────────────────────────
// Ohne das würde die Sperre auch beim Lesen zuschlagen: Wer einen langen Eintrag
// liest, tippt minutenlang nichts und wäre plötzlich ausgesperrt.
// Gemeldet wird höchstens alle 15 s – die Ereignisse feuern sonst hundertfach.

let activityHandler;
let lastReport = 0;

export function trackActivity(dotNetRef) {
    untrackActivity();
    activityHandler = () => {
        const now = Date.now();
        if (now - lastReport < 15000) return;
        lastReport = now;
        dotNetRef.invokeMethodAsync('OnUserActivity');
    };
    for (const type of ['keydown', 'mousedown', 'mousemove', 'wheel', 'touchstart']) {
        document.addEventListener(type, activityHandler, { passive: true });
    }
}

export function untrackActivity() {
    if (!activityHandler) return;
    for (const type of ['keydown', 'mousedown', 'mousemove', 'wheel', 'touchstart']) {
        document.removeEventListener(type, activityHandler);
    }
    activityHandler = null;
    lastReport = 0;
}
