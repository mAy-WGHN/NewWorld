import { HttpErrorResponse } from '@angular/common/http';
import {
  ADMINISTRATOR_ROLE_ID,
  CatalogEntry,
  Role,
  accessProblemMessage,
  areaOf,
  canChangeRole,
  canDeleteRole,
  dayAfter,
  grantedBy,
  groupCatalog,
  isGuid,
  isKnownPattern,
  isValidTopic,
  matches,
  periodText,
  validateCustomKey,
  validatePeriod,
  validateRoleName,
  wildcardsOf,
} from './admin-access-models';

const entry = (key: string, description = '', scopable = false, owner = 'workforce'): CatalogEntry => ({ key, owner, scopable, description });

const catalog = [
  entry('absence:create-own', 'Eigene Abwesenheiten anlegen.', false),
  entry('absence:approve', 'Urlaub genehmigen.', true),
  entry('certificate:review', 'AU prüfen.', true),
  entry('access:role:read', 'Rollen ansehen.', false, 'access'),
];

const role = (over: Partial<Role> = {}): Role => ({ id: 'r1', name: 'Filialleitung', privileges: [], builtIn: false, ...over });

describe('privilege patterns', () => {
  it('takes the area before the first colon', () => {
    expect(areaOf('absence:create-own')).toBe('absence');
    expect(areaOf('access:role:read')).toBe('access');
    expect(areaOf('*')).toBe('*');
  });

  it('matches exact keys, everything and a whole area', () => {
    expect(matches('*', 'absence:approve')).toBe(true);
    expect(matches('absence:*', 'absence:approve')).toBe(true);
    expect(matches('absence:approve', 'absence:approve')).toBe(true);
    expect(matches('absence:*', 'absence-x:approve')).toBe(false);
    expect(matches('absence:approve', 'absence:create-own')).toBe(false);
  });

  it('knows * , registered keys and areas with at least one key, and refuses everything else', () => {
    const keys = catalog.map((c) => c.key);
    expect(isKnownPattern('*', keys)).toBe(true);
    expect(isKnownPattern('absence:*', keys)).toBe(true);
    expect(isKnownPattern('absence:approve', keys)).toBe(true);
    expect(isKnownPattern('inventur:*', keys)).toBe(false);
    expect(isKnownPattern('absence:nope', keys)).toBe(false);
    expect(isKnownPattern('Absence:Approve', keys)).toBe(false);
  });

  it('counts what a wildcard grants and offers one per area', () => {
    const keys = catalog.map((c) => c.key);
    expect(grantedBy('absence:*', keys)).toBe(2);
    expect(grantedBy('*', keys)).toBe(4);
    expect(wildcardsOf(catalog)).toEqual(['*', 'absence:*', 'access:*', 'certificate:*']);
  });
});

describe('groupCatalog', () => {
  it('groups by area with sorted areas and keys', () => {
    const groups = groupCatalog(catalog, '');

    expect(groups.map((g) => g.area)).toEqual(['absence', 'access', 'certificate']);
    expect(groups[0].entries.map((e) => e.key)).toEqual(['absence:approve', 'absence:create-own']);
  });

  it('filters by key, description or application, without caring for case or accents', () => {
    expect(groupCatalog(catalog, 'GENEHMIGEN').flatMap((g) => g.entries.map((e) => e.key))).toEqual(['absence:approve']);
    expect(groupCatalog(catalog, 'certificate').flatMap((g) => g.entries.map((e) => e.key))).toEqual(['certificate:review']);
    expect(groupCatalog(catalog, 'Access').flatMap((g) => g.entries.map((e) => e.key))).toEqual(['access:role:read']);
    expect(groupCatalog(catalog, 'xyz')).toEqual([]);
  });
});

