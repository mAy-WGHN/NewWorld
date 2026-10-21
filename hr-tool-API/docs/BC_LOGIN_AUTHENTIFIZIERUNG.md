# Login gegen Business Central – Erkenntnisse aus der Inventur-API

> **Überholt (Stand 2026-10-20):** Der Login läuft nicht mehr im HR-Tool. Anmeldung = Rollen-Rechte, BC-Zugriff = BC-Anbindung (dort ist `Zeiterfassung.GetStatus` bereits umgesetzt). Das Dokument bleibt als Hintergrund zu den BC-Eigenheiten stehen; siehe `CLAUDE.md`.

Quelle: `C:\Users\MehmetAydogan\source\repos\Inventur-API` (.NET 10, ASP.NET Core, EF Core/PostgreSQL).
Stand der Analyse: 09.10.2026. Alle Angaben stammen aus dem gelesenen Code, nicht aus einem Live-Test.

## 1. Überblick

Der Login prüft **Personalnummer + PIN live gegen Business Central (BC)**. Dafür gibt es zwei getrennte Ebenen:

| Ebene | Zweck | Klasse |
|---|---|---|
| **S2S-Authentifizierung** (API ↔ BC) | Die API holt sich per OAuth2 Client Credentials ein Azure-AD-Token | `Services/BcAuthService.cs` |
| **Benutzer-Login** (Mitarbeiter ↔ API) | Personalnummer + PIN werden über eine BC-Codeunit geprüft | `Services/BcEmployeeCredentialValidator.cs` |

Der Mitarbeiter authentifiziert sich also **nie direkt** bei Azure AD. Die API meldet sich als technische Anwendung an BC an und fragt dort "stimmt diese PIN?".

```
Client ──POST /api/v1/auth/login──▶ AuthController
                                      │
                                SessionService.LoginAsync
                                      │
                       BcEmployeeCredentialValidator
                          │                    │
              1) GetStatus (PIN-Prüfung)   2) GetStaffV2 (Filiale)
                          │                    │
                    BcODataClient (SOAP, Bearer-Token)
                                      │
                              BcAuthService
                                      │
                    Azure AD  (client_credentials) ──▶ Access Token
```

## 2. Schritt 1: Token von Azure AD holen (`BcAuthService`)

- OAuth2 **Client Credentials Flow**: `POST {TokenEndpoint}` mit Form-Body
  `client_id`, `client_secret`, `scope`, `grant_type=client_credentials`.
- Antwort: `access_token`, `expires_in`, `token_type`.
- Token wird **im Speicher gecacht** (Singleton) bis `expires_in − 60 s`. Ein `SemaphoreSlim` mit Double-Check verhindert parallele Token-Anfragen.
- **Retry nur für `AADSTS7000215`** (am Mandanten beobachtete, sporadische Drosselung): bis zu 3 Wiederholungen mit 2 / 5 / 10 s Abstand (max. ~17 s). Jeder andere Fehler schlägt sofort fehl und wirft `BcAuthException`.

### Konfiguration (`BcAuthOptions`, Section `BusinessCentral`)

| Key | Bedeutung |
|---|---|
| `TokenEndpoint` | Azure-AD-Token-URL (in `appsettings.json`) |
| `ClientId` / `ClientSecret` | App-Registration in Azure AD |
| `Scope` | BC-Scope (typisch `https://api.businesscentral.dynamics.com/.default`) |
| `TenantId` | Azure-AD-Tenant, Teil der BC-URL |
| `Environment` | BC-Umgebungsname (z. B. `Wintergerst-Gruppe`) |
| `CompanyName` | BC-Company-Kurzname (z. B. `WGHN`) |

ClientId/Secret/Scope/TenantId/Environment/CompanyName gehören **nicht ins Repo**: lokal per `.env` (Vorlage `.env.example`, Schreibweise `BusinessCentral__ClientId=` usw.), produktiv per Umgebungsvariable bzw. Key Vault.

### DI-Registrierung (`Program.cs`)

```csharp
builder.Services.Configure<BcAuthOptions>(builder.Configuration.GetSection("BusinessCentral"));
builder.Services.AddHttpClient(BcAuthService.HttpClientName);
builder.Services.AddSingleton<IBcAuthService, BcAuthService>();
builder.Services.AddHttpClient(BcODataClient.HttpClientName);
builder.Services.AddScoped<IBcODataClient, BcODataClient>();
builder.Services.AddScoped<IEmployeeCredentialValidator, BcEmployeeCredentialValidator>();
builder.Services.AddDataProtection().SetApplicationName("Inventur-API").PersistKeysToDbContext<InventoryDbContext>();
```

