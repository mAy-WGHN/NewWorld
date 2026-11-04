import { PrivilegeGrant } from '../../core/auth/privileges';

const count = (n: number, one: string, many: string): string => `${n} ${n === 1 ? one : many}`;

/** Where a privilege reaches, in plain words: everywhere, or the stores and people it is limited to. */
export function reachLabel(grant: PrivilegeGrant): string {
  if (grant.unrestricted) {
    return 'überall';
  }
  const parts: string[] = [];
  if (grant.storeIds.length) {
    parts.push(count(grant.storeIds.length, 'Filiale', 'Filialen'));
  }
  if (grant.employeeIds.length) {
    parts.push(count(grant.employeeIds.length, 'Person', 'Personen'));
  }
  return parts.length ? parts.join(', ') : 'nur du selbst';
}

export interface PrivilegeGroup {
  area: string;
  items: { key: string; action: string; reach: string }[];
}

/** The privileges `area:action` grouped by area, both sorted. A key without a colon is its own area. */
export function groupPrivileges(grants: readonly PrivilegeGrant[]): PrivilegeGroup[] {
  const areas = new Map<string, PrivilegeGroup>();
  for (const grant of [...grants].sort((a, b) => a.key.localeCompare(b.key))) {
    const colon = grant.key.indexOf(':');
    const area = colon < 0 ? grant.key : grant.key.slice(0, colon);
    const action = colon < 0 ? grant.key : grant.key.slice(colon + 1);
    const group = areas.get(area) ?? { area, items: [] };
    group.items.push({ key: grant.key, action, reach: reachLabel(grant) });
    areas.set(area, group);
  }
  return [...areas.values()].sort((a, b) => a.area.localeCompare(b.area));
}
