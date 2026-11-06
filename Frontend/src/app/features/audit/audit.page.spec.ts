import { AUDIT_LIMIT, AuditEntry, actorLabel, mergeByTime } from './audit.page';

const entry = (id: string, occurredAt: string, over: Partial<AuditEntry> = {}): AuditEntry => ({
  id,
  occurredAt,
  service: 'portal',
  actorKind: 'Person',
  actorPersonnelNumber: '10042',
  actorName: 'Erika Mustermann',
  action: 'Create',
  entityType: 'FaqRecord',
  entityId: id,
  changes: null,
  ...over,
});

describe('audit page', () => {
  it('merges the logs of the services newest first and cuts at the limit', () => {
    const merged = mergeByTime(
      [
        [entry('a', '2028-03-01T00:15:00Z'), entry('b', '2028-02-29T23:30:00Z')],
        [entry('c', '2028-03-01T08:00:00Z', { service: 'access' })],
      ],
      2,
    );
    expect(merged.map((e) => e.id)).toEqual(['c', 'a']);
  });

  it('shows only the newest 50 entries even if every service sends 50', () => {
    const list = (service: string, minute: number) =>
      Array.from({ length: AUDIT_LIMIT }, (_, i) => entry(`${service}${i}`, `2026-11-06T10:${String(minute + i).padStart(2, '0')}:00Z`));
    const merged = mergeByTime([list('a', 0), list('b', 0)], AUDIT_LIMIT);
    expect(AUDIT_LIMIT).toBe(50);
    expect(merged).toHaveLength(50);
    expect(merged[0].occurredAt).toBe('2026-11-06T10:49:00Z');
    expect(merged.at(-1)!.occurredAt).toBe('2026-11-06T10:25:00Z');
  });

  it('shows name and personnel number, a service by its name and nobody as anonymous', () => {
    expect(actorLabel(entry('a', '2028-03-01T00:00:00Z'))).toBe('Erika Mustermann (10042)');
    expect(actorLabel(entry('b', '2028-03-01T00:00:00Z', { actorKind: 'Service', actorName: 'workforce' }))).toBe('Dienst workforce');
    expect(actorLabel(entry('c', '2028-03-01T00:00:00Z', { actorKind: 'Anonymous', actorName: '' }))).toBe('Anonym');
  });
});
