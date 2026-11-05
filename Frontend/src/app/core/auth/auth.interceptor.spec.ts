import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { authInterceptor } from './auth.interceptor';
import { AuthService } from './auth.service';
import { StepUpService } from './step-up.service';

const TOKENS = { accessToken: 'access-1', expiresAt: '2026-11-04T12:15:00Z', refreshToken: 'refresh-1' };
const ME = { employeeId: 'e1', roles: [], privileges: [] };

describe('authInterceptor', () => {
  let client: HttpClient;
  let http: HttpTestingController;
  let auth: AuthService;
  let router: Router;

  beforeEach(async () => {
    sessionStorage.clear();
    TestBed.configureTestingModule({
      providers: [provideRouter([]), provideHttpClient(withInterceptors([authInterceptor])), provideHttpClientTesting()],
    });
    client = TestBed.inject(HttpClient);
    http = TestBed.inject(HttpTestingController);
    auth = TestBed.inject(AuthService);
    router = TestBed.inject(Router);

    sessionStorage.setItem('nw.refresh', 'refresh-0');
    const restored = auth.restore();
    http.expectOne('/svc/access/api/auth/refresh').flush(TOKENS);
    await new Promise((r) => setTimeout(r));
    http.expectOne('/svc/access/api/me/privileges').flush(ME);
    await restored;
  });

  afterEach(() => http.verify());

  it('adds the access token to calls to our services', () => {
    client.get('/svc/workforce/api/me/absences').subscribe();

    const request = http.expectOne('/svc/workforce/api/me/absences');
    expect(request.request.headers.get('Authorization')).toBe('Bearer access-1');
    request.flush([]);
  });

  it('adds no token to other addresses', () => {
    client.get('https://example.org/data').subscribe();

    const request = http.expectOne('https://example.org/data');
    expect(request.request.headers.has('Authorization')).toBe(false);
    request.flush({});
  });

  it('leaves the login and refresh calls alone', () => {
    client.post('/svc/access/api/auth/login', {}).subscribe({ error: () => undefined });

    const request = http.expectOne('/svc/access/api/auth/login');
    expect(request.request.headers.has('Authorization')).toBe(false);
    request.flush({}, { status: 401, statusText: 'Unauthorized' });
  });

  it('refreshes once on 401 and repeats the call with the new token', async () => {
    let result: unknown;
    client.get('/svc/portal/api/reports').subscribe((r) => (result = r));

    http.expectOne('/svc/portal/api/reports').flush({}, { status: 401, statusText: 'Unauthorized' });
    await new Promise((r) => setTimeout(r));
    http.expectOne('/svc/access/api/auth/refresh').flush({ ...TOKENS, accessToken: 'access-2', refreshToken: 'refresh-2' });
    await new Promise((r) => setTimeout(r));

    const retry = http.expectOne('/svc/portal/api/reports');
    expect(retry.request.headers.get('Authorization')).toBe('Bearer access-2');
    retry.flush(['report']);
    expect(result).toEqual(['report']);
  });

  it('ends the session and goes to the login page if the refresh fails', async () => {
    const navigate = vi.spyOn(router, 'navigate').mockResolvedValue(true);
    let failed = false;
    client.get('/svc/portal/api/reports').subscribe({ error: () => (failed = true) });

    http.expectOne('/svc/portal/api/reports').flush({}, { status: 401, statusText: 'Unauthorized' });
    await new Promise((r) => setTimeout(r));
    http.expectOne('/svc/access/api/auth/refresh').flush({}, { status: 401, statusText: 'Unauthorized' });
    await new Promise((r) => setTimeout(r));

    expect(failed).toBe(true);
    expect(auth.isAuthenticated()).toBe(false);
    expect(navigate).toHaveBeenCalledWith(['/login'], expect.anything());
  });

  it('passes other errors through without a refresh', () => {
    let status = 0;
    client.get('/svc/portal/api/reports').subscribe({ error: (e) => (status = e.status) });

    http.expectOne('/svc/portal/api/reports').flush({}, { status: 403, statusText: 'Forbidden' });

    expect(status).toBe(403);
    http.expectNone('/svc/access/api/auth/refresh');
  });

  it('starts the second factor when a service only lacks it, and still passes the error on', async () => {
    const stepUp = TestBed.inject(StepUpService);
    const navigated: string[] = [];
    stepUp.navigate = (url) => navigated.push(url);
    let status = 0;
    client.get('/svc/workforce/api/me/payslips').subscribe({ error: (e) => (status = e.status) });

    http.expectOne('/svc/workforce/api/me/payslips').flush({ title: 'SecondFactorRequired' }, { status: 403, statusText: 'Forbidden' });
    http.expectOne('/svc/access/api/auth/step-up/start').flush({ authorizationUrl: 'https://login.microsoftonline.com/x' });
    await new Promise((r) => setTimeout(r));

    expect(status).toBe(403);
    expect(navigated).toEqual(['https://login.microsoftonline.com/x']);
  });

  it('does not start the second factor for an ordinary refusal', () => {
    client.get('/svc/workforce/api/me/payslips').subscribe({ error: () => undefined });

    http.expectOne('/svc/workforce/api/me/payslips').flush({ title: 'Forbidden' }, { status: 403, statusText: 'Forbidden' });

    http.expectNone('/svc/access/api/auth/step-up/start');
  });
});
