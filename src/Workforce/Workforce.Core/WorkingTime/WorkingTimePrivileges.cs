namespace Workforce.Core.WorkingTime;

/// <summary>Privileges of the working time account (class 2 data: the balance reveals working time and corrections).</summary>
public static class WorkingTimePrivileges
{
    public const string ReadOwn = "workingtime:read-own";
    public const string Read = "workingtime:read";
    public const string Book = "workingtime:book";
    public const string ManageRules = "workingtime:manage-rules";

    /// <summary>The privileges with what they allow in plain words (German, shown in Access when roles are put together).</summary>
    public static readonly IReadOnlyList<(string Key, bool Scopable, string Description)> Catalog =
    [
        (ReadOwn, false, "Das eigene Arbeitszeitkonto ansehen (Saldo, Buchungen, Monatsabschlüsse)."),
        (Read, true, "Das Arbeitszeitkonto von Personen in der Reichweite ansehen (Saldo, Buchungen, Monatsabschlüsse; HR)."),
        (Book, true, "Im Arbeitszeitkonto von Personen in der Reichweite buchen: Anfangsbestand, Monatsabschluss mit den gearbeiteten Stunden, Freizeitausgleich, Auszahlung, Auszahlung bei Austritt und Korrekturen (HR)."),
        (ManageRules, false, "Die Regeln des Arbeitszeitkontos für alle festlegen (HR): Auszahlungsschwelle (Standard: 0 Stunden, also immer), Verfall von Überstunden (Standard: nie) und die Monatsschwelle je Arbeitsverhältnis, ab der Stunden als Überstunden zählen."),
    ];
}
