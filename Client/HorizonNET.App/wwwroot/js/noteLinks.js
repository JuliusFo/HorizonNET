// Links im Notiz-Editor anklickbar machen.
//
// Der Editor-Inhalt liegt in einem contenteditable-Bereich. Browser behandeln einen Klick
// auf einen Link dort nicht als Navigation, sondern setzen den Cursor – der Link wirkt
// „tot". Dieser delegierte Handler öffnet ihn stattdessen in einem neuen Tab.
//
// Bewusst NICHT abgefangen wird ein Klick mit gedrückter Alt-Taste: das bleibt der Weg,
// den Cursor in einen Link zu setzen, um seinen Text zu bearbeiten.

const EDITOR_SELECTOR = '.rz-html-editor-content';

let attached = false;

function onClick(event) {
    if (event.altKey || event.button !== 0) return;

    const link = event.target.closest?.(`${EDITOR_SELECTOR} a[href]`);
    if (!link) return;

    const href = link.getAttribute('href');
    if (!href) return;

    event.preventDefault();
    event.stopPropagation();

    // noopener: das geöffnete Fenster darf nicht auf window.opener zugreifen.
    window.open(href, '_blank', 'noopener,noreferrer');
}

// Ein einziger Handler auf document reicht: der Editor-DOM wird von Radzen laufend
// neu geschrieben, ein direkt am Element hängender Listener ginge dabei verloren.
export function attach() {
    if (attached) return;
    document.addEventListener('click', onClick, true);
    attached = true;
}
