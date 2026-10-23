# BC-Fixtures (WireMock.Net)

Antworten des Business-Central-Ersatzes als Dateien, ein Ordner pro Szenario (Mapping-Format von WireMock.Net).
Die Tests in `tests/BcIntegration.Infrastructure.Tests` laden je Test genau ein Szenario.

| Szenario | Inhalt |
| --- | --- |
| `employees-success` | zwei Mitarbeitende (leeres BC-Datum `0001-01-01`, leere Filiale) |
| `employees-empty` | leere Ergebnisliste |
| `employees-pagination` | zwei Seiten über `@odata.nextLink` (absolute URL wie bei BC) |
| `employees-pagination-loop` | `@odata.nextLink` zeigt auf sich selbst (Schutz gegen Endlosschleifen) |
| `employees-invalid-json` | 200 mit HTML statt JSON |
| `employees-throttled` | 429 mit `Retry-After: 1`, danach Erfolg |
| `employees-server-error` | immer 500 |
| `employees-timeout` | Antwort erst nach 2 s |
| `employees-expired-token` | 401 für das alte Token, Erfolg mit neuem Token |
| `employees-unauthorized` | immer 401 |
| `master-data` | eine Filiale und die zwei Mitarbeitenden aus `employees-success` |
| `pin-verify` | PIN-Prüfung: Personalnummer 10042 mit PIN 4711 ist gültig, alles andere nicht |
| `pin-verify-server-error` | PIN-Prüfung antwortet 500 (Fehlertext enthält absichtlich PIN und Namen) |
| `absence-create` / `absence-update` | Upsert über die externe ID (POST bzw. PATCH mit ETag) |
| `absence-validation-error` | BC-Validierungsfehler (400) |
| `absence-throttled-write` | 429 auf POST, danach Erfolg |
| `absence-server-error` | 500 auf POST |

Alle Endpunkte, Felder und Fehlercodes sind Annahmen (`TODO(BC-Modell)`): Mitarbeitende nach Standard-API v2.0, Filialen
und Abwesenheiten über angenommene eigene API-Seiten. Die Fehlertexte enthalten absichtlich personenbezogene Inhalte, damit
die Tests prüfen können, dass sie nirgends weitergegeben werden.
