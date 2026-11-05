import { HttpErrorResponse } from '@angular/common/http';
import { GENERIC_ERROR, problemTitle } from '../absences/absence-models';

/** A role (`GET /api/roles`): a named set of privilege patterns ("what a person may do"). */
export interface Role {
  id: string;
  name: string;
  privileges: string[];
  builtIn: boolean;
}

/** A registered privilege (`GET /api/catalog`). `owner` is the application, or `custom` for own privileges. */
export interface CatalogEntry {
  key: string;
  owner: string;
  scopable: boolean;
  description: string;
}

/** A named set of stores (`GET /api/scopes`): a region, a district. */
export interface StoreGroup {
  id: string;
  name: string;
  storeIds: string[];
}

export interface Assignment {
  id: string;
  employeeId: string;
  roleId: string;
  unrestricted: boolean;
  scopeIds: string[];
  includeHomeStore: boolean;
  validFrom: string | null;
  validTo: string | null;
  source: string;
}

export interface CreateAssignmentRequest {
  roleId: string;
  unrestricted: boolean;
  scopeIds: string[] | null;
  includeHomeStore: boolean;
  validFrom: string | null;
  validTo: string | null;
}

export type TargetKind = 'employee' | 'store' | 'scope';

export interface CreateResponsibilityRequest {
  topic: string;
  responsibleEmployeeId: string;
  target: { kind: TargetKind; id: string };
  validFrom: string | null;
  validTo: string | null;
}

/** Who is responsible for a person, per topic (`GET /api/people/{id}/responsibilities`). */
export interface TopicResponsibility {
  topic: string;
  responsibleEmployeeIds: string[];
}

export interface MainStore {
  effectiveStoreId: string | null;
  overrideStoreId: string | null;
  suggestedStoreId: string | null;
}

/** The stores of the portal (`GET /api/locations`), reduced to what is shown here. */
export interface StoreOption {
  id: string;
  name: string;
  storeCode: string;
  archived: boolean;
}

export const ADMINISTRATOR_ROLE_ID = 'a0000000-0000-0000-0000-000000000001';

/** Topics that are in use (decisions 17, 30, 32, 37); free topics are allowed too. */
export const KNOWN_TOPICS: readonly { topic: string; label: string }[] = [
  { topic: 'absence.approval', label: 'Urlaub genehmigen' },
  { topic: 'schedule.adjustment', label: 'Dienstplan anpassen (HR-Gruppe bei Ausfall)' },
  { topic: 'employee.change-request', label: 'Änderungsanträge der Mitarbeiterkarte entscheiden' },
  { topic: 'workingtime.payout', label: 'Auszahlung des Arbeitszeitguthabens bei Austritt' },
];

export const TARGET_KINDS: readonly { kind: TargetKind; label: string; hint: string }[] = [
  { kind: 'employee', label: 'Person', hint: 'Personen-ID der Person, für die jemand zuständig ist.' },
  { kind: 'store', label: 'Filiale', hint: 'Filial-ID der Filiale (Auswahl unten).' },
  { kind: 'scope', label: 'Filial-Gruppe', hint: 'Eine Filial-Gruppe (Region, Bezirk).' },
];

// ---------- privileges: grouping, search, patterns ----------

const PATTERN = /^(\*|[a-z0-9-]+(:[a-z0-9-]+)*:(\*|[a-z0-9-]+))$/;
const TOPIC = /^[a-z0-9-]+(\.[a-z0-9-]+)*$/;
const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

export const isGuid = (text: string): boolean => GUID.test(text.trim());
export const isValidTopic = (text: string): boolean => TOPIC.test(text.trim());
export const isValidPattern = (text: string): boolean => PATTERN.test(text);

/** The part before the first colon: the area (`absence:create-own` → `absence`). */
export const areaOf = (key: string): string => (key === '*' ? '*' : key.split(':')[0]);

/** Exact, `*`, or `area:*` (like the service). */
export function matches(pattern: string, key: string): boolean {
  return pattern === '*' || pattern === key || (pattern.endsWith(':*') && key.startsWith(pattern.slice(0, -1)));
}

/** A pattern is known if it is `*`, a registered key, or `area:*` matching at least one registered key (the service refuses all others). */
export function isKnownPattern(pattern: string, catalogKeys: readonly string[]): boolean {
  return isValidPattern(pattern) && (pattern === '*' || catalogKeys.some((k) => matches(pattern, k)));
}

/** How many registered privileges a pattern grants. */
export const grantedBy = (pattern: string, catalogKeys: readonly string[]): number => catalogKeys.filter((k) => matches(pattern, k)).length;

const normalize = (text: string): string => text.normalize('NFD').replace(/\p{M}/gu, '').toLowerCase().trim();

export interface CatalogGroup {
  area: string;
  entries: CatalogEntry[];
}

/** The catalogue grouped by area, filtered by the search (key, description, application), areas and entries sorted. */
export function groupCatalog(entries: readonly CatalogEntry[], query: string): CatalogGroup[] {
  const q = normalize(query);
  const groups = new Map<string, CatalogEntry[]>();
  for (const entry of [...entries].sort((a, b) => a.key.localeCompare(b.key))) {
    if (q && ![entry.key, entry.description, entry.owner].some((text) => normalize(text).includes(q))) {
      continue;
    }
    const area = areaOf(entry.key);
    groups.set(area, [...(groups.get(area) ?? []), entry]);
  }
  return [...groups.entries()].sort(([a], [b]) => a.localeCompare(b)).map(([area, list]) => ({ area, entries: list }));
}

