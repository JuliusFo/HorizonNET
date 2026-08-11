namespace HorizonNET.App.Components;

// Ein Eintrag in einer Auswahlliste: Id als Wert, Label als Beschriftung. Genau die
// beiden Eigenschaften, die die Radzen-Dropdowns über ValueProperty/TextProperty lesen.
//
// Bewusst EIN gemeinsamer Typ für Ordner- und Task-Auswahl: Vorher hatte jeder Editor
// seinen eigenen, formgleichen Record – und der Notiz-Editor kopierte die Ordner-Optionen
// Feld für Feld in seinen eigenen um, nur weil die Typen verschieden hießen.
public record SelectOption(int Id, string Label);
