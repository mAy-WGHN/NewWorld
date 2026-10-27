# Urlaubsberechnung (Workforce)

Stand 2026-10-27, Entscheidung 24 in `CLAUDE.md`. Code: `Workforce.Core/Absences/VacationEntitlement.cs` und `VacationAccount.cs`; Tests: `VacationEntitlementTests`, `VacationAccountTests`, `AbsenceServiceTests`.

## Bausteine

| Baustein | Quelle | Bedeutung |
| --- | --- | --- |
| **Jahresanspruch laut Vertrag** | HR (`PUT …/vacation-entitlements/{year}`) | z. B. 25 (5-Tage-Woche) oder 30 (6-Tage-Woche); 23,5 für „28 Tage auf 5 Tage umgerechnet“ (HR trägt den fertigen Wert ein) |
| **Eintrittsdatum** | BC (`EmployeeChanged.EntryDate`) | nur für das Eintrittsjahr; unbekannt = ganzes Jahr |
| **Zusatztage** | HR (`…/vacation-extra-days`) | `CarryOver` (Resturlaub aus Jahr Y) oder `Additional` (Zusatz-/Sonderurlaub für Jahr Y); ganze oder halbe Tage |

## Regeln

1. **Vertragliche Tage des Jahres** = Jahresanspruch, außer im Eintrittsjahr: `Jahresanspruch / 12 × volle Monate`, **aufgerundet auf halbe Tage**, höchstens der Jahresanspruch. Der Eintrittsmonat zählt nur, wenn der Eintritt am 1. liegt. Vor dem Eintrittsjahr: 0.
2. **Zusatztage** gelten ab `UsableFrom` (`CarryOver`: 1.1. des Folgejahres, `Additional`: 1.1. des Jahres). **Ob und wann sie verfallen, pflegt HR** (`GET/PUT /api/vacation-rules`, Recht `absence:manage-vacation-rules`): Monat und Tag im Jahr nach dem Bezugsjahr, z. B. 31.3. für „nach dem ersten Quartal des nächsten Jahres“. **Standard: sie verfallen nie.** Ein 29.2. fällt in Jahren ohne Schalttag auf den 28.2. Was bis zum Termin nicht verbraucht ist, ist verloren. Die Regel gilt für alle Zusatztage, auch die schon erfassten (rückwirkend).
3. **Verbrauch**: jeder Urlaubs-Arbeitstag nimmt zuerst aus Zusatztagen (der früher verfallende zuerst), erst danach aus den vertraglichen Tagen des Jahres. Urlaub vor `UsableFrom` verbraucht sie nie (er wurde aus dem Vertrag seines Jahres bezahlt).
4. **Effektive Urlaubstage** = vertragliche Tage + Zusatztage, die noch zählen (zu Jahresbeginn vorhanden, abzüglich verfallener Reste).
5. **Genommen** = genehmigter Urlaub vor heute, **geplant** = genehmigt ab heute plus beantragt; **Rest** = effektive Tage − genommen − geplant (kann negativ werden, wenn der Vertrag nachträglich gesenkt wird).
6. **Antrag prüfen**: Der Antrag wird wie gebucht gerechnet; Zusatztage, die vor dem Ende des beantragten Zeitraums verfallen, zählen für ihn nicht (der unverbrauchte Rest ist dann verloren). Ein Antrag im April kann also nicht auf Resturlaub bauen, der am 31.3. verfällt.
7. **Rundung anteiliger Tage** (Eintrittsjahr, Änderungen im Jahr): Standard aufrunden auf halbe Tage; HR kann `Up`, `Nearest`, `Down` oder `None` mit Schritt 0,5 oder 1 wählen. Ein volles Jahr ohne Änderung bleibt, wie eingegeben, ungerundet.
8. **Änderung des Jahresanspruchs im Jahr** (z. B. 30 → 25 bei Wechsel von 6 auf 5 Tage): HR trägt Datum und neuen Jahresanspruch ein (`vacation-entitlement-changes`). Ab dem 1. des Monats; liegt das Datum mitten im Monat, ab dem Folgemonat; nur im Jahr des Datums. Anspruch des Jahres = Summe der jeweils geltenden Jahreswerte je Monat ÷ 12 (nur Monate der Beschäftigung), gerundet nach Regel 7, höchstens der höchste Jahreswert. Beispiel: 25 bis Juni, 20 ab Juli → (25 × 6 + 20 × 6) / 12 = 22,5.
9. **Wartezeit** (HR: Monate, Modus): Vor Ablauf (Eintritt + Monate) prüft ein Antrag: `Blocked` = kein Urlaub; `Accrued` = beantragte plus bereits gebuchte Tage dürfen die bis dahin erworbenen nicht übersteigen (Jahresanspruch ÷ 12 × vollendete Monate seit Eintritt, Monat vollendet am Tag des Eintrittstages, gerundet nach Regel 7). Standard: keine Wartezeit. Das angezeigte Konto bleibt unverändert.
10. Arbeitstage zählen nach dem Kalender der Person (Mo–Sa, Feiertage je Bundesland/Filiale, Filialen mit Feiertagsöffnung siehe Entscheidung 23).

## Beispiele (aus dem Code berechnet)

**Eintrittsjahr, Jahresanspruch 25**

| Eintritt | volle Monate | 25/12 × Monate | Anspruch |
| --- | --- | --- | --- |
| 01.01. | 12 | 25,000 | 25 |
| 01.03. | 10 | 20,833 | 21 |
| 15.03. | 9 | 18,750 | 19 |
| 01.06. | 7 | 14,583 | 15 |
| 01.10. | 3 | 6,250 | 6,5 |
| 01.12. | 1 | 2,083 | 2,5 |
| 15.12. | 0 | 0,000 | 0 |

**Zusatztage und Verfall** (Regel „verfällt am 31.3.“, Jahresanspruch 25, Resturlaub aus 2025 = 4, Zusatzurlaub 2026 = 1; Urlaub 2.–4.2. (3 Tage) und 6.–11.7. (6 Tage))

| Stand | vertraglich | Zusatz zählt | verfallen | effektiv | genommen | geplant | Rest |
| --- | --- | --- | --- | --- | --- | --- | --- |
| 15.01.2026 | 25 | 5 | 0 | 30 | 0 | 9 | 21 |
| 31.03.2026 | 25 | 5 | 0 | 30 | 3 | 6 | 21 |
| 01.04.2026 | 25 | 4 | **1** | 29 | 3 | 6 | 20 |
| 31.12.2026 | 25 | 4 | 1 | 29 | 9 | 0 | 20 |

Im Februar verbrauchen die 3 Tage zuerst den Resturlaub (3 von 4); das eine übrige Resturlaubstag verfällt am 1.4. Das 1 Zusatztag 2026 wird im Juli verbraucht, vor den 25 vertraglichen Tagen. Im Konto 2027 steht der Rest des `Additional` 2026 (hier 0) bis 31.3.2027 zur Verfügung.

## Nicht umgesetzt / offen

- Anteiliger Anspruch bei **Austritt** im Jahr ist nicht gebaut (Regeln werden mit der Personalabteilung geklärt).
- Die Umrechnung des Anspruchs bei Wechsel der Wochentage (z. B. 6 = 30 / 5 = 25 oder 6 = 28 / 5 = 23,5) rechnet das System nicht selbst: HR trägt den neuen Jahresanspruch als Änderung (Regel 8) ein.
- Bereits vorhandene Personenkopien haben noch kein Eintrittsdatum, bis BC Integration die Person erneut meldet; bis dahin gilt der volle Jahresanspruch.