describe('roles', () => {
  it('never deletes a built-in role and never changes the administrator role', () => {
    const admin = role({ id: ADMINISTRATOR_ROLE_ID, name: 'Administrator', builtIn: true });
    const publicUser = role({ id: 'x', name: 'Public User', builtIn: true });

    expect(canDeleteRole(admin)).toBe(false);
    expect(canChangeRole(admin)).toBe(false);
    expect(canDeleteRole(publicUser)).toBe(false);
    expect(canChangeRole(publicUser)).toBe(true);
    expect(canDeleteRole(role())).toBe(true);
  });

  it('wants a name that is new, regardless of case and spaces', () => {
    const roles = [role({ name: 'Administrator' })];

    expect(validateRoleName('  ', roles)).toContain('Namen');
    expect(validateRoleName(' administrator ', roles)).toContain('schon');
    expect(validateRoleName('Revisor', roles)).toBeNull();
  });
});

describe('custom privileges', () => {
  it('needs the form area:action, without wildcards and not twice', () => {
    expect(validateCustomKey('inventur:freigeben', catalog)).toBeNull();
    expect(validateCustomKey('inventur', catalog)).toContain('bereich:aktion');
    expect(validateCustomKey('Inventur:Freigeben', catalog)).toContain('bereich:aktion');
    expect(validateCustomKey('inventur:*', catalog)).toContain('bereich:aktion');
    expect(validateCustomKey('*', catalog)).toContain('bereich:aktion');
    expect(validateCustomKey('absence:approve', catalog)).toContain('schon');
  });
});

describe('ids, topics and periods', () => {
  it('recognises GUIDs and topics', () => {
    expect(isGuid('3f2504e0-4f89-11d3-9a0c-0305e82c3301')).toBe(true);
    expect(isGuid('12345')).toBe(false);
    expect(isValidTopic('absence.approval')).toBe(true);
    expect(isValidTopic('Absence Approval')).toBe(false);
    expect(isValidTopic('')).toBe(false);
  });

  it('refuses an end before the start', () => {
    expect(validatePeriod('2026-11-10', '2026-11-05')).toContain('vor');
    expect(validatePeriod('2026-11-05', '2026-11-05')).toBeNull();
    expect(validatePeriod('', '2026-11-05')).toBeNull();
  });

  it('sends "until and including a day" as the start of the next day, also across month and year ends', () => {
    expect(dayAfter('2026-11-30')).toBe(new Date(2026, 11, 1).toISOString());
    expect(dayAfter('2026-12-31')).toBe(new Date(2027, 0, 1).toISOString());
    expect(dayAfter('2028-02-28')).toBe(new Date(2028, 1, 29).toISOString());
    expect(dayAfter('2028-02-29')).toBe(new Date(2028, 2, 1).toISOString());
    expect(dayAfter('')).toBeNull();
  });

  it('shows the last day of a period as the person typed it', () => {
    expect(periodText(null, null)).toBe('unbefristet');
    expect(periodText(new Date(2026, 10, 5).toISOString(), dayAfter('2026-11-30'))).toBe('ab 05.11.2026 bis 30.11.2026');
  });
});

describe('accessProblemMessage', () => {
  const problem = (title: string, status: number) => new HttpErrorResponse({ status, error: { title } });

  it('explains the reasons of the service in plain words', () => {
    expect(accessProblemMessage(problem('NoEscalation', 403))).toContain('nicht mehr Rechte');
    expect(accessProblemMessage(problem('NotInReach', 403))).toContain('Reichweite');
    expect(accessProblemMessage(problem('LastAdministrator', 403))).toContain('Administrator');
    expect(accessProblemMessage(problem('BuiltInRole', 403))).toContain('eingebaute');
    expect(accessProblemMessage(problem('InUse', 409))).toContain('verwendet');
    expect(accessProblemMessage(problem('UnknownPrivilege', 422))).toContain('Katalog');
    expect(accessProblemMessage(problem('Invalid', 422))).toContain('nicht gültig');
    expect(accessProblemMessage(problem('NotFound', 404))).toContain('nicht (mehr)');
  });

  it('says a missing right as such and uses the general text for the unknown', () => {
    expect(accessProblemMessage(new HttpErrorResponse({ status: 403 }))).toContain('Recht');
    expect(accessProblemMessage(problem('Surprise', 500))).toContain('nicht geklappt');
    expect(accessProblemMessage(new Error('x'))).toContain('nicht geklappt');
  });
});
