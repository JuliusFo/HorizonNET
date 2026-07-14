// Globale Tastenkürzel. Aktuell: Strg+K (bzw. Cmd+K) öffnet die Suchpalette.

let handler;

export function registerSearchHotkey(dotNetRef) {
    unregisterSearchHotkey();
    handler = (e) => {
        if ((e.ctrlKey || e.metaKey) && e.key && e.key.toLowerCase() === 'k') {
            e.preventDefault(); // sonst öffnet Firefox/Chrome die eigene Suchleiste
            dotNetRef.invokeMethodAsync('OpenFromHotkey');
        }
    };
    document.addEventListener('keydown', handler);
}

export function unregisterSearchHotkey() {
    if (handler) {
        document.removeEventListener('keydown', handler);
        handler = null;
    }
}

export function focusElement(el) {
    el?.focus();
}
