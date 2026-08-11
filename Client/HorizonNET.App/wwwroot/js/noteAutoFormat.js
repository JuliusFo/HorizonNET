// Automatische Formatierung im Notiz-Editor.
//
// Der RadzenHtmlEditor ist ein contenteditable-Bereich ohne Markdown-Verständnis: Wer eine
// Liste anfangen will, muss in die Werkzeugleiste greifen. Dieses Modul erkennt die
// gewohnten Kürzel AM ZEILENANFANG und wandelt sie beim Leerzeichen in echtes HTML:
//
//   "- ", "* ", "+ "   → Aufzählung
//   "1. " (jede Zahl)  → nummerierte Liste
//   "# " … "### "      → Überschrift 1–3
//   "> "               → Zitat
//
// Dazu Tab / Shift+Tab innerhalb einer Liste zum Ein- und Ausrücken.
//
// Umgesetzt über document.execCommand. Das gilt als veraltet, ist hier aber genau richtig:
// Es ist derselbe Weg, den der Editor für seine eigenen Werkzeugleisten-Knöpfe nutzt.
// Dadurch entsteht dasselbe HTML wie beim Klick auf den Listen-Knopf, die Rückgängig-Kette
// des Browsers bleibt heil, und execCommand löst selbst ein input-Ereignis aus – ohne das
// bekäme der Auto-Save der Notiz die Änderung nie mit.

const EDITOR_SELECTOR = '.rz-html-editor-content';

// Blöcke, an denen ein "Zeilenanfang" gemessen wird.
const BLOCK_SELECTOR = 'p, div, li, blockquote, h1, h2, h3, h4, h5, h6';

const MARKERS = [
    { muster: /^[-*+]$/,    befehl: () => document.execCommand('insertUnorderedList') },
    { muster: /^\d+\.$/,    befehl: () => document.execCommand('insertOrderedList') },
    { muster: /^(#{1,3})$/, befehl: (t) => document.execCommand('formatBlock', false, `h${t.length}`) },
    { muster: /^>$/,        befehl: () => document.execCommand('formatBlock', false, 'blockquote') }
];

let attached = false;

// Der Editor-Bereich, in dem der Cursor steht – oder null, wenn woanders getippt wird.
function editorOf(node) {
    const element = node?.nodeType === Node.TEXT_NODE ? node.parentElement : node;
    return element?.closest?.(EDITOR_SELECTOR) ?? null;
}

// Text zwischen Blockanfang und Cursor. Genau daran hängt "steht am Zeilenanfang":
// Ist dort nur das Kürzel, fängt der Nutzer gerade eine Zeile an.
function textBeforeCaret(block, range) {
    const davor = document.createRange();
    davor.selectNodeContents(block);
    davor.setEnd(range.startContainer, range.startOffset);
    return davor;
}

function onKeyDown(event) {
    if (event.ctrlKey || event.altKey || event.metaKey) return;

    const selection = document.getSelection();
    if (!selection || selection.rangeCount === 0) return;

    const range = selection.getRangeAt(0);
    if (!range.collapsed) return;               // Auswahl: der Nutzer will ersetzen, nicht formatieren
    if (!editorOf(range.startContainer)) return;

    const startElement = range.startContainer.nodeType === Node.TEXT_NODE
        ? range.startContainer.parentElement
        : range.startContainer;
    const block = startElement?.closest(BLOCK_SELECTOR);
    if (!block) return;

    if (event.key === 'Tab') {
        handleTab(event, block);
        return;
    }

    if (event.key !== ' ') return;
    handleMarker(event, block, selection, range);
}

// Tab rückt nur INNERHALB einer Liste ein – sonst bleibt es der normale Fokuswechsel.
function handleTab(event, block) {
    if (!block.closest('li')) return;

    event.preventDefault();
    document.execCommand(event.shiftKey ? 'outdent' : 'indent');
}

function handleMarker(event, block, selection, range) {
    // In einer Liste würde "- " nur einen Aufzählungspunkt im Aufzählungspunkt erzeugen;
    // dort ist der Bindestrich wörtlich gemeint.
    if (block.closest('li')) return;

    const davor = textBeforeCaret(block, range);
    const kürzel = davor.toString();

    const treffer = MARKERS.find(m => m.muster.test(kürzel));
    if (!treffer) return;

    event.preventDefault();

    // Kürzel entfernen: als Auswahl löschen statt am Text herumzuschneiden, damit der
    // Schritt in derselben Rückgängig-Kette landet wie die Formatierung danach.
    selection.removeAllRanges();
    selection.addRange(davor);
    document.execCommand('delete');

    treffer.befehl(kürzel);
}

// Ein einziger Handler auf document reicht: Der Editor-DOM wird von Radzen laufend neu
// geschrieben, ein direkt am Element hängender Listener ginge dabei verloren.
export function attach() {
    if (attached) return;
    document.addEventListener('keydown', onKeyDown, true);
    attached = true;
}
