namespace DMShot.Settings;

/// <summary>
/// Wie viele Aufnahmen der Verlauf behält. Bis 0.9.7 war das die Konstante 10 im
/// HistoryStore; jetzt eine Einstellung (Zahl oder "unbegrenzt").
/// macOS parity implemented; see docs/PARITY.md.
/// </summary>
public static class HistoryLimit
{
    public const int Default = 10;
    public const int Min = 1;

    /// <summary>Obergrenze des Eingabefelds. Jeder Eintrag hält ein PNG in voller Auflösung
    /// vor, deshalb eine Zahl, die man noch versehentlich tippen kann, statt int.MaxValue.</summary>
    public const int Max = 999;

    /// <summary>Grenze für "unbegrenzt" — der Verlauf wirft dann nie etwas weg.</summary>
    public const int Unlimited = int.MaxValue;

    public static int Clamp(int value) => value < Min ? Min : value > Max ? Max : value;

    /// <summary>Die Grenze, mit der der HistoryStore arbeitet. Eine unbrauchbare Zahl aus einer
    /// beschädigten settings.json darf den Verlauf nicht auf null bringen, daher Clamp.</summary>
    public static int Effective(bool unlimited, int value) => unlimited ? Unlimited : Clamp(value);

    public static int Effective(Settings settings) => Effective(settings.HistoryUnlimited, settings.HistoryLimit);
}
