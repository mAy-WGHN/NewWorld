# ClamAV für die AU-Dateien (Betrieb)

Jeder Upload einer Arbeitsunfähigkeitsbescheinigung geht vor der Verschlüsselung an `clamd` (INSTREAM). Jeder Fehler gilt als „nicht sauber“
(fail closed); solange `clamd` nicht antwortet, meldet `/health/ready` von Workforce `Degraded` und Uploads werden abgelehnt. Hintergrund:
`docs/krankmeldung-au.md`.

## Pflicht in Produktion

| Punkt | Wert |
| --- | --- |
| Image | `clamav/clamav` in derselben Version wie im AppHost (`src/AppHost/AppHost.cs`), Signaturen mit `freshclam` aktuell halten |
| Konfiguration | `infra/clamav/clamd.conf` als `/etc/clamav/clamd.conf` einbinden (Grenzen mindestens so groß wie `MaxFileBytes`) |
| Netz | Port 3310 nur im selben Pod oder in einem privaten Netz; die Verbindung ist **unverschlüsselt**, nie öffentlich erreichbar |
| Speicher | 1 bis 2 GB RAM für die Signaturdatenbank; Signaturen auf einem Volume, damit ein Neustart nicht neu lädt |
| Anbindung | `Workforce:Certificates:Scanner:Host` (Pflicht außerhalb von Development/Testing), `Port` (3310), `TimeoutSeconds` (30) |
| Keine Cloud-Scanner | Die Dateien verlassen den Dienst nicht; ein externer Scanner wäre eine Weitergabe von Gesundheitsdaten |

## Prüfen

- Nach dem Start: `/health/ready` von Workforce ist `Healthy` (nicht `Degraded`).
- Mit der EICAR-Testdatei hochladen: der Upload wird abgelehnt (`422`), es entsteht keine Datei im Speicher.
- Alarm einrichten auf `Degraded` von Workforce: Dann können Mitarbeitende keine AU hochladen.

## Beispiel (Compose, ein Pod/Netz mit Workforce)

```yaml
services:
  clamav:
    image: clamav/clamav:1.4
    volumes:
      - clamav-signatures:/var/lib/clamav
      - ./infra/clamav/clamd.conf:/etc/clamav/clamd.conf:ro
    # kein "ports": nur das interne Netz erreicht 3310
volumes:
  clamav-signatures:
```
