---
name: commit-vorschlag
description: Schlägt für alle uncommitteten Änderungen des Portals (geändert, gestaged, untracked) eine sinnvolle Aufteilung in Commits samt Commit-Message vor. Verwenden, wenn der Nutzer einen Commit-Vorschlag, eine Commit-Aufteilung oder Commit-Messages für seinen aktuellen Stand haben möchte, z. B. „mach mir einen Commit-Vorschlag“, „wie soll ich das committen?“, „gehört das alles in einen Commit?“, /commit-vorschlag.
---

# Commit-Vorschlag (Portal)

Ziel: Der Nutzer bekommt für **jede** uncommittete Datei einen Vorschlag, in welchen Commit sie
gehört, und für jeden Commit eine fertige Message. **Es wird nichts committet, gestaged oder
gepusht** — der Nutzer committet in diesem Repo selbst. Auch nicht anbieten, es zu übernehmen.

## 1. Stand erfassen

Parallel ausführen:

```bash
git status --porcelain=v1 -uall
git diff --stat
git diff --cached --stat
git log --format='%h %s%n%b---' -15
git status -sb
```

- Keine Änderungen → kurz sagen, dass der Arbeitsbaum sauber ist, und aufhören.
- `git status -sb` zeigt, ob lokale Commits noch nicht gepusht sind (`[ahead N]`) — wichtig für
  die Migrations-Prüfung in Schritt 3.
- Warnungen „LF will be replaced by CRLF“ sind hier normal (`core.autocrlf=true`) und kein Hinweis
  auf eine Änderung. Nicht erwähnen, außer `git diff --stat` zeigt eine Datei komplett als geändert.

## 2. Änderungen verstehen

- Für geänderte Dateien `git diff -- <datei>` lesen (bei gestagten zusätzlich
  `git diff --cached -- <datei>`), untracked Dateien lesen. Viele Dateien → Diffs gebündelt holen.
- **Nicht committen / prüfen** — diese Dateien keinem Commit zuordnen, sondern separat nennen:
  - `.env` (Zugangsdaten zu BC — niemals committen), `bin/`, `obj/`, `publish/`, `app.zip`, `.vs/`
    (alle per `.gitignore` ausgeschlossen, tauchen normalerweise nicht auf).
  - `*.xlsx`/`*.csv` (Lagerbestandsjournale, Auswertungen wie `Einheiten_Faktor0.csv`, meist unter
    `docs/spreadsheets/`) sind bewusst ignoriert — echte Filialdaten gehören nicht ins Repo.
  - `Portal.csproj.user` ist trotz `*.user` getrackt. Taucht die Datei als geändert auf, ist
    das meist nur die lokale Startkonfiguration der IDE → unter „prüfen“ nennen, nicht einplanen.
  - Debug-Reste: `Console.WriteLine`, auskommentierter Code, `// TODO test`, geänderte
    `appsettings.Development.json` mit lokalen Werten.

## 3. In Commits gruppieren

Grundregeln:
- Ein Commit = eine logische Änderung, die **für sich baut und deren Tests grün sind** (Hauptprojekt
  und `Portal.Tests/` bauen zusammen). Lieber mehrere Commits als einen Sammelcommit, aber
  keine künstliche Zerlegung.
- Reihenfolge nach Abhängigkeiten: was anderes Code braucht, kommt zuerst.

Repo-spezifisch zusammenhalten (**nie** auf verschiedene Commits verteilen):
- **Entity + `InventoryDbContext` + Migration (`<Zeitstempel>_<Name>.cs` und `.Designer.cs`) +
  `InventoryDbContextModelSnapshot.cs`** — der Snapshot passt sonst nicht zur Migration, und die
  nächste `dotnet ef migrations add` erzeugt Müll.
- **Code + Tests + Doku desselben Features.** Laut `CLAUDE.md` wird die Doku mit der Änderung
  gepflegt, sie gehört deshalb in denselben Commit. Die Doku liegt in `docs/` (Übersicht in `CLAUDE.md`):
  `docs/API_DOKUMENTATION.md` (technische Gesamtdoku, inkl. Supervisor-Web-App-Schnittstelle — Abschnitte
  nur für eine Zielgruppe sind mit `[App]`/`[Web-App]`/`[BC-Team]` markiert),
  `docs/need-to-know.md` (Verhalten beim Zählen), `docs/tasks.md` (offene Punkte, Stand, Risiken),
  `docs/BC_BUCHUNGSSCHNITTSTELLE.md` (Absprachen mit dem BC-Team).
