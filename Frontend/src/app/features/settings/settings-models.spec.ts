import { PrivilegeGrant } from '../../core/auth/privileges';
import { groupPrivileges, reachLabel } from './settings-models';

const grant = (key: string, patch: Partial<PrivilegeGrant> = {}): PrivilegeGrant => ({ key, unrestricted: true, storeIds: [], employeeIds: [], ...patch });

describe('reachLabel', () => {
  it('says "überall" for an unrestricted privilege', () => {
    expect(reachLabel(grant('absence:read'))).toBe('überall');
  });

  it('counts stores and people, with singular and plural', () => {
    expect(reachLabel(grant('a:b', { unrestricted: false, storeIds: ['1'] }))).toBe('1 Filiale');
    expect(reachLabel(grant('a:b', { unrestricted: false, storeIds: ['1', '2', '3'] }))).toBe('3 Filialen');
    expect(reachLabel(grant('a:b', { unrestricted: false, employeeIds: ['1'] }))).toBe('1 Person');
    expect(reachLabel(grant('a:b', { unrestricted: false, storeIds: ['1', '2'], employeeIds: ['1', '2'] }))).toBe('2 Filialen, 2 Personen');
  });

  it('says "nur du selbst" when nothing is listed', () => {
    expect(reachLabel(grant('notification:read-own', { unrestricted: false }))).toBe('nur du selbst');
  });
});

describe('groupPrivileges', () => {
  it('groups by the part before the colon, both sorted', () => {
    const groups = groupPrivileges([grant('absence:read'), grant('certificate:upload-own'), grant('absence:approve'), grant('plain')]);

    expect(groups.map((g) => g.area)).toEqual(['absence', 'certificate', 'plain']);
    expect(groups[0].items.map((i) => i.key)).toEqual(['absence:approve', 'absence:read']);
    expect(groups[2].items.map((i) => i.action)).toEqual(['plain']);
  });

  it('is empty without privileges', () => {
    expect(groupPrivileges([])).toEqual([]);
  });
});
