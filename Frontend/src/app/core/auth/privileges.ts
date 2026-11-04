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
