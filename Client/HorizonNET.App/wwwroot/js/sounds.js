// Kurze, synthetisch erzeugte UI-Sounds über die Web Audio API – keine Asset-Dateien nötig.
//
// Später auf echte Audio-Dateien umstellen: einfach in playSound(name) statt der Ton-
// Erzeugung `new Audio('/sounds/' + name + '.mp3').play()` verwenden. Die C#-Seite
// (SoundService) ruft nur playSound(name) auf und bleibt unverändert.

let ctx;

function audio() {
    ctx ??= new (window.AudioContext || window.webkitAudioContext)();
    if (ctx.state === 'suspended') ctx.resume();
    return ctx;
}

// Spielt eine Folge kurzer Töne (freqs in Hz) als Arpeggio bzw. – bei step=0 – als Akkord.
function play(freqs, { step = 0, dur = 0.16, type = 'sine', gain = 0.14 } = {}) {
    const ac = audio();
    const now = ac.currentTime;
    freqs.forEach((f, i) => {
        const osc = ac.createOscillator();
        const g = ac.createGain();
        osc.type = type;
        osc.frequency.value = f;
        const start = now + i * step;
        g.gain.setValueAtTime(0, start);
        g.gain.linearRampToValueAtTime(gain, start + 0.012);
        g.gain.exponentialRampToValueAtTime(0.0001, start + dur);
        osc.connect(g).connect(ac.destination);
        osc.start(start);
        osc.stop(start + dur + 0.02);
    });
}

export function playSound(name) {
    try {
        switch (name) {
            case 'success':   play([660, 880], { step: 0.07, dur: 0.18 }); break;          // heller Doppelton
            case 'daily':     play([880], { dur: 0.10, type: 'triangle' }); break;         // kurzer Tick
            case 'celebrate': play([523, 659, 784, 1047], { step: 0.10, dur: 0.16 }); break; // kleine Fanfare (C-Dur)
            case 'error':     play([180, 150], { step: 0.10, dur: 0.22, type: 'sawtooth', gain: 0.12 }); break; // tief/dissonant
        }
    } catch {
        // Sound ist optional – Fehler (z. B. blockierter AudioContext) still ignorieren.
    }
}
