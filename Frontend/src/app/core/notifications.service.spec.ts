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
});
