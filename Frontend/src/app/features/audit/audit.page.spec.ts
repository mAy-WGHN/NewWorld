import { AuditEntry, actorLabel, mergeByTime } from './audit.page';

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

  it('shows name and personnel number, a service by its name and nobody as anonymous', () => {
    expect(actorLabel(entry('a', '2028-03-01T00:00:00Z'))).toBe('Erika Mustermann (10042)');
    expect(actorLabel(entry('b', '2028-03-01T00:00:00Z', { actorKind: 'Service', actorName: 'workforce' }))).toBe('Dienst workforce');
    expect(actorLabel(entry('c', '2028-03-01T00:00:00Z', { actorKind: 'Anonymous', actorName: '' }))).toBe('Anonym');
  });
});
