import { PrivilegeSet } from '../auth/privileges';
import { ALL_ITEMS, visibleNav } from './nav-items';

const keys = (...k: string[]) => new PrivilegeSet(k.map((key) => ({ key, unrestricted: true, storeIds: [], employeeIds: [] })));
const paths = (set: PrivilegeSet) => visibleNav(set).flatMap((g) => g.items.map((i) => i.path));

describe('visibleNav', () => {
  it('shows only open areas to a person without privileges', () => {
    expect(paths(PrivilegeSet.empty)).toEqual(['faq']);
  });

  it('shows an area if one of its privileges is granted', () => {
    expect(paths(keys('absence:create-own'))).toContain('absences');
    expect(paths(keys('absence:read-reason'))).toContain('team-absences');
  });

  it('hides areas whose privilege is missing', () => {
    expect(paths(keys('absence:read-own'))).not.toContain('approvals');
    expect(paths(keys('absence:read-own'))).not.toContain('admin/access');
  });

  it('drops groups without entries', () => {
    expect(visibleNav(PrivilegeSet.empty).map((g) => g.title)).toEqual(['Portal']);
  });

  it('has unique paths', () => {
    const all = ALL_ITEMS.map((i) => i.path);
    expect(new Set(all).size).toBe(all.length);
  });
});