- Enthält eine Doku-Datei Abschnitte zu **verschiedenen** Themen (z. B. `docs/need-to-know.md` mit einem
  fertigen und einem nur geplanten Abschnitt), das benennen und die Abschnitte grob beschreiben.
  Eigene Doku-Commits (`docs: …`) nur für Doku, die zu keinem Code-Commit gehört (z. B. ein reiner
  Statusbericht oder Planungstext).

Prüfungen, deren Ergebnis unter „Hinweise“ gehört:
- **Migration geändert oder gelöscht, die schon gepusht ist?** Für jede geänderte/gelöschte Datei in
  `Data/Migrations/` (außer dem Snapshot) `git log origin/main --oneline -- <datei>` prüfen. Ist sie
  auf `origin/main`, **deutlich warnen**: eine gepushte Migration kann schon auf Azure oder bei
  anderen angewendet sein — statt sie zu ändern, eine neue Folgemigration anlegen.
- **Neue Migration:** kurz daran erinnern, die Defaults neuer Non-Nullable-Spalten zu prüfen
  (Falle aus `CLAUDE.md`: Generator setzt `""`/`false`/`0` statt des C#-Defaults), und dass sie vor
  dem Deploy auf Azure angewendet werden muss.
- **Endpunkte geändert** (`Controllers/`, Request-/Response-DTOs in `Contracts/`), aber
  `docs/API_DOKUMENTATION.md` bzw. die getrackte `Postman-Collection/` nicht? Als möglicherweise
  vergessene Doku nennen — nicht selbst ändern.

Eine Datei mit Änderungen für mehrere Commits: benennen und Hunks grob beschreiben. `git add -p` ist
interaktiv — der Nutzer kann es selbst ausführen. Ist die Trennung unsauber, die Datei dem
Hauptcommit zuordnen und das erwähnen.

Jede uncommittete Datei muss genau einmal vorkommen — in einem Commit oder unter „nicht committen /
prüfen“. Am Ende gegen `git status --porcelain=v1 -uall` abgleichen.

## 4. Commit-Messages

Stil dieses Repos (aus der Historie, im Zweifel `git log` erneut ansehen):
- **Deutsch**, Conventional Commits mit Scope: `feat(counts): …`, `feat(auth): …`,
  `feat(teams): …`, `feat(stores): …`, `feat(bc-sync): …`, `fix(…): …`, `test: …`, `docs: …`,
  `docs(need-to-know): …`, `chore: …`. Scope = fachlicher Bereich, nicht Dateiname.
- **Nur eine Betreffzeile**, kein Body. Beschreibt das Ergebnis, nicht die Dateien
  (`feat(counts): Einheitenumrechnung beim Zählen, Mengen immer in der Basiseinheit`).
  Max. ~90 Zeichen, kein Punkt am Ende.
- **Keine `Co-Authored-By`- oder sonstigen Attribution-Zeilen** — der Nutzer committet selbst.
- Commits wie `pt.2` oder `quality of life` in der Historie sind keine Vorlage.

## 5. Ausgabe

Für jeden Commit in der vorgeschlagenen Reihenfolge:

````markdown
### 1. feat(finalize): Übertragung nach BC über Warteschlange, Freigabe, Retry und Nachzügler

Dateien:
- `Domain/Entities/InventoryPostingJob.cs` (neu)
- `Data/Migrations/20260927082355_AddPostingJobsAndLateEvents.cs` + `.Designer.cs` (neu), `InventoryDbContextModelSnapshot.cs`
- `Services/InventoryPostingProcessor.cs` (neu), …
- `Portal.Tests/Services/InventoryPostingTests.cs` (neu)
- `docs/API_DOKUMENTATION.md`, `docs/tasks.md`

```bash
git add Domain/Entities/InventoryPostingJob.cs Data/Migrations/20260927082355_AddPostingJobsAndLateEvents.cs …
git commit -m "feat(finalize): Übertragung nach BC über Warteschlange, Freigabe, Retry und Nachzügler"
```
````

- Pfade mit Leerzeichen oder Umlauten in `git add` in Anführungszeichen setzen.
- Ordner nur dann als Ganzes angeben, wenn wirklich alles darin zu diesem Commit gehört.
- Nie `git add -A` oder `git add .` vorschlagen.

Danach:
- **Nicht committen / prüfen:** Dateien aus Schritt 2 mit kurzer Begründung (falls vorhanden).
- **Hinweise:** Ergebnisse der Prüfungen aus Schritt 3, Dateien mit gemischten Änderungen, bereits
  gestagte Dateien, die in einen anderen Commit gehören (`git restore --staged <datei>`).

Nicht nachfragen, ob committet werden soll — der Vorschlag ist das Ergebnis.
