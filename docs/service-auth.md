# Dienst-zu-Dienst-Authentifizierung

Entscheidung 18 (`CLAUDE.md`), überarbeitet 2026-11-10. Personen melden sich nur über Access an (Entscheidung 13); hier geht es um Aufrufe **zwischen Diensten** (z. B. Access → BC Integration, Workforce → Access).

## Prinzip

Jeder Dienst hat ein eigenes **Schlüsselpaar** (ECDSA P-256, ES256). Der **private** Schlüssel bleibt beim Dienst (User Secrets, später Key Vault) und signiert bei jedem Aufruf ein kurzlebiges Token. Wer den Dienst aufrufen darf, kennt nur den **öffentlichen** Schlüssel und die gewährten **Service Roles**; wird ein Empfänger kompromittiert, erfährt der Angreifer keine Geheimnisse der Aufrufer.

| Claim | Inhalt |
| --- | --- |
| `iss` | `svc:<name>` des Aufrufers (das Präfix trennt Dienst- von Personen-Tokens) |
| `aud` | Name des Ziels; ein Token für einen anderen Dienst wird abgelehnt |
| `nbf`/`iat`/`exp` | gültig 2 Minuten; der Empfänger akzeptiert höchstens 5 Minuten Laufzeit und 30 Sekunden Uhrenabweichung |
| `jti` | eindeutige ID; jedes Token wird nur **einmal** akzeptiert (Replay-Schutz je Instanz) |

Pro Aufruf, auch pro Wiederholung der Resilience-Pipeline, wird ein neues Token signiert. Erlaubt ist nur ES256 (`none` und `HS256` werden abgelehnt). Im Log steht nur ein Kurzcode (`Service token rejected (<Code>)`), nie das Token. Die Rollen je Fähigkeit stehen im Code (`AccessRoles`, `BcIntegrationRoles`, `WorkforceRoles`), **wer sie hat**, in der Konfiguration des Empfängers.

Entra bleibt nur dort, wo ein externer Dienst es verlangt: BC (Cloud, `Bc:Credential`), Service Bus mit Managed Identity bei Azure-Hosting (`infra/servicebus.bicep`, Entscheidung 8) und der zweite Faktor für die Lohnabrechnung (`zweiter-faktor-entra.md`).

## Konfiguration

Abschnitt `ServiceAuth`, nur über User Secrets, Umgebungsvariablen oder Key Vault (nie `appsettings*.json`):

```
ServiceAuth:Name                         = access                 # eigener Name (Audience für Aufrufer)
ServiceAuth:PrivateKey                   = <base64 PKCS#8>        # nur Dienste, die andere aufrufen
ServiceAuth:Peers:workforce:PublicKeys:0 = <base64 SPKI>          # Dienste, die diesen aufrufen dürfen
ServiceAuth:Peers:workforce:Roles:0      = Access.Catalog.Register
```

Umgebungsvariablen: `ServiceAuth__Peers__workforce__PublicKeys__0` usw.; mehrere öffentliche Schlüssel je Aufrufer sind für die Rotation erlaubt. Der Dienst **startet nicht**, wenn ein Schlüssel nicht lesbar ist, ein Aufrufer keinen Schlüssel hat, `Name` fehlt oder eine Rolle vergeben wird, die der Dienst nicht kennt; Fehlermeldungen nennen die Einstellung, nie den Wert.

**Wer ruft wen auf (heute):**

| Empfänger | Aufrufer | Rollen |
| --- | --- | --- |
| `access` | `workforce`, `notifications`, `portal` | `Access.Catalog.Register`, `Access.Permissions.Read` (Neuaufbau der Berechtigungskopie, Aufrufer-Code folgt) |
| `bc-integration` | `access` | `BcIntegration.Pin.Verify` |
| `bc-integration` | Betreiber (noch keine Zuweisung) | `BcIntegration.EmployeeErasure.Request` |
| `workforce` | später die HR API | `Workforce.AbsenceDetails.Read` |

Lokal setzt der AppHost genau diese Konfiguration (`src/AppHost/AppHost.cs`) mit bei jedem Start neu erzeugten Schlüsseln. Die API-Tests nutzen den Test-Auth-Handler (`Shared.Testing`), die End-to-End-Tests echte Tokens. Neue Endpunkte zwischen Diensten bekommen eine eigene Service Role und einen Eintrag in dieser Tabelle.

## Schlüssel erzeugen, verteilen, rotieren

```powershell
./scripts/New-ServiceKey.ps1 -Name access
```

Das Skript schreibt `<name>.private.key` und `<name>.public.key` (zeigt nichts in der Konsole, `*.key` steht in der `.gitignore`). Den **privaten** Schlüssel trägt man nur beim Besitzer ein (`ServiceAuth:PrivateKey`), den **öffentlichen** bei jedem Empfänger (`ServiceAuth:Peers:<name>:PublicKeys`); danach die privaten Dateien löschen.

**Rotation ohne Ausfall:** (1) neues Paar erzeugen; (2) den neuen öffentlichen Schlüssel bei allen Empfängern zusätzlich eintragen (`PublicKeys:1`) und neu starten; (3) beim Aufrufer den neuen privaten Schlüssel eintragen und neu starten; (4) den alten öffentlichen Schlüssel bei den Empfängern entfernen.

## Voraussetzungen und Grenzen

- **TLS zwischen den Diensten ist Pflicht** (ohne wären Inhalt und Token mitlesbar).
- **Replay-Schutz gilt je Instanz;** bei mehreren Instanzen könnte ein abgefangenes Token innerhalb seiner 2 Minuten an einer anderen Instanz noch einmal akzeptiert werden. Eine gemeinsame Ablage der `jti` wäre nötig (offen).
- Die Token binden nicht an die einzelne Anfrage (Methode, Pfad, Body).
- Die öffentlichen Schlüssel liegen in der Konfiguration der Empfänger; wer sie ändern kann, kann Aufrufer hinzufügen. Die Konfiguration gehört deshalb in einen geschützten Speicher.
