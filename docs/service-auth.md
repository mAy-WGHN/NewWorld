# Dienst-zu-Dienst-Authentifizierung (Entscheidung 18)

Stand: 2026-10-26. Ersetzt das Entra-Einrichtungspaket. Personen melden sich weiterhin nur über Access an (Entscheidung 13); hier geht es ausschließlich um Aufrufe **zwischen Diensten** (z. B. Access → BC Integration, Workforce → Access).

## Prinzip

Jeder Dienst hat ein eigenes **Schlüsselpaar** (ECDSA P-256, Algorithmus ES256).

- Der **private Schlüssel** bleibt beim Besitzer-Dienst (User Secrets, später Key Vault). Er signiert bei jedem Aufruf ein kurzlebiges Token.
- Die Dienste, die ihn aufrufen dürfen, kennen nur den **öffentlichen Schlüssel** und eine Liste von Rechten (Service Roles). Wird ein Empfänger kompromittiert, erfährt der Angreifer keine Geheimnisse der Aufrufer.

Ein Token ist ein JWT mit:

| Claim | Inhalt |
| --- | --- |
| `iss` | `svc:<name>` des Aufrufers (z. B. `svc:access`). Das Präfix hält Dienst-Tokens von Personen-Tokens (Issuer von Access) getrennt. |
| `aud` | Name des Ziels (z. B. `bc-integration`). Ein Token für einen anderen Dienst wird abgelehnt. |
| `nbf`/`iat`/`exp` | Gültig 2 Minuten. Der Empfänger akzeptiert höchstens 5 Minuten Laufzeit und 30 Sekunden Uhrenabweichung. |
| `jti` | Eindeutige ID. Jedes Token wird nur **einmal** akzeptiert (Replay-Schutz je Instanz, bis es abgelaufen ist). |

Pro Aufruf (auch pro Wiederholung durch die Resilience-Pipeline) wird ein neues Token signiert. Die Prüfung erlaubt nur ES256; unsignierte oder symmetrisch signierte Tokens (`none`, `HS256`) werden abgelehnt. Fehlerursachen stehen nur als Kurzcode im Log (`Service token rejected (<Code>)`), nie das Token.

## Was Entra vorher geleistet hat und wie es jetzt geregelt ist

| Früher (Entra) | Jetzt |
| --- | --- |
| Managed Identity je Dienst | Schlüsselpaar je Dienst |
| App Role je Fähigkeit, vergeben im Tenant | Service Role je Fähigkeit im Code (`AccessRoles`, `BcIntegrationRoles`, `WorkforceRoles`); **wer sie hat**, steht in der Konfiguration des Empfängers (`ServiceAuth:Peers:<aufrufer>:Roles`) |
| Tokens vom Entra-Tenant | Tokens vom Aufrufer selbst, mit dessen Schlüssel signiert |
| Prüfung über Tenant/JWKS | Prüfung lokal mit dem öffentlichen Schlüssel aus der Konfiguration |

Unverändert: Der Zugriff von BC Integration auf **Business Central** (Cloud) braucht weiterhin ein Entra-Token (`Bc:Credential`, nur dort). Das gilt auch für Service Bus mit Managed Identity, sobald in Azure gehostet wird (siehe `infra/servicebus.bicep`, Entscheidung 8).

## Konfiguration

Abschnitt `ServiceAuth`, nur über User Secrets, Umgebungsvariablen bzw. Key Vault (nie `appsettings*.json`):

```
ServiceAuth:Name                         = access                 # eigener Name (Audience für Aufrufer)
ServiceAuth:PrivateKey                   = <base64 PKCS#8>        # nur Dienste, die andere aufrufen
ServiceAuth:Peers:workforce:PublicKeys:0 = <base64 SPKI>          # Dienste, die diesen aufrufen dürfen
ServiceAuth:Peers:workforce:Roles:0      = Access.Catalog.Register
```

Umgebungsvariablen: `ServiceAuth__Peers__workforce__PublicKeys__0` usw. Mehrere öffentliche Schlüssel pro Aufrufer sind erlaubt (Rotation, siehe unten).

Der Dienst **startet nicht**, wenn ein Schlüssel nicht lesbar ist, ein Aufrufer keinen Schlüssel hat, `Name` fehlt oder einem Aufrufer eine Rolle gegeben wird, die der Dienst nicht kennt. Fehlermeldungen nennen die Einstellung, nie den Wert.

### Wer ruft wen auf (heute)

| Empfänger | Aufrufer | Rollen |
| --- | --- | --- |
| `access` | `workforce`, `notifications` | `Access.Catalog.Register`, `Access.Permissions.Read` (für den Neuaufbau der Berechtigungskopie, Aufrufer-Code folgt) |
| `bc-integration` | `access` | `BcIntegration.Pin.Verify` |
| `bc-integration` | Betreiber (noch keine Zuweisung) | `BcIntegration.EmployeeErasure.Request` |
| `workforce` | später die HR API | `Workforce.AbsenceDetails.Read` |

Lokal setzt der AppHost genau diese Konfiguration (`src/AppHost/AppHost.cs`) mit Schlüsseln, die bei jedem Start neu erzeugt werden. Tests der APIs nutzen den Test-Auth-Handler (`Shared.Testing`), die End-to-End-Tests laufen mit echten Tokens.

## Schlüssel erzeugen und verteilen

```powershell
./scripts/New-ServiceKey.ps1 -Name access
```

Das Skript schreibt das Paar in zwei Dateien (`<name>.private.key`, `<name>.public.key`), zeigt nichts davon in der Konsole und legt die Dateien nicht ins Repo (`*.key` ist in `.gitignore`). Den **privaten** Schlüssel trägt man nur beim Besitzer-Dienst ein (`ServiceAuth:PrivateKey`), den **öffentlichen** bei jedem Empfänger unter `ServiceAuth:Peers:<name>:PublicKeys`. Danach die privaten Dateien löschen.

### Rotation ohne Ausfall

1. Neues Paar erzeugen.
2. Den neuen **öffentlichen** Schlüssel bei allen Empfängern zusätzlich eintragen (`PublicKeys:1`), Empfänger neu starten.
3. Beim Aufrufer den neuen **privaten** Schlüssel eintragen, neu starten.
4. Den alten öffentlichen Schlüssel bei den Empfängern entfernen.

## Voraussetzungen und Grenzen

- **TLS zwischen den Diensten ist Pflicht.** Ein Token ist nur wenige Minuten und nur einmal nutzbar, aber ohne TLS wären Inhalt und Token mitlesbar.
- **Replay-Schutz gilt je Instanz.** Läuft ein Dienst mehrfach, könnte ein abgefangenes Token innerhalb seiner Laufzeit (2 Minuten) an einer anderen Instanz noch einmal akzeptiert werden. Wer das ausschließen will, braucht eine gemeinsame Ablage der `jti` (offen).
- **Die Token binden nicht an die einzelne Anfrage** (Methode, Pfad, Body). Ein Angreifer mit Netzwerkzugriff müsste ein frisches Token abfangen, bevor es verwendet wird.
- **Die öffentlichen Schlüssel liegen in der Konfiguration der Empfänger.** Wer diese ändern kann, kann Aufrufer hinzufügen. Die Konfiguration gehört deshalb in einen geschützten Speicher (Key Vault, Deployment-Pipeline).
- Neue Endpunkte zwischen Diensten bekommen weiterhin eine eigene Service Role und einen Eintrag in der Tabelle oben.
