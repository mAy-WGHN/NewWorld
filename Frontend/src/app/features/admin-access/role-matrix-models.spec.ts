import { ADMINISTRATOR_ROLE_ID, CatalogEntry, Role } from './admin-access-models';
import { cellState, cellTitle, changeCount, changedRoles, coveringPattern, draftsOf, isToggleable, matrixRows, toggled } from './role-matrix-models';

const role = (over: Partial<Role> = {}): Role => ({ id: 'r1', name: 'Filialleitung', privileges: [], builtIn: false, ...over });

const catalog: CatalogEntry[] = [
  { key: 'absence:approve', owner: 'workforce', scopable: true, description: 'Urlaub genehmigen.' },
  { key: 'absence:create-own', owner: 'workforce', scopable: false, description: 'Eigene Abwesenheiten anlegen.' },
  { key: 'certificate:review', owner: 'workforce', scopable: true, description: 'AU pruefen.' },
];

describe('cellState', () => {
  it('is direct for an exact entry, covered through a collective privilege, otherwise none', () => {
    expect(cellState(['absence:approve'], 'absence:approve')).toBe('direct');
    expect(cellState(['absence:*'], 'absence:approve')).toBe('covered');
    expect(cellState(['*'], 'certificate:review')).toBe('covered');
    expect(cellState(['absence:*'], 'certificate:review')).toBe('none');
    expect(cellState([], 'absence:approve')).toBe('none');
  });

  it('keeps an exact entry direct even if a collective privilege covers it, too', () => {
    expect(cellState(['absence:*', 'absence:approve'], 'absence:approve')).toBe('direct');
  });

  it('knows the collective privilege that covers an entry', () => {
    expect(coveringPattern(['absence:*', 'absence:approve'], 'absence:approve')).toBe('absence:*');
    expect(coveringPattern(['absence:approve'], 'absence:approve')).toBeNull();
  });

  it('treats the collective privilege of an area as covered by "*" and the area as a whole as direct', () => {
    expect(cellState(['*'], 'absence:*')).toBe('covered');
    expect(cellState(['absence:*'], 'absence:*')).toBe('direct');
  });
});

describe('isToggleable and toggled', () => {
  it('lets the person click only cells of a changeable role that are not merely covered', () => {
    expect(isToggleable('none', true)).toBe(true);
    expect(isToggleable('direct', true)).toBe(true);
    expect(isToggleable('covered', true)).toBe(false);
    expect(isToggleable('none', false)).toBe(false);
  });

  it('adds or removes an entry', () => {
    expect(toggled(['a:b'], 'c:d')).toEqual(['a:b', 'c:d']);
    expect(toggled(['a:b', 'c:d'], 'a:b')).toEqual(['c:d']);
  });
});

describe('changes', () => {
  const roles = [role({ id: 'r1', privileges: ['absence:approve'] }), role({ id: 'r2', name: 'HR', privileges: ['absence:*'] })];

  it('starts without changes', () => {
    expect(changedRoles(roles, draftsOf(roles))).toEqual([]);
    expect(changeCount(roles, draftsOf(roles))).toBe(0);
  });

  it('counts what was added and taken away, ignoring the order', () => {
    const drafts = draftsOf(roles);
    drafts['r1'] = ['certificate:review', 'absence:approve'];
    drafts['r2'] = [];

    expect(changedRoles(roles, drafts).map((r) => r.id)).toEqual(['r1', 'r2']);
    expect(changeCount(roles, drafts)).toBe(2); // +1 in r1, -1 in r2
    drafts['r1'] = ['absence:approve'];
    expect(changedRoles(roles, drafts).map((r) => r.id)).toEqual(['r2']);
  });

  it('never counts the administrator role', () => {
    const admin = role({ id: ADMINISTRATOR_ROLE_ID, name: 'Administrator', privileges: ['*'], builtIn: true });
    const drafts = draftsOf([admin]);
    drafts[ADMINISTRATOR_ROLE_ID] = [];

    expect(changedRoles([admin], drafts)).toEqual([]);
  });
});

describe('matrixRows', () => {
  it('has "everything" first and per area the collective privilege followed by its privileges', () => {
    const { all, groups } = matrixRows(catalog, '');

    expect(all.key).toBe('*');
    expect(groups.map((g) => g.area)).toEqual(['absence', 'certificate']);
    expect(groups[0].head.key).toBe('absence:*');
    expect(groups[0].rows.map((r) => r.key)).toEqual(['absence:approve', 'absence:create-own']);
  });

  it('filters by key and description, without caring for umlauts or case', () => {
    expect(matrixRows(catalog, 'GENEHMIGEN').groups.map((g) => g.area)).toEqual(['absence']);
    expect(matrixRows(catalog, 'pruefen').groups.map((g) => g.area)).toEqual(['certificate']);
    expect(matrixRows(catalog, 'nothing like this').groups).toEqual([]);
  });
});

describe('cellTitle', () => {
  const r = role();
  const row = { key: 'absence:approve', label: 'absence:approve', description: '', kind: 'privilege' as const };

  it('says what a click does, or why there is none', () => {
    expect(cellTitle(r, row, 'none', null, true)).toContain('anklicken zum Vergeben');
    expect(cellTitle(r, row, 'direct', null, true)).toContain('anklicken zum Entfernen');
    expect(cellTitle(r, row, 'covered', 'absence:*', true)).toContain('Sammelrecht absence:*');
    expect(cellTitle(r, row, 'direct', null, false)).toContain('Rolle nicht änderbar');
  });
});
