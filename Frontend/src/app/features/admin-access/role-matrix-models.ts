import { CatalogEntry, Role, areaOf, canChangeRole, groupCatalog, matches } from './admin-access-models';

/**
 * The state of one cell of the matrix (a role and a privilege or a collective privilege):
 * - `direct`: the role lists exactly this entry;
 * - `covered`: the role does not list it, but a collective privilege (`*` or `area:*`) of the role grants it;
 * - `none`: the role does not have it.
 * A direct entry that is also covered stays `direct` (it can be taken away; the role then still has it through the collective privilege).
 */
export type CellState = 'direct' | 'covered' | 'none';

/** The collective privilege that grants `key` to the role (other than `key` itself), or `null`. */
export function coveringPattern(privileges: readonly string[], key: string): string | null {
  return privileges.find((p) => p !== key && matches(p, key)) ?? null;
}

export function cellState(privileges: readonly string[], key: string): CellState {
  if (privileges.includes(key)) {
    return 'direct';
  }
  return coveringPattern(privileges, key) ? 'covered' : 'none';
}

/** Whether the person may click the cell: the role can be changed and the entry is not only granted through a collective privilege. */
export const isToggleable = (state: CellState, roleChangeable: boolean): boolean => roleChangeable && state !== 'covered';

/** The role's privileges after a click on the cell of `key`. */
export function toggled(privileges: readonly string[], key: string): string[] {
  return privileges.includes(key) ? privileges.filter((p) => p !== key) : [...privileges, key];
}

const sameSet = (a: readonly string[], b: readonly string[]): boolean => a.length === b.length && [...a].sort().join('|') === [...b].sort().join('|');

/** The drafts of the matrix: the privileges of every role as they are being edited. */
export type Drafts = Record<string, string[]>;

export function draftsOf(roles: readonly Role[]): Drafts {
  return Object.fromEntries(roles.map((r) => [r.id, [...r.privileges]]));
}

/** The roles whose draft differs from what the service holds. */
export function changedRoles(roles: readonly Role[], drafts: Drafts): Role[] {
  return roles.filter((r) => canChangeRole(r) && drafts[r.id] && !sameSet(drafts[r.id], r.privileges));
}

/** How many entries were added or taken away in total (for "N Änderungen"). */
export function changeCount(roles: readonly Role[], drafts: Drafts): number {
  return changedRoles(roles, drafts).reduce((sum, r) => {
    const now = drafts[r.id];
    const added = now.filter((p) => !r.privileges.includes(p)).length;
    const removed = r.privileges.filter((p) => !now.includes(p)).length;
    return sum + added + removed;
  }, 0);
}

/** A row of the matrix: a collective privilege or a single privilege. */
export interface MatrixRow {
  key: string;
  label: string;
  description: string;
  kind: 'all' | 'area' | 'privilege';
}

export interface MatrixGroup {
  area: string;
  /** The row "everything of this area" (`area:*`) and then the single privileges. */
  head: MatrixRow;
  rows: MatrixRow[];
}

/** The rows of the matrix: first "everything" (`*`), then per area the collective privilege and its privileges; filtered by the search. */
export function matrixRows(catalog: readonly CatalogEntry[], query: string): { all: MatrixRow; groups: MatrixGroup[] } {
  const groups = groupCatalog(catalog, query).map((g) => ({
    area: g.area,
    head: { key: `${g.area}:*`, label: `Alles in ${g.area}`, description: `Alle Rechte des Bereichs ${g.area}, auch spätere.`, kind: 'area' as const },
    rows: g.entries.map((e) => ({ key: e.key, label: e.key, description: e.description, kind: 'privilege' as const })),
  }));
  return { all: { key: '*', label: 'Alles', description: 'Alle Rechte, auch spätere.', kind: 'all' }, groups };
}

/** The text that explains a cell to a screen reader and as a tooltip. */
export function cellTitle(role: Role, row: MatrixRow, state: CellState, covering: string | null, changeable: boolean): string {
  const base = `${role.name}: ${row.label}`;
  if (state === 'covered') {
    return `${base} - durch das Sammelrecht ${covering}, dort änderbar`;
  }
  if (!changeable) {
    return `${base} - ${state === 'direct' ? 'ja' : 'nein'} (Rolle nicht änderbar)`;
  }
  return `${base} - ${state === 'direct' ? 'ja, anklicken zum Entfernen' : 'nein, anklicken zum Vergeben'}`;
}

export { areaOf };