## 3. Schritt 2: PIN gegen BC prüfen (`BcEmployeeCredentialValidator`)

### 3.1 Aufruf: Codeunit `Zeiterfassung` / Funktion `GetStatus`

Es wird bewusst **dieselbe Codeunit wie im HR-Zeiterfassungstool** verwendet (Team-Entscheidung 23.09.). BC vergleicht die PIN intern (gegen "Time Entry Password" bzw. das Kassierer-Kennwort der verknüpften Retail Staff ID). Es kommt nur Erfolg/Misserfolg plus Name zurück. Die API muss keinen Hash vergleichen.

- Transport: **SOAP 1.1** (Codeunit Web Service), kein OData-REST.
- URL: `https://api.businesscentral.dynamics.com/v2.0/{TenantId}/{Environment}/WS/{CompanyName}/Codeunit/Zeiterfassung`
- Header: `Authorization: Bearer <token>`, `SOAPAction: "urn:microsoft-dynamics-schemas/codeunit/Zeiterfassung:GetStatus"`, `Content-Type: text/xml`
- Parameter: `employeeNo`, `queryDate` (heute, `yyyy-MM-dd`, UTC), `password` (die PIN)

Request-Envelope:

```xml
<?xml version="1.0" encoding="utf-8"?>
<soap:Envelope xmlns:soap="http://schemas.xmlsoap.org/soap/envelope/">
  <soap:Body>
    <GetStatus xmlns="urn:microsoft-dynamics-schemas/codeunit/Zeiterfassung">
      <employeeNo>11207</employeeNo>
      <queryDate>2026-10-09</queryDate>
      <password>1234</password>
    </GetStatus>
  </soap:Body>
</soap:Envelope>
```

Antwort: Das Element `return_value` enthält einen **JSON-String**:

```json
{ "success": true, "employeeName": "Max Mustermann" }
```

Login gilt als erfolgreich, wenn `success == true` **und** `employeeName` nicht leer ist.

### 3.2 Wichtige Eigenheiten (live verifiziert 23.09. laut Code-Kommentaren)

1. **Leeres Passwort liefert `success:true`**. BC prüft es nicht. Die API weist ein leeres PIN deshalb **immer selbst ab**, bevor BC aufgerufen wird.
2. Falsches Passwort und unbekannte Personalnummer liefern sauber `success:false` mit HTTP 200 und einer `message`, **kein SOAP-Fault**.
3. Eine `BcAuthException` ist daher ein echter technischer Fehler (Auth, Netzwerk, BC down) und keine normale Ablehnung.
4. **PIN/Passwort niemals loggen**, auch nicht in Exception-Messages. Das HR-Tool schreibt bei Fehlern den kompletten SOAP-Envelope mit Klartext-Passwort ins Log, das soll hier ausdrücklich nicht passieren.
5. Nicht jede Codeunit antwortet im gleichen Format. `GetStatus` liefert JSON in `return_value`, `GetStaffV2` liefert verschachteltes XML ohne `return_value`. Neue Codeunits vor der Nutzung live prüfen.

### 3.3 Zweiter Aufruf: Filiale per `GetStaffV2`

`GetStatus` liefert keine Filiale. Deshalb folgt ein zweiter SOAP-Call: Codeunit `GetStaffV2`, Funktion `GetStaffV2`, Parameter `staffID` (plus leere `responseCode`/`errorText` und das `getStaffV2XML`-Element). Aus der XML-Antwort wird das Element `Staff` gelesen und daraus das Feld **`Store_No.`**. Ist keine Filiale vorhanden, ist `StoreNo` leer und der Login klappt trotzdem (siehe 5.).

### 3.4 Fallback bei nicht erreichbarem BC

- Nach jedem **erfolgreichen Live-Login** wird in der Tabelle `CachedEmployeeCredential` gespeichert: `EmployeeNo`, `DisplayName`, `StoreNo`, `EncryptedPin`, `LastVerifiedAt`.
- Die PIN liegt **reversibel verschlüsselt** (ASP.NET Core Data Protection, Protector `Inventur-API.CachedEmployeeCredential.Pin`), bewusst nicht gehasht (User-Entscheidung 23.09.). Die Schlüssel liegen in der DB, damit sie Redeploys überleben.
- Wirft `GetStatus` eine `BcAuthException`, wird **nur dann** gegen diesen Cache geprüft (Vergleich mit `CryptographicOperations.FixedTimeEquals`). Eine von BC abgelehnte PIN fällt **nie** auf den Cache zurück.
- Kein Ablaufdatum des Caches (User-Entscheidung 23.09.). Eine in BC geänderte oder gesperrte PIN wird erst beim nächsten erfolgreichen Online-Login sichtbar. Das ist ein akzeptiertes Risiko.
- Schlägt nur der Store-Lookup (`GetStaffV2`) fehl, nachdem die PIN bestätigt wurde, wird die zuletzt gecachte Filiale genutzt.

