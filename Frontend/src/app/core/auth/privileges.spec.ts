import { PrivilegeSet } from './privileges';

const grant = (key: string) => ({ key, unrestricted: true, storeIds: [], employeeIds: [] });

describe('PrivilegeSet', () => {
  const set = new PrivilegeSet([grant('absence:read-own'), grant('faq:read')]);

  it('knows exactly the granted keys', () => {
    expect(set.has('absence:read-own')).toBe(true);
    expect(set.has('absence:approve')).toBe(false);
  });

  it('treats a prefix or wildcard string as a plain key (Access expands wildcards itself)', () => {
    expect(set.has('absence:*')).toBe(false);
    expect(set.has('absence')).toBe(false);
  });

  it('hasAny is true if one of the keys is granted', () => {
    expect(set.hasAny(['absence:approve', 'faq:read'])).toBe(true);
    expect(set.hasAny(['absence:approve', 'report:read'])).toBe(false);
  });

  it('needs no privilege for an empty or missing list', () => {
    expect(set.hasAny(undefined)).toBe(true);
    expect(set.hasAny([])).toBe(true);
    expect(PrivilegeSet.empty.hasAny(undefined)).toBe(true);
  });

  it('keeps the reach of a grant', () => {
    const restricted = new PrivilegeSet([{ key: 'absence:approve', unrestricted: false, storeIds: ['s1'], employeeIds: [] }]);
    expect(restricted.get('absence:approve')?.storeIds).toEqual(['s1']);
  });
});
