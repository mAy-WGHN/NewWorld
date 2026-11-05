import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { AuthService, LoginError } from './auth.service';

const TOKENS = { accessToken: 'access-1', expiresAt: '2026-11-04T12:15:00Z', refreshToken: 'refresh-1' };
const ME = { employeeId: 'e1', roles: ['Public User'], privileges: [{ key: 'faq:read', unrestricted: true, storeIds: [], employeeIds: [] }] };

describe('AuthService', () => {
  let auth: AuthService;
  let http: HttpTestingController;

  beforeEach(() => {
    sessionStorage.clear();
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    auth = TestBed.inject(AuthService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  async function signIn(): Promise<void> {
    const done = auth.login('4711', '1234');
    http.expectOne('/svc/access/api/auth/login').flush(TOKENS);
    await Promise.resolve();
    await new Promise((r) => setTimeout(r));
    http.expectOne('/svc/access/api/me/privileges').flush(ME);
    await done;
  }

  it('signs in, keeps the access token in memory and loads the privileges', async () => {
    await signIn();

    expect(auth.isAuthenticated()).toBe(true);
    expect(auth.token).toBe('access-1');
    expect(auth.employeeId()).toBe('e1');
    expect(auth.privileges().has('faq:read')).toBe(true);
  });

  it('shows the privileges of a previewed role and returns to the real ones afterwards', async () => {
    await signIn();

    auth.startPreview('Revisor', [{ key: 'report:read', unrestricted: true, storeIds: [], employeeIds: [] }]);

    expect(auth.previewRole()).toBe('Revisor');
    expect(auth.privileges().has('report:read')).toBe(true);
    expect(auth.privileges().has('faq:read')).toBe(false);
    expect(auth.realPrivileges().has('faq:read')).toBe(true);

    auth.endPreview();

    expect(auth.previewRole()).toBeNull();
    expect(auth.privileges().has('faq:read')).toBe(true);
  });

  it('ends a preview when the session ends', async () => {
    await signIn();
    auth.startPreview('Revisor', []);

    auth.clear();

    expect(auth.previewRole()).toBeNull();
  });

  it('sends personnel number and PIN only to the login call and never stores the PIN', async () => {
    const done = auth.login('4711', '1234');
    const request = http.expectOne('/svc/access/api/auth/login');
    expect(request.request.body).toEqual({ personnelNumber: '4711', pin: '1234' });
    request.flush(TOKENS);
    await new Promise((r) => setTimeout(r));
    http.expectOne('/svc/access/api/me/privileges').flush(ME);
    await done;

    const stored = JSON.stringify(Object.entries(sessionStorage)) + JSON.stringify(Object.entries(localStorage));
    expect(stored).not.toContain('1234');
    expect(sessionStorage.getItem('nw.refresh')).toBe('refresh-1');
    expect(localStorage.getItem('nw.refresh')).toBeNull();
  });

  it('maps a wrong PIN to "invalid" and stays signed out', async () => {
    const done = auth.login('4711', 'bad');
    http.expectOne('/svc/access/api/auth/login').flush({ title: 'InvalidCredentials' }, { status: 401, statusText: 'Unauthorized' });

    await expect(done).rejects.toMatchObject({ failure: 'invalid' });
    expect(auth.isAuthenticated()).toBe(false);
    expect(sessionStorage.getItem('nw.refresh')).toBeNull();
  });

  it('maps throttling to "locked" with the wait time', async () => {
    const done = auth.login('4711', 'bad');
    http.expectOne('/svc/access/api/auth/login').flush({}, { status: 429, statusText: 'Too Many', headers: { 'Retry-After': '120' } });

    const error = (await done.catch((e: unknown) => e)) as LoginError;
    expect(error.failure).toBe('locked');
    expect(error.retryAfterSeconds).toBe(120);
  });

  it('maps an unavailable login to "unavailable"', async () => {
    const done = auth.login('4711', '1234');
    http.expectOne('/svc/access/api/auth/login').flush({}, { status: 503, statusText: 'Unavailable' });

    await expect(done).rejects.toMatchObject({ failure: 'unavailable' });
  });

  it('restores a session from the refresh token after a reload', async () => {
    sessionStorage.setItem('nw.refresh', 'refresh-0');

    const done = auth.restore();
    const refresh = http.expectOne('/svc/access/api/auth/refresh');
    expect(refresh.request.body).toEqual({ refreshToken: 'refresh-0' });
    refresh.flush({ ...TOKENS, refreshToken: 'refresh-2' });
    await new Promise((r) => setTimeout(r));
    http.expectOne('/svc/access/api/me/privileges').flush(ME);
    await done;

    expect(auth.isAuthenticated()).toBe(true);
    expect(sessionStorage.getItem('nw.refresh')).toBe('refresh-2');
  });

  it('does nothing on restore without a refresh token', async () => {
    await auth.restore();

    expect(auth.isAuthenticated()).toBe(false);
  });

  it('shares one request between concurrent refreshes (the token rotates)', async () => {
    sessionStorage.setItem('nw.refresh', 'refresh-0');
    await signInWithToken();

    const first = auth.refresh();
    const second = auth.refresh();
    http.expectOne('/svc/access/api/auth/refresh').flush({ ...TOKENS, accessToken: 'access-2', refreshToken: 'refresh-3' });

    expect(await first).toBe(true);
    expect(await second).toBe(true);
    expect(auth.token).toBe('access-2');
  });

  it('ends the session when the refresh token is rejected', async () => {
    sessionStorage.setItem('nw.refresh', 'old');

    const done = auth.restore();
    http.expectOne('/svc/access/api/auth/refresh').flush({}, { status: 401, statusText: 'Unauthorized' });
    await done;

    expect(auth.isAuthenticated()).toBe(false);
    expect(sessionStorage.getItem('nw.refresh')).toBeNull();
  });

  it('keeps the refresh token when the network fails (a retry may work)', async () => {
    sessionStorage.setItem('nw.refresh', 'keep');

    const done = auth.refresh();
    http.expectOne('/svc/access/api/auth/refresh').flush({}, { status: 0, statusText: 'Unknown' });

    expect(await done).toBe(false);
    expect(sessionStorage.getItem('nw.refresh')).toBe('keep');
  });

  it('logs out: tells Access and clears everything locally', async () => {
    await signIn();

    const done = auth.logout();
    http.expectOne('/svc/access/api/auth/logout').flush(null, { status: 204, statusText: 'No Content' });
    await done;

    expect(auth.isAuthenticated()).toBe(false);
    expect(auth.token).toBeNull();
    expect(sessionStorage.getItem('nw.refresh')).toBeNull();
  });

  it('clears the local session even if the logout call fails', async () => {
    await signIn();

    const done = auth.logout();
    http.expectOne('/svc/access/api/auth/logout').flush({}, { status: 500, statusText: 'Error' });
    await done;

    expect(auth.isAuthenticated()).toBe(false);
  });

  async function signInWithToken(): Promise<void> {
    const done = auth.restore();
    http.expectOne('/svc/access/api/auth/refresh').flush(TOKENS);
    await new Promise((r) => setTimeout(r));
    http.expectOne('/svc/access/api/me/privileges').flush(ME);
    await done;
  }
});
