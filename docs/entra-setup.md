# Entra-Einrichtungspaket (für den Tenant-Admin)

Stand: 2026-10-23. Für die lokale Entwicklung und alle Tests wird **nichts** davon gebraucht (lokal gilt ein statisches Fake-Token, siehe `docs/architektur.md`). Das Paket beschreibt, was für den Betrieb in Azure nötig ist und warum.

## Grundsatz

- **Personen** melden sich nicht über Entra an, sondern über den Access-Dienst (Personalnummer + BC-PIN, eigenes Token; Entscheidung 13). Es gibt deshalb **keine Benutzer-App-Rollen, keine Benutzergruppen für Fachrechte und keine Zuordnung Entra-`oid` → Mitarbeitende** (das weicht vom Plan ab).
- **Entra wird nur zwischen Diensten** gebraucht (Client Credentials mit Managed Identity), plus der Zugriff von BC Integration auf Business Central und der Service-Bus-Zugriff (RBAC, kein Shared Access Key).

## 1. Managed Identities

Fünf Dienste, je eine (systemseitig zugewiesene) Managed Identity: `access`, `bc-integration`, `notifications`, `workforce`, `portal`. Begründung: jeder Dienst bekommt genau die Rechte, die er braucht (Prinzip der geringsten Rechte); keine Geheimnisse im Code.

## 2. App-Registrierungen und App Roles

Je eine Registrierung pro API, die von anderen Diensten aufgerufen wird. Alle Rollen sind nur für Anwendungen (`allowedMemberTypes = Application`).

| App-Registrierung | App Role | Wofür | Aufrufer |
| --- | --- | --- | --- |
| NewWorld Access API | `Access.Catalog.Register` | Dienst meldet seinen Rechtekatalog beim Start an | Workforce, Notifications |
| NewWorld Access API | `Access.Permissions.Read` | vollständiger Neuaufbau der Berechtigungskopie nach Ausfall/Neuinstallation | Workforce, Notifications, Portal |
| NewWorld BC Integration API | `BcIntegration.Pin.Verify` | Login prüft Personalnummer + PIN in BC | Access |
| NewWorld BC Integration API | `BcIntegration.EmployeeErasure.Request` | DSGVO-Löschung einer Person auslösen | Betreiber (noch keine Zuweisung) |
| NewWorld Workforce API | `Workforce.AbsenceDetails.Read` | Abwesenheitsdetails inkl. Art für berechtigte Dienste | später die HR API (noch keine Zuweisung) |

Notifications und Portal bieten (noch) keine Dienst-Endpunkte; für sie gibt es keine Registrierung. Jeder neue Endpunkt zwischen Diensten bekommt eine eigene App Role.

Skript: `infra/entra/New-NewWorldApps.ps1` (idempotent, `-WhatIf` unterstützt).

## 3. Rollenzuweisungen

Skript: `infra/entra/Grant-ServiceRoles.ps1`; Prüfung ohne Änderung: `infra/entra/Test-NewWorldEntra.ps1`. Die Zuweisungen entsprechen genau der Tabelle in Abschnitt 2.

## 4. Business Central

| Zustimmung | Wer | Begründung |
| --- | --- | --- |
| Dynamics 365 Business Central `API.ReadWrite.All` (Anwendungsberechtigung, Administratorzustimmung) | Managed Identity von BC Integration | BC Integration ist der einzige BC-Zugang: liest Stammdaten (Filialen, Mitarbeitende), schreibt Abwesenheiten, prüft die PIN. BC kennt keine feinere Anwendungsberechtigung für eigene API-Seiten. |

Zusätzlich in BC: Identität auf der Seite „Microsoft Entra-Anwendungen“ anlegen und die Berechtigungssätze für die benutzerdefinierten API-Seiten zuweisen (TODO(BC-Modell): Seiten und Berechtigungssätze stehen noch nicht fest).

## 5. Service Bus

Topologie und Rollenzuweisungen: `infra/servicebus.bicep` (Parameter: Principal-IDs der fünf Managed Identities). Lokale Authentifizierung ist abgeschaltet (`disableLocalAuth`). Sender: nur eigene Topics/Queues; Empfänger: nur die eigene Subscription (die Dead-Letter-Queue gehört dazu, hat also dieselbe Einschränkung).

## 6. Bitte an den Tenant-Admin

Eine auf diese Apps **beschränkte** Verwaltungsrolle („Anwendungsadministrator“ nur für die oben genannten Registrierungen, z. B. über einen Verwaltungseinheit-Scope), damit Rollen künftig ohne Tenant-weite Rechte angepasst werden können. Wenn das nicht möglich ist: Änderungen weiterhin über die Skripte und den Tenant-Admin.

## Offen

Entra-Lizenzen (Frontline), Personalnummer im Entra-Benutzerobjekt und Gruppenzuweisung entfallen mit Entscheidung 13, solange Personen nicht über Entra angemeldet werden. Ändert sich das (Single Sign-on), kommt die Identitätszuordnung aus dem Plan zurück.
