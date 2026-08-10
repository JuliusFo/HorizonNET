// Tastatur-Navigation im Journal: Alt+Pfeil links/rechts blättert einen Tag zurück
// bzw. vor, Alt+H springt auf heute.
//
// Bewusst MIT Alt: Die blanken Pfeiltasten gehören dem Editor – wer im Text navigiert,
// darf dabei nicht den Tag wechseln.

let handler;

export function register(dotNetRef) {
    unregister();
    handler = (e) => {
        if (!e.altKey || e.ctrlKey || e.metaKey) return;

        let method = null;
        if (e.key === 'ArrowLeft') method = 'GoPreviousDayAsync';
        else if (e.key === 'ArrowRight') method = 'GoNextDayAsync';
        else if (e.key && e.key.toLowerCase() === 'h') method = 'GoTodayAsync';

        if (!method) return;

        e.preventDefault();
        dotNetRef.invokeMethodAsync(method);
    };
    document.addEventListener('keydown', handler);
}

export function unregister() {
    if (handler) {
        document.removeEventListener('keydown', handler);
        handler = null;
    }
}