/** Wildcards the person may add: everything, and one entry per area. */
export function wildcardsOf(entries: readonly CatalogEntry[]): string[] {
  return ['*', ...[...new Set(entries.map((e) => areaOf(e.key)))].sort().map((area) => `${area}:*`)];
}

/** Keys of a role that are no longer in the catalogue (they have no effect). */
export const unknownIn = (role: Pick<Role, 'privileges'>, catalogKeys: readonly string[]): string[] =>
  role.privileges.filter((p) => !isKnownPattern(p, catalogKeys));

// ---------- roles ----------

/** The administrator role is fixed; the other built-in roles can be changed but never deleted. */
export const canChangeRole = (role: Role): boolean => role.id !== ADMINISTRATOR_ROLE_ID;
export const canDeleteRole = (role: Role): boolean => !role.builtIn;

export function builtInHint(role: Role): string {
  if (role.id === ADMINISTRATOR_ROLE_ID) {
    return 'Eingebaut: Alle Rechte. Kann nicht geändert oder gelöscht werden.';
  }
  return 'Eingebaut: Kann nicht gelöscht werden, die Rechte lassen sich ändern.';
}

export function validateRoleName(name: string, roles: readonly Role[]): string | null {
  const trimmed = name.trim();
  if (!trimmed) {
    return 'Bitte einen Namen für die Rolle eingeben.';
  }
  if (roles.some((r) => r.name.toLowerCase() === trimmed.toLowerCase())) {
    return 'Eine Rolle mit diesem Namen gibt es schon.';
  }
  return null;
}

/** Why a custom privilege cannot be created, or null. Wildcards are not allowed as own privileges. */
export function validateCustomKey(key: string, catalog: readonly CatalogEntry[]): string | null {
  const trimmed = key.trim();
  if (!trimmed.includes(':') || !isValidPattern(trimmed) || trimmed.endsWith('*')) {
    return 'Das Recht braucht die Form bereich:aktion (nur Kleinbuchstaben, Ziffern und Bindestriche, zum Beispiel inventur:freigeben).';
  }
  if (catalog.some((c) => c.key === trimmed)) {
    return 'Dieses Recht gibt es schon.';
  }
  return null;
}

// ---------- dates ----------

/** Start of the chosen local day as a time stamp. */
export function dayStart(date: string): string | null {
  if (!date) {
    return null;
  }
  const [y, m, d] = date.split('-').map(Number);
  return new Date(y, m - 1, d).toISOString();
}

/** "Until and including" a day is sent as the start of the following day (the service ends a period before its end). */
export function dayAfter(date: string): string | null {
  if (!date) {
    return null;
  }
  const [y, m, d] = date.split('-').map(Number);
  return new Date(y, m - 1, d + 1).toISOString();
}

export const formatDay = (iso: string): string => new Date(iso).toLocaleDateString('de-DE', { day: '2-digit', month: '2-digit', year: 'numeric' });

/** The last day of a period that ends before `iso`. */
export const formatLastDay = (iso: string): string => formatDay(new Date(new Date(iso).getTime() - 1).toISOString());

export function validatePeriod(from: string, to: string): string | null {
  return from && to && to < from ? 'Das Ende liegt vor dem Beginn.' : null;
}

/** "ab … bis …", "unbefristet" without dates. */
export function periodText(validFrom: string | null, validTo: string | null): string {
  const parts = [validFrom ? `ab ${formatDay(validFrom)}` : '', validTo ? `bis ${formatLastDay(validTo)}` : ''].filter(Boolean);
  return parts.length ? parts.join(' ') : 'unbefristet';
}

// ---------- store options ----------

export const storeLabel = (store: StoreOption): string => `${store.name} (${store.storeCode})`;

export function storeName(id: string | null, stores: readonly StoreOption[]): string {
  if (!id) {
    return '–';
  }
  const store = stores.find((s) => s.id === id);
  return store ? storeLabel(store) : id;
}

// ---------- errors ----------

const MESSAGES: Record<string, string> = {
  NotInReach: 'Das liegt außerhalb deiner Reichweite. Du darfst nur Personen und Filialen ändern, die du selbst erreichst.',
  NoEscalation: 'Du kannst nicht mehr Rechte oder eine größere Reichweite vergeben, als du selbst hast.',
  ValidityTooLong: 'Du kannst nicht länger vergeben, als deine eigenen Rechte gelten. Bitte ein früheres Ende wählen.',
  BuiltInRole: 'Diese eingebaute Rolle ist geschützt und lässt sich so nicht ändern oder löschen.',
  LastAdministrator: 'Es muss mindestens eine Person mit der unbefristeten, uneingeschränkten Administrator-Rolle bleiben.',
  UnknownPrivilege: 'Mindestens ein Recht ist im Katalog nicht bekannt. Bitte nur Rechte aus dem Katalog wählen.',
  InUse: 'Das gibt es schon oder wird noch verwendet (zum Beispiel eine Rolle oder Filial-Gruppe, die noch zugewiesen ist).',
  NotFound: 'Das gibt es nicht (mehr). Bitte die Seite neu laden.',
  Invalid: 'Die Eingabe ist nicht gültig. Bitte Namen, Rechte, Zeitraum und Personen-IDs prüfen.',
};

/** The error of a call in plain German; unknown errors get the general text, a missing right is said as such. */
export function accessProblemMessage(error: unknown): string {
  if (error instanceof HttpErrorResponse && error.status === 403 && problemTitle(error) === undefined) {
    return 'Dafür fehlt dir das Recht.';
  }
  const title = problemTitle(error);
  return (title && MESSAGES[title]) || GENERIC_ERROR;
}
