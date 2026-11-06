import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { AUDIT_LIMIT, AuditEntry, AuditPage, actorLabel, mergeByTime } from './audit.page';

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

describe('audit page: older entries', () => {
  const page = (service: string, from: number, count: number, minute0 = 59) =>
    Array.from({ length: count }, (_, i) =>
      entry(`${service}-${from + i}`, `2026-11-06T10:${String(minute0 - from - i).padStart(2, '0')}:00Z`, { service }),
    );

  async function settle(fixture: ComponentFixture<AuditPage>): Promise<void> {
    for (let i = 0; i < 4; i++) {
      await fixture.whenStable();
      await new Promise((r) => setTimeout(r));
    }
  }

  async function create(): Promise<{ fixture: ComponentFixture<AuditPage>; http: HttpTestingController }> {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    const http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(AuditPage);
    await settle(fixture);
    return { fixture, http };
  }

  it('offers older lines only while a service may have more, and asks for the page after its last line seen', async () => {
    const { fixture, http } = await create();
    const root = fixture.nativeElement as HTMLElement;
    const first = http.expectOne((r) => r.url === '/svc/access/api/audit' && r.params.get('limit') === '50');
    first.flush(page('access', 0, AUDIT_LIMIT));
    for (const s of ['workforce', 'portal', 'notifications']) {
      http.expectOne((r) => r.url === `/svc/${s}/api/audit`).flush([]);
    }
    await settle(fixture);

    const button = root.querySelector<HTMLButtonElement>('[data-action="load-more"]');
    expect(button).not.toBeNull();
    button!.click();
    await settle(fixture);

    const next = http.expectOne((r) => r.url === '/svc/access/api/audit' && r.params.has('before'));
    expect(next.request.params.get('before')).toBe('2026-11-06T10:10:00Z'); // the last of the 50 (minutes 59 down to 10)
    expect(next.request.params.get('beforeId')).toBe('access-49');
    http.expectNone((r) => r.url !== '/svc/access/api/audit'); // the others had less than a page: nothing more to ask
    next.flush(page('access', AUDIT_LIMIT, 3)); // fewer than a page: the end
    await settle(fixture);

    expect(root.querySelector('[data-action="load-more"]')).toBeNull();
    http.verify();
  });

  it('shows no button when every service has given all it has', async () => {
    const { fixture, http } = await create();
    http.expectOne((r) => r.url === '/svc/access/api/audit').flush(page('access', 0, 3));
    for (const s of ['workforce', 'portal', 'notifications']) {
      http.expectOne((r) => r.url === `/svc/${s}/api/audit`).flush([]);
    }
    await settle(fixture);

    expect((fixture.nativeElement as HTMLElement).querySelector('[data-action="load-more"]')).toBeNull();
  });
});