## 4. Absicherung rund um den Login (`SessionService`)

- **Pflichtfelder:** `employeeNo`, `pin`, `deviceId`, `platform`. Sonst Outcome `InvalidRequest` (400).
- **Brute-Force-Schutz:** 5 Fehlversuche je Personalnummer führen zu 5 Minuten Sperre (`SessionPolicy`, Tabelle `LoginThrottle`). Ein Erfolg löscht den Zähler.
- **Externe Inventurhelfer** (`Inv-Helfer-N`) existieren nur in der API, nicht in BC, und werden lokal per Passwort-Hash geprüft, bevor BC überhaupt gefragt wird.
- **Plattform-Regeln für BC-Logins:**
  - `Web` erfordert `Employee.IsSupervisor`, sonst `NotSupervisor` (403). Die Rolle ist dann `Supervisor`.
  - `Android` erfordert `Employee.IsTeamLead`, sonst `BcLoginNotAllowed` (403). Die Rolle ist dann `TeamLead`.
- **Rolle kommt nicht aus BC**, sondern aus Plattform + lokalen Flags. BC liefert nur Identität (Nummer, Name, Filiale).
- Bei erstem BC-Login wird ein `Employee`-Datensatz angelegt bzw. der Name aktualisiert (Upsert).
- **Session:** serverseitig in `EmployeeSession` gespeichert (widerrufbar), Token ist eine GUID. Gültigkeit 30 Minuten Inaktivität, jeder authentifizierte Request verlängert sie.

## 5. Endpunkte

| Methode | Pfad | Zweck |
|---|---|---|
| POST | `/api/v1/auth/login` | Login (anonym) |
| POST | `/api/v1/auth/sessions/select-store` | Filiale nachträglich setzen, falls beim Login `storeNo == null` |
| POST | `/api/v1/auth/logout` | Eigene Sitzung beenden |
| POST | `/api/v1/auth/sessions/{id}/revoke` | Sitzung widerrufen (Supervisor) |
| POST | `/api/v1/auth/employees/{employeeNo}/revoke-all` | Alle Sitzungen eines Mitarbeiters widerrufen (Supervisor) |

### Fehler-Mapping beim Login

| Outcome | HTTP |
|---|---|
| `InvalidRequest` | 400 |
| `InvalidCredentials` | 401 |
| `PlatformNotAllowed` / `NotSupervisor` / `BcLoginNotAllowed` | 403 |
| `UnknownStore` / `NoTeamAssigned` | 409 |
| `UserBlocked` | 429 |

Fehlerantworten sind JSON:API-Error-Dokumente mit einem `code` aus `ErrorCodes`.

## 6. DTOs: Was kommt bei erfolgreicher Authentifizierung zurück?

### 6.1 Request: `LoginRequest` (`Contracts/LoginRequest.cs`)

```csharp
public record LoginRequest(string EmployeeNo, string Pin, string DeviceId, string? AppVersion, Platform? Platform);
```

| Feld | Pflicht | Hinweis |
|---|---|---|
| `employeeNo` | ja | Personalnummer |
| `pin` | ja | darf nicht leer sein |
| `deviceId` | ja | Geräte-ID |
| `appVersion` | nein | |
| `platform` | ja | `android` oder `web` (Enum als camelCase-String) |

### 6.2 Response: `LoginResponse` (`Contracts/LoginResponse.cs`)

```csharp
public record LoginResponse(
    Guid SessionToken, string EmployeeNo, string DisplayName, string? StoreNo,
    EmployeeRole Role, DateTimeOffset ValidUntil, DateTimeOffset ServerTime);
```

**Es werden genau 7 Felder zurückgegeben, bewusst wenig:**

| Feld | Typ | Herkunft / Bedeutung |
|---|---|---|
| `sessionToken` | GUID | Neu erzeugtes Session-Token der API (kein BC-/Azure-Token) |
| `employeeNo` | string | Personalnummer |
| `displayName` | string | `employeeName` aus BC (`GetStatus`). Bei externen Helfern der vom Teamleiter vergebene Name |
| `storeNo` | string? | `Store_No.` aus BC (`GetStaffV2`), `null` wenn BC keine Filiale liefert. Teamleiter in der App bekommen immer `null` und müssen die Filiale selbst wählen |
| `role` | enum | `counter`, `supervisor`, `teamLead` (aus Plattform + lokalen Flags, nicht aus BC) |
| `validUntil` | ISO-8601 | Ablauf der Session (jetzt + 30 min) |
| `serverTime` | ISO-8601 | Serverzeit beim Abschluss des Logins |

