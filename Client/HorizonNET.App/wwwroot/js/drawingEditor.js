// Interop für den js-draw-Zeichen-Editor (Phase 9f).
//
// Die ~500 KB große Bibliothek wird LAZY geladen – erst wenn wirklich eine Zeichnung
// geöffnet wird, nicht schon beim App-Start. bundle.js exponiert das globale `jsdraw`.
//
// Der Editor meldet an C# nur das Signal „hat sich geändert" (OnDrawingChanged); das
// eigentliche SVG holt C# erst beim Speichern per getSvg() – so geht nicht bei jedem
// Strich das ganze Dokument über die Interop-Grenze.

let libraryPromise = null;

// Lädt bundle.js (globales `jsdraw`) + Editor.css einmalig nach.
function ensureLibrary() {
    if (libraryPromise) return libraryPromise;

    libraryPromise = new Promise((resolve, reject) => {
        if (!document.querySelector('link[data-jsdraw]')) {
            const link = document.createElement('link');
            link.rel = 'stylesheet';
            link.href = 'lib/js-draw/Editor.css';
            link.setAttribute('data-jsdraw', '');
            document.head.appendChild(link);
        }

        if (window.jsdraw) { resolve(window.jsdraw); return; }

        const script = document.createElement('script');
        script.src = 'lib/js-draw/bundle.js';
        script.onload = () => resolve(window.jsdraw);
        script.onerror = () => reject(new Error('js-draw (bundle.js) konnte nicht geladen werden'));
        document.head.appendChild(script);
    });

    return libraryPromise;
}

// Erzeugt einen Editor im container, lädt optional ein bestehendes SVG und meldet
// Änderungen an dotNetRef. Ein evtl. schon vorhandener Editor wird vorher entfernt.
export async function init(container, svg, dotNetRef) {
    const jsdraw = await ensureLibrary();
    destroy(container);

    const editor = new jsdraw.Editor(container);
    editor.addToolbar();

    if (svg && svg.trim().length > 0) {
        try {
            await editor.loadFromSVG(svg);
        } catch (e) {
            console.warn('Zeichnung konnte nicht geladen werden:', e);
        }
    }

    // Nach jedem abgeschlossenen (bzw. rückgängig gemachten) Zeichen-Befehl „dirty" melden.
    const notify = () => {
        try { dotNetRef.invokeMethodAsync('OnDrawingChanged'); } catch { /* Kontext weg */ }
    };
    editor.notifier.on(jsdraw.EditorEventType.CommandDone, notify);
    editor.notifier.on(jsdraw.EditorEventType.CommandUndone, notify);

    container._jsdrawEditor = editor;
}

// Aktuelles SVG als String (beim Speichern aufgerufen).
export function getSvg(container) {
    const editor = container?._jsdrawEditor;
    return editor ? editor.toSVG().outerHTML : null;
}

// Kleines PNG (data:-URI) für die Listenvorschau – heruntergerechnet, damit nicht das
// volle Bild in der Notizliste landet.
export async function getThumbnail(container) {
    const editor = container?._jsdrawEditor;
    if (!editor) return null;
    try {
        const full = editor.toDataURL('image/png');
        return await downscale(full, 240, 160);
    } catch {
        return null;
    }
}

// Entfernt den Editor (räumt js-draws globale Listener auf) und leert den Container.
export function destroy(container) {
    if (!container) return;
    const editor = container._jsdrawEditor;
    if (editor) {
        try { editor.remove(); } catch { /* egal */ }
        container._jsdrawEditor = null;
    }
    container.innerHTML = '';
}

// Skaliert ein data:-PNG proportional auf max. w×h herunter.
function downscale(dataUrl, maxW, maxH) {
    return new Promise((resolve) => {
        const img = new Image();
        img.onload = () => {
            const ratio = Math.min(maxW / img.width, maxH / img.height, 1);
            const w = Math.max(1, Math.round(img.width * ratio));
            const h = Math.max(1, Math.round(img.height * ratio));
            const canvas = document.createElement('canvas');
            canvas.width = w;
            canvas.height = h;
            canvas.getContext('2d').drawImage(img, 0, 0, w, h);
            resolve(canvas.toDataURL('image/png'));
        };
        img.onerror = () => resolve(null);
        img.src = dataUrl;
    });
}
