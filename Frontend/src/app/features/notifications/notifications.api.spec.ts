import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { NotificationsApi } from './notifications.api';

describe('NotificationsApi', () => {
  let api: NotificationsApi;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    api = TestBed.inject(NotificationsApi);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('loads the newest notifications', async () => {
    const done = api.list(50);
    const request = http.expectOne((r) => r.url === '/svc/notifications/api/me/notifications');
    expect(request.request.params.get('take')).toBe('50');
    expect(request.request.params.has('before')).toBe(false);
    request.flush([{ id: 'n1' }]);

    expect(await done).toEqual([{ id: 'n1' }]);
  });

  it('loads older ones before a time', async () => {
    const done = api.list(50, '2026-11-01T08:00:00+00:00');
    const request = http.expectOne((r) => r.url === '/svc/notifications/api/me/notifications');
    expect(request.request.params.get('before')).toBe('2026-11-01T08:00:00+00:00');
    request.flush([]);
    await done;
  });

  it('marks one or all notifications as read', async () => {
    const one = api.markRead('n1');
    const first = http.expectOne('/svc/notifications/api/me/notifications/n1/read');
    expect(first.request.method).toBe('POST');
    first.flush(null, { status: 204, statusText: 'No Content' });
    await one;

    const all = api.markAllRead();
    http.expectOne('/svc/notifications/api/me/notifications/read-all').flush(null, { status: 204, statusText: 'No Content' });
    await all;
  });

  it('loads the choices per kind and channel and changes one', async () => {
    const list = api.preferences();
    http.expectOne('/svc/notifications/api/me/notification-preferences').flush([{ templateKey: 'k', mandatory: false, channels: [{ channel: 'Push', enabled: true }] }]);
    expect((await list)[0].templateKey).toBe('k');

    const set = api.setPreference('workforce.vacation-decision.requester', 'Push', false);
    const request = http.expectOne('/svc/notifications/api/me/notification-preferences/workforce.vacation-decision.requester/Push');
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual({ enabled: false });
    request.flush(null, { status: 204, statusText: 'No Content' });
    await set;
  });
});
