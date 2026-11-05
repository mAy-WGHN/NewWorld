import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { AuthService } from './auth/auth.service';
import { PrivilegeSet } from './auth/privileges';
import { NotificationsService } from './notifications.service';

const grant = (...keys: string[]) => new PrivilegeSet(keys.map((key) => ({ key, unrestricted: true, storeIds: [], employeeIds: [] })));

describe('NotificationsService', () => {
  let http: HttpTestingController;
  const privileges = signal(grant('notification:read-own'));

  function create(): NotificationsService {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), { provide: AuthService, useValue: { privileges } }],
    });
    http = TestBed.inject(HttpTestingController);
    return TestBed.inject(NotificationsService);
  }

  beforeEach(() => privileges.set(grant('notification:read-own')));

  afterEach(() => http.verify());

  it('knows nothing before the first answer', () => {
    expect(create().unread()).toBeNull();
  });

  it('counts the unread notifications', async () => {
    const service = create();

    const done = service.refresh();
    http.expectOne('/svc/notifications/api/me/notifications/unread-count').flush({ count: 3 });
    await done;

    expect(service.unread()).toBe(3);
  });

  it('keeps the last count if a refresh fails', async () => {
    const service = create();
    const first = service.refresh();
    http.expectOne('/svc/notifications/api/me/notifications/unread-count').flush({ count: 2 });
    await first;

    const second = service.refresh();
    http.expectOne('/svc/notifications/api/me/notifications/unread-count').flush({}, { status: 503, statusText: 'Unavailable' });
    await second;

    expect(service.unread()).toBe(2);
  });

  it('does not ask without the privilege', async () => {
    privileges.set(grant('faq:read'));
    const service = create();

    await service.refresh();

    http.expectNone('/svc/notifications/api/me/notifications/unread-count');
    expect(service.unread()).toBeNull();
  });

  it('forgets the count on sign-out', async () => {
    const service = create();
    const done = service.refresh();
    http.expectOne('/svc/notifications/api/me/notifications/unread-count').flush({ count: 5 });
    await done;

    service.clear();

    expect(service.unread()).toBeNull();
  });

  describe('arrivals', () => {
    const COUNT = '/svc/notifications/api/me/notifications/unread-count';
    const item = (id: string, readAt: string | null = null) => ({ id, templateKey: 'k', title: `T${id}`, body: `B${id}`, deepLink: '/x', createdAt: '2026-11-05T10:00:00Z', readAt });

    async function count(service: NotificationsService, value: number, newest: unknown[] = []): Promise<void> {
      const done = service.refresh();
      http.expectOne(COUNT).flush({ count: value });
      const list = http.match((r) => r.url === '/svc/notifications/api/me/notifications');
      list.forEach((r) => r.flush(newest));
      await done;
    }

    it('announces nothing on the first count', async () => {
      const service = create();
      await count(service, 4);
      expect(service.arrived()).toBeNull();
    });

    it('announces what came in when the count rises', async () => {
      const service = create();
      await count(service, 1);

      const done = service.refresh();
      http.expectOne(COUNT).flush({ count: 2 });
      await Promise.resolve();
      await Promise.resolve();
      const request = http.expectOne((r) => r.url === '/svc/notifications/api/me/notifications');
      expect(request.request.params.get('take')).toBe('1');
      request.flush([item('a')]);
      await done;

      expect(service.arrived()?.items.map((n) => n.id)).toEqual(['a']);
    });

    it('does not announce when the count stays or falls', async () => {
      const service = create();
      await count(service, 3);
      await count(service, 3);
      await count(service, 1);
      expect(service.arrived()).toBeNull();
    });

    it('asks for at most three at once', async () => {
      const service = create();
      await count(service, 0);

      const done = service.refresh();
      http.expectOne(COUNT).flush({ count: 9 });
      await Promise.resolve();
      await Promise.resolve();
      const request = http.expectOne((r) => r.url === '/svc/notifications/api/me/notifications');
      expect(request.request.params.get('take')).toBe('3');
      request.flush([item('a'), item('b'), item('c')]);
      await done;
    });

    it('forgets the arrival on sign-out', async () => {
      const service = create();
      await count(service, 0);
      const done = service.refresh();
      http.expectOne(COUNT).flush({ count: 1 });
      await Promise.resolve();
      await Promise.resolve();
      http.expectOne((r) => r.url === '/svc/notifications/api/me/notifications').flush([item('a')]);
      await done;

      service.clear();

      expect(service.arrived()).toBeNull();
    });
  });
});