**Nicht** zurückgegeben werden: PIN, BC-/Azure-Access-Token, weitere BC-Mitarbeiterdaten (Abteilung, E-Mail, Arbeitszeit-Status usw.), IP, Geräte- und Plattforminformationen.

### 6.3 Antwort-Hülle (JSON:API)

`AuthController` verpackt die Antwort mit `JsonApiOk("sessions", sessionToken, data)`. Die Felder liegen unter `data.attributes`, `type` ist `"sessions"` und `id` das Session-Token. JSON-Namen sind camelCase, Enums sind camelCase-Strings.

```json
{
  "data": {
    "type": "sessions",
    "id": "3f2b8c1e-5a6d-4e0f-9b7a-1c2d3e4f5a6b",
    "attributes": {
      "sessionToken": "3f2b8c1e-5a6d-4e0f-9b7a-1c2d3e4f5a6b",
      "employeeNo": "11207",
      "displayName": "Max Mustermann",
      "storeNo": "12",
      "role": "supervisor",
      "validUntil": "2026-10-09T10:30:00+00:00",
      "serverTime": "2026-10-09T10:00:00+00:00"
    }
  }
}
```

(Beispielwerte, nicht aus einem echten Lauf. Das optionale `meta` wird beim Login nicht gesetzt.)

### 6.4 Interne Zwischen-DTOs (nicht öffentlich)

- `EmployeeCredential(EmployeeNo, DisplayName, StoreNo)`: Ergebnis des Validators. **Das ist alles, was die BC-Prüfung der API liefert:** Nummer, Name, Filiale.
- `GetStatusResponse(success, employeeName)`: BC-Antwort von `GetStatus`.
- `LoginResult(Outcome, Data, Error)`: Rückgabe von `SessionService.LoginAsync` an den Controller.
- `SelectStoreResponse(sessionToken, storeNo)`: Antwort von `select-store`.

## 7. Hinweise für die Übernahme ins HR-Tool

1. Die Bausteine `BcAuthOptions`, `BcAuthService`, `BcODataClient` (`PostSoapAsync`, `CallLscCodeunitAsync`) und `LscCodeunitResponse` sind vom Fachlichen entkoppelt und direkt wiederverwendbar.
2. `Zeiterfassung.GetStatus` ist dieselbe Codeunit, die das HR-Tool bereits nutzt. Für die reine PIN-Prüfung ist **kein neuer BC-Entwicklungsaufwand** nötig.
3. Leeres Passwort immer **vor** dem BC-Call abweisen (siehe 3.2, Punkt 1).
4. Entscheiden, ob der Fallback-Cache (reversibel verschlüsselte PIN, ohne Ablauf) übernommen werden soll. Er erhöht die Verfügbarkeit, hat aber die unter 3.4 genannten Sicherheitsfolgen.
5. Lockout (5 Fehlversuche, 5 Minuten) und serverseitige, widerrufbare Sessions sind unabhängig von BC und sollten im HR-Tool ebenfalls vorgesehen werden.
6. Klartext-PIN nirgends loggen, auch nicht in Fehlerpfaden.

## 8. Relevante Dateien in der Inventur-API

| Datei | Inhalt |
|---|---|
| `Controllers/AuthController.cs` | Endpunkte, Fehler-Mapping |
| `Services/SessionService.cs` | Login-Ablauf, Throttling, Rollen, Session |
| `Services/BcEmployeeCredentialValidator.cs` | PIN-Prüfung gegen BC, Cache-Fallback |
| `Services/IEmployeeCredentialValidator.cs` | Schnittstelle + `EmployeeCredential` |
| `Services/BcAuthService.cs` | Azure-AD-Token (Client Credentials, Cache, Retry) |
| `Services/BcODataClient.cs` | SOAP-/OData-Aufrufe mit Bearer-Token |
| `Services/LscCodeunitResponse.cs` | Parser für SOAP-Antworten |
| `Services/BcAuthOptions.cs`, `.env.example` | Konfiguration |
| `Domain/Entities/CachedEmployeeCredential.cs` | Fallback-Cache |
| `Authentication/SessionPolicy.cs` | Timeout, Lockout-Konstanten |
| `Contracts/LoginRequest.cs`, `LoginResponse.cs` | DTOs |
| `Services/LoginResult.cs` | Outcome-Enum |
| `Services/DevEmployeeCredentialValidator.cs` | Dev-Platzhalter, in `Program.cs` aktuell nicht registriert |
