import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { AbsencesApi } from './absences.api';

describe('AbsencesApi', () => {
  let api: AbsencesApi;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    api = TestBed.inject(AbsencesApi);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('loads the own absences', async () => {
    const done = api.list();
    http.expectOne('/workforce/api/me/absences').flush([{ id: 'a1' }]);

    expect(await done).toEqual([{ id: 'a1' }]);
  });

  it('loads the vacation account of a year', async () => {
    const done = api.balance(2027);
    const request = http.expectOne((r) => r.url === '/workforce/api/me/vacation-balance');
    expect(request.request.params.get('year')).toBe('2027');
    request.flush({ year: 2027, entitlement: 30, taken: 0, planned: 5, remaining: 25 });

    expect((await done).remaining).toBe(25);
  });

  it('reports a sick note, with and without an end', async () => {
    const open = api.reportSick('2026-12-01', null);
    const first = http.expectOne('/workforce/api/me/absences/sick');
    expect(first.request.body).toEqual({ from: '2026-12-01', to: null });
    first.flush({ id: 'a1' });
    await open;

    const closed = api.reportSick('2026-12-01', '2026-12-03');
    const second = http.expectOne('/workforce/api/me/absences/sick');
    expect(second.request.body).toEqual({ from: '2026-12-01', to: '2026-12-03' });
    second.flush({ id: 'a2' });
    await closed;
  });

  it('requests vacation', async () => {
    const done = api.requestVacation('2026-12-21', '2026-12-31');
    const request = http.expectOne('/workforce/api/me/absences/vacation');
    expect(request.request.body).toEqual({ from: '2026-12-21', to: '2026-12-31' });
    request.flush({ id: 'a3' });
    await done;
  });

  it('withdraws vacation and ends a sick note', async () => {
    const withdraw = api.withdraw('a3');
    http.expectOne('/workforce/api/me/absences/a3/withdraw').flush({ id: 'a3' });
    await withdraw;

    const end = api.endSick('a1', '2026-12-04');
    const request = http.expectOne('/workforce/api/me/absences/a1/end');
    expect(request.request.body).toEqual({ to: '2026-12-04' });
    request.flush({ id: 'a1' });
    await end;
  });

  it('uploads a certificate as a form with the period of the certificate', async () => {
    const file = new File([new Uint8Array([1, 2, 3])], 'au.pdf', { type: 'application/pdf' });

    const done = api.uploadCertificate('a1', file, '2026-12-01', '2026-12-05');
    const request = http.expectOne('/workforce/api/me/absences/a1/certificates');
    const form = request.request.body as FormData;
    expect(form.get('certifiedFrom')).toBe('2026-12-01');
    expect(form.get('certifiedTo')).toBe('2026-12-05');
    expect((form.get('file') as File).name).toBe('au.pdf');
    request.flush({ id: 'c1' });
    await done;
  });
});
