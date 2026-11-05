/** One privilege of the signed-in person with its reach (as delivered by `GET /api/me/privileges`). */
export interface PrivilegeGrant {
  key: string;
  unrestricted: boolean;
  storeIds: string[];
  employeeIds: string[];
}

export interface MyPrivileges {
  employeeId: string;
  roles: string[];
  privileges: PrivilegeGrant[];
  /** Who is signed in, from the copy of the people in Access (empty if the person is not known there). */
  name?: string | null;
  personnelNumber?: string | null;
}

/**
 * The effective privileges. Access already expands wildcards to registered privileges, so a plain key comparison is enough.
 * This only decides what the frontend shows; the services check every request themselves.
 */
export class PrivilegeSet {
  private readonly byKey: Map<string, PrivilegeGrant>;

  constructor(readonly grants: readonly PrivilegeGrant[] = []) {
    this.byKey = new Map(grants.map((g) => [g.key, g]));
  }

  static readonly empty = new PrivilegeSet();

  has(key: string): boolean {
    return this.byKey.has(key);
  }

  /** True if at least one of the keys is granted; an empty list means "no privilege needed". */
  hasAny(keys: readonly string[] | undefined): boolean {
    return !keys || keys.length === 0 || keys.some((k) => this.byKey.has(k));
  }

  get(key: string): PrivilegeGrant | undefined {
    return this.byKey.get(key);
  }
}

/**
 * The privileges a role grants, as the frontend sees them: the role's patterns (`*`, `area:*` or a plain key) matched against the registered
 * privileges, like Access does. The reach is not part of a role (it comes with an assignment), so every grant counts as unrestricted.
 */
export function grantsOfRole(patterns: readonly string[], catalogKeys: readonly string[]): PrivilegeGrant[] {
  const matches = (pattern: string, key: string): boolean =>
    pattern === '*' || pattern === key || (pattern.endsWith(':*') && key.startsWith(pattern.slice(0, -1)));
  return [...new Set(catalogKeys)]
    .filter((key) => patterns.some((p) => matches(p, key)))
    .map((key) => ({ key, unrestricted: true, storeIds: [], employeeIds: [] }));
}
