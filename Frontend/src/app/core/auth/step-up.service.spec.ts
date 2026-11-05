import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { AuthService } from './auth.service';
import { StepUpError, StepUpService, isSecondFactorRequired, problemTitleOf } from './step-up.service';

describe('StepUpService', () => {
  let service: StepUpService;
  let http: HttpTestingController;
  let auth: { acceptAccessToken: ReturnType<typeof vi.fn> };
  let navigated: string[];

  beforeEach(() => {
    sessionStorage.clear();
    auth = { acceptAccessToken: vi.fn() };
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), { provide: AuthService, useValue: auth }] });
    service = TestBed.inject(StepUpService);
    http = TestBed.inject(HttpTestingController);
    navigated = [];
    service.navigate = (url) => navigated.push(url);
  });

  afterEach(() => http.verify());

  it('sends the person to Microsoft and remembers where they were', async () => {
    const begun = service.begin('/payslip');
    http.expectOne('/svc/access/api/auth/step-up/start').flush({ authorizationUrl: 'https://login.microsoftonline.com/x' });
    await begun;

    expect(navigated).toEqual(['https://login.microsoftonline.com/x']);
    expect(service.pendingReturn()).toBe('/payslip');
  });

  it('goes only once if several calls ask at the same time', async () => {
    const first = service.begin('/a');
    const second = service.begin('/a');
    http.expectOne('/svc/access/api/auth/step-up/start').flush({ authorizationUrl: 'https://login.microsoftonline.com/x' });
    await Promise.all([first, second]);

    expect(navigated).toHaveLength(1);
  });

  it('says why it cannot start: no linked account, not available', async () => {
    const noLink = service.begin('/a');
    http.expectOne('/svc/access/api/auth/step-up/start').flush({ title: 'NoEntraLink' }, { status: 404, statusText: 'x' });
    await expect(noLink).rejects.toMatchObject({ failure: 'no-link' });

    const off = service.begin('/a');
    http.expectOne('/svc/access/api/auth/step-up/start').flush({ title: 'StepUpUnavailable' }, { status: 503, statusText: 'x' });
    await expect(off).rejects.toMatchObject({ failure: 'unavailable' });
    expect(navigated).toEqual([]);
  });

  it('takes the new access token after the return and gives back the address to continue with', async () => {
    const begun = service.begin('/payslip');
    http.expectOne('/svc/access/api/auth/step-up/start').flush({ authorizationUrl: 'https://login.microsoftonline.com/x' });
    await begun;

    const done = service.complete();
    http.expectOne('/svc/access/api/auth/step-up/token').flush({ accessToken: 'new-token', expiresAt: '2026-11-05T12:00:00Z' });

    expect(await done).toBe('/payslip');
    expect(auth.acceptAccessToken).toHaveBeenCalledWith('new-token');
    expect(service.pendingReturn()).toBe('/');
  });

  it('reports a missing confirmation', async () => {
    const done = service.complete();
    http.expectOne('/svc/access/api/auth/step-up/token').flush({ title: 'SecondFactorRequired' }, { status: 409, statusText: 'x' });

    await expect(done).rejects.toEqual(expect.any(StepUpError));
    expect(auth.acceptAccessToken).not.toHaveBeenCalled();
  });

  it('does not take the person to a foreign address from the storage', () => {
    for (const bad of ['https://evil.example', '//evil.example', 'javascript:alert(1)']) {
      sessionStorage.setItem('nw.stepup.return', bad);
      expect(service.pendingReturn()).toBe('/');
    }
  });

  it('recognises the refusal that only lacks the second factor', () => {
    const lacking = new HttpErrorResponse({ status: 403, error: { title: 'SecondFactorRequired' } });
    expect(isSecondFactorRequired(lacking)).toBe(true);
    expect(isSecondFactorRequired(new HttpErrorResponse({ status: 403, error: { title: 'Forbidden' } }))).toBe(false);
    expect(isSecondFactorRequired(new HttpErrorResponse({ status: 409, error: { title: 'SecondFactorRequired' } }))).toBe(false);
    expect(isSecondFactorRequired(new Error('x'))).toBe(false);
    expect(problemTitleOf(lacking)).toBe('SecondFactorRequired');
  });
});
