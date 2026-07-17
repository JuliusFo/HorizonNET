namespace HorizonNET.Shared.Transfer.Enums;

// Art einer Übung. Entscheidet, welche Felder eines ExerciseSet überhaupt gelten,
// welche das Formular zeigt und welchen Leitwert die Auswertung bildet – die drei
// Arten haben bewusst keine gemeinsame Form (Laufen kennt keine Wiederholungen).
public enum ExerciseKind
{
    // Wiederholungen + Gewicht (Hanteln). Leitwert: geschätztes 1RM / Volumen.
    Strength = 0,

    // Wiederholungen, Gewicht optional als Zusatzgewicht (Liegestütze).
    // Leitwert: Wiederholungen je Einheit.
    Bodyweight = 1,

    // Strecke + Dauer (Laufen). Leitwert: Pace (min/km).
    Endurance = 2
}
