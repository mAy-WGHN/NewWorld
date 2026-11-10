# Mock-Login (nur lokale Entwicklung)

Anmeldung im Frontend (`http://localhost:4200`) mit **Personalnummer + PIN**. Die PIN ist für alle Testpersonen **`4711`**. Business Central wird lokal durch WireMock ersetzt (`tests/Fixtures/Bc/e2e-landscape`). Das sind Testwerte, **keine echten Zugänge**, nirgends sonst gültig und nicht in `appsettings*.json` oder andere Umgebungen zu übernehmen.

| Personalnummer | Person | Rolle lokal | Filiale |
| --- | --- | --- | --- |
| `10099` | Anna Admin | Administrator (Recht `*`) | keine |
| `10044` | Ben Kollege | Personalabteilung, genehmigt Urlaub für alle | F017 |
| `10042` | Erika Mustermann | Public User | F017 |
| `10043` | Max Filialleiter | Public User (eine Filialleiter-Rolle gibt es nicht) | F017 |
| `10045` | Clara Drossel | Public User | F017 |
| `10046` | Dora Fehler | Public User | F017 |

- Rollen und die HR-Person richtet der AppHost beim Start selbst ein (Seed aus `docs/rollen.json`, nur lokal). Alle anderen Personen haben nur „Public User“.
- Den Urlaubsanspruch gibt es nicht automatisch, er wird per API gesetzt (`know-how.md`).
- Weitere Personen oder andere PINs: `pin-<Nummer>.json` und einen Eintrag in `employees.json` unter `tests/Fixtures/Bc/e2e-landscape/` anlegen (die Dateien nutzen auch die End-to-End-Tests).
- Start, Ports und Fehlersuche: `know-how.md`.
