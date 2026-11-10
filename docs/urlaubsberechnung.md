# Urlaubsberechnung (Workforce)

Entscheidungen 23, 24, 28 und 39 in `CLAUDE.md`, überarbeitet 2026-11-10. Code: `Workforce.Core/Absences/VacationEntitlement.cs` und `VacationAccount.cs`; Tests: `VacationEntitlementTests`, `VacationAccountTests`, `AbsenceServiceTests`. Alle Regeln gelten rückwirkend, das Konto wird aus den Zeiträumen berechnet.

## Bausteine

| Baustein | Quelle | Bedeutung |
| --- | --- | --- |
| Jahresanspruch laut Vertrag | HR (`PUT /api/employees/{id}/vacation-entitlements/{year}`) | z. B. 25 (5-Tage-Woche), 30 (6-Tage-Woche) oder 23,5; HR trägt den fertigen Wert ein |
| Eintritts- und Austrittsdatum | BC (`EmployeeChanged`) | nur für Eintritts- und Austrittsjahr; unbekannt = ganzes Jahr |
| Zusatztage | HR (`…/vacation-extra-days`) | `CarryOver` (Resturlaub aus Jahr Y) oder `Additional` (Zusatz-/Sonderurlaub für Jahr Y), ganze oder halbe Tage |

## Regeln

1. **Vertragliche Tage** = Jahresanspruch. Im Eintrittsjahr `Jahresanspruch ÷ 12 × volle Monate`, aufgerundet nach Regel 7, höchstens der Jahresanspruch; der Eintrittsmonat zählt nur bei Eintritt am 1. Vor dem Eintrittsjahr: 0.
2. **Austritt im Jahr** (Entscheidung 39): dieselbe Formel; der Austrittsmonat zählt nur, wenn der Austritt am **letzten Tag des Monats** liegt (im Schaltjahr ist der 29.2. der letzte Tag, der 28. nicht). Eintritt und Austritt im selben Jahr zählen die Monate dazwischen. Für Folgejahre gibt es nichts mehr, für frühere Jahre den vollen Anspruch. Beispiele bei 25 Tagen: Austritt 31.3. → 6,5; 30.3. → 4,5; 31.12. → 25; 1.3. bis 31.8. → 12,5.
3. **Zusatztage** gelten ab `UsableFrom` (`CarryOver`: 1.1. des Folgejahres, `Additional`: 1.1. des Jahres). **Verfall pflegt HR** (`GET/PUT /api/vacation-rules`, `absence:manage-vacation-rules`): Monat und Tag im Jahr nach dem Bezugsjahr (z. B. 31.3.), **Standard: nie**; ein 29.2. fällt ohne Schalttag auf den 28.2. Die Regel gilt auch für schon erfasste Zusatztage.
4. **Verbrauch:** Jeder Urlaubs-Arbeitstag nimmt zuerst aus Zusatztagen (der früher verfallende zuerst), dann aus den vertraglichen Tagen. Urlaub vor `UsableFrom` verbraucht sie nie.
5. **Effektive Tage** = vertragliche Tage + Zusatztage, die noch zählen. **Genommen** = genehmigter Urlaub vor heute, **geplant** = genehmigt ab heute plus beantragt, **Rest** = effektive Tage − genommen − geplant (kann negativ werden, wenn der Vertrag nachträglich sinkt).
6. **Antrag prüfen:** wie gebucht gerechnet; Zusatztage, die vor dem Ende des beantragten Zeitraums verfallen, zählen nicht (ein Antrag im April baut nicht auf Resturlaub, der am 31.3. verfällt).
7. **Rundung anteiliger Tage:** Standard aufrunden auf halbe Tage; HR wählt `Up`, `Nearest`, `Down` oder `None` mit Schritt 0,5 oder 1. Ein volles Jahr ohne Änderung bleibt ungerundet.
8. **Änderung des Jahresanspruchs im Jahr** (z. B. 30 → 25 bei Wechsel von 6 auf 5 Tage; `…/vacation-entitlement-changes`): gilt ab dem 1. des Monats, bei Datum mitten im Monat ab dem Folgemonat, nur im Jahr des Datums. Anspruch = Summe der je Monat geltenden Jahreswerte ÷ 12 (nur Monate der Beschäftigung), gerundet nach Regel 7. Beispiel: 25 bis Juni, 20 ab Juli → (25 × 6 + 20 × 6) ÷ 12 = 22,5.
9. **Wartezeit** (HR: Monate und Modus, Standard keine): Vor Ablauf prüft ein Antrag: `Blocked` = kein Urlaub; `Accrued` = beantragte plus gebuchte Tage dürfen die bis dahin erworbenen (Jahresanspruch ÷ 12 × vollendete Monate) nicht übersteigen (`422 WaitingPeriod`). Das angezeigte Konto bleibt unverändert.
10. **Arbeitstage** zählen nach dem Kalender der Person: Mo–Sa, Feiertage für Bayern, Filialen mit Feiertagsöffnung (Entscheidung 23).

## Beispiele

**Eintrittsjahr, Jahresanspruch 25:** Eintritt 01.01. → 25; 01.03. (10 Monate, 20,833) → 21; 15.03. (9 Monate, 18,75) → 19; 01.06. → 15; 01.10. (3 Monate, 6,25) → 6,5; 01.12. (2,083) → 2,5; 15.12. → 0.

**Zusatztage und Verfall** (verfällt am 31.3., Anspruch 25, Resturlaub aus 2025 = 4, Zusatzurlaub 2026 = 1, Urlaub 2.–4.2. und 6.–11.7.):

| Stand | vertraglich | Zusatz zählt | effektiv | genommen | geplant | Rest |
| --- | --- | --- | --- | --- | --- | --- |
| 15.01.2026 | 25 | 5 | 30 | 0 | 9 | 21 |
| 31.03.2026 | 25 | 5 | 30 | 3 | 6 | 21 |
| 01.04.2026 | 25 | 4 (1 verfallen) | 29 | 3 | 6 | 20 |
| 31.12.2026 | 25 | 4 | 29 | 9 | 0 | 20 |

Im Februar verbrauchen die 3 Tage zuerst den Resturlaub; der eine übrige Tag verfällt am 1.4. Der Zusatztag 2026 wird im Juli vor den 25 vertraglichen Tagen verbraucht.

## Nicht umgesetzt

- Abgeltung von Resturlaub und Zusatztagen beim Austritt, Verrechnung von mehr genommenem Urlaub, gesetzlicher Mindesturlaub nach § 5 BUrlG (voller Anspruch bei Austritt nach dem 30.6.).
- Die Umrechnung bei Wechsel der Wochentage (6 = 30 / 5 = 25) macht das System nicht selbst: HR trägt den neuen Jahresanspruch als Änderung ein (Regel 8).
- Personenkopien ohne Eintrittsdatum zählen den vollen Jahresanspruch, bis BC Integration die Person erneut meldet.
