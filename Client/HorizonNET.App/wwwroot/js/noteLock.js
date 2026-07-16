// Tab-Lock für den Notiz-Editor.
//
// Zwei Tabs auf derselben Notiz haben sich gegenseitig den Stand überschrieben (jeder
// Tab speicherte seinen eigenen, veralteten Inhalt). Deshalb darf eine Notiz nur in
// einem Tab bearbeitet werden; alle weiteren Tabs zeigen sie schreibgeschützt.
//
// Der Lock liegt in localStorage (überlebt einen Absturz nur bis zum TTL-Ablauf) und
// wird per Heartbeat frisch gehalten. Der BroadcastChannel sorgt dafür, dass ein
// bereits offener Editor eine Übernahme sofort mitbekommt statt erst nach TTL-Ablauf.

const TAB_ID = crypto.randomUUID();
const LOCK_TTL_MS = 15000;   // ab hier gilt ein Lock als verwaist (Tab abgestürzt/geschlossen)
const HEARTBEAT_MS = 5000;

const channel = new BroadcastChannel('note-lock');

// Der Lock, den dieser Tab gerade hält: { noteId, owner (DotNetObjectReference), timer }
let held = null;

const storageKey = noteId => `note-lock:${noteId}`;

function readLock(noteId) {
    try {
        return JSON.parse(localStorage.getItem(storageKey(noteId)));
    } catch {
        return null;
    }
}

function stampLock(noteId) {
    localStorage.setItem(storageKey(noteId), JSON.stringify({ tabId: TAB_ID, ts: Date.now() }));
}

// Versucht, die Notiz für diesen Tab zu sperren. false = ein anderer, lebender Tab
// bearbeitet sie bereits (der Aufrufer geht dann in den Read-Only-Modus).
// force = true übernimmt den Lock trotzdem ("Bearbeitung hier übernehmen").
export function acquire(noteId, owner, force) {
    release();

    const existing = readLock(noteId);
    const heldElsewhere = existing
        && existing.tabId !== TAB_ID
        && (Date.now() - existing.ts) < LOCK_TTL_MS;

    if (heldElsewhere && !force) return false;

    stampLock(noteId);
    channel.postMessage({ noteId, tabId: TAB_ID });
    held = { noteId, owner, timer: setInterval(() => stampLock(noteId), HEARTBEAT_MS) };
    return true;
}

// noteId (optional): nur freigeben, wenn dieser Tab den Lock für GENAU diese Notiz hält.
// Ohne Argument (interner Aufruf aus acquire, pagehide) wird unbedingt freigegeben.
// Wichtig, wenn zwei Editor-Komponenten (Notiz/Zeichnung) wechseln: der abgebaute darf
// nicht den frisch geholten Lock des neuen wieder entfernen.
export function release(noteId) {
    if (!held) return;
    if (noteId != null && held.noteId !== noteId) return;

    clearInterval(held.timer);

    // Nur den eigenen Lock entfernen – hat inzwischen ein anderer Tab übernommen,
    // gehört der Eintrag ihm.
    const existing = readLock(held.noteId);
    if (existing && existing.tabId === TAB_ID) localStorage.removeItem(storageKey(held.noteId));

    held = null;
}

// Ein anderer Tab hat die Notiz übernommen, die wir gerade halten.
channel.onmessage = event => {
    const { noteId, tabId } = event.data;
    if (!held || tabId === TAB_ID || noteId !== held.noteId) return;

    clearInterval(held.timer);
    const owner = held.owner;
    held = null;
    owner.invokeMethodAsync('OnLockLost');
};

// Beim Schließen/Verstecken des Tabs den Lock freigeben, damit die Notiz nicht bis
// zum TTL-Ablauf blockiert bleibt. (pagehide feuert auch dort, wo unload es nicht tut.)
window.addEventListener('pagehide', release);
