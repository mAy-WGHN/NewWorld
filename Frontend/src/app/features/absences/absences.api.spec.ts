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
    http.expectOne('/svc/workforce/api/me/absences').flush([{ id: 'a1' }]);

    expect(await done).toEqual([{ id: 'a1' }]);
  });

  it('loads the vacation account of a year', async () => {
    const done = api.balance(2027);
    const request = http.expectOne((r) => r.url === '/svc/workforce/api/me/vacation-balance');
    expect(request.request.params.get('year')).toBe('2027');
    request.flush({ year: 2027, entitlement: 30, taken: 0, planned: 5, remaining: 25 });

    expect((await done).remaining).toBe(25);
  });

  it('reports a sick note with its kind and the eAU mark', async () => {
    const done = api.reportSick({ kind: 'FollowUp', from: '2026-12-01', to: '2026-12-03', electronic: true });
    const request = http.expectOne('/svc/workforce/api/me/absences/sick');
    expect(request.request.body).toEqual({ from: '2026-12-01', to: '2026-12-03', kind: 'FollowUp', electronicCertificate: true });
    request.flush({ id: 'a1' });
    await done;
  });

  it('hands in the AU with the sick note, for the period of the sick note', async () => {
    const file = new File(['x'], 'au.pdf', { type: 'application/pdf' });
    const done = api.reportSickNote({ kind: 'First', from: '2026-12-01', to: '2026-12-03', electronic: false, file });
    http.expectOne('/svc/workforce/api/me/absences/sick').flush({ id: 'a1' });
    await new Promise((r) => setTimeout(r));
    const upload = http.expectOne('/svc/workforce/api/me/absences/a1/certificates');
    const form = upload.request.body as FormData;
    expect([form.get('certifiedFrom'), form.get('certifiedTo')]).toEqual(['2026-12-01', '2026-12-03']);
    upload.flush({});

    expect(await done).toEqual({ certificateFailed: false });
  });

  it('keeps the sick note if the upload fails and says so', async () => {
    const file = new File(['x'], 'au.pdf', { type: 'application/pdf' });
    const done = api.reportSickNote({ kind: 'First', from: '2026-12-01', to: '2026-12-03', electronic: false, file });
    http.expectOne('/svc/workforce/api/me/absences/sick').flush({ id: 'a1' });
    await new Promise((r) => setTimeout(r));
    http.expectOne('/svc/workforce/api/me/absences/a1/certificates').flush({ title: 'UnsupportedFileType' }, { status: 415, statusText: 'Unsupported' });

    expect(await done).toEqual({ certificateFailed: true, reason: 'Erlaubt sind nur PDF, JPEG und PNG.' });
  });

  it('uploads nothing when there is no file or the AU is an eAU', async () => {
    const none = api.reportSickNote({ kind: 'First', from: '2026-12-01', to: '2026-12-03', electronic: false, file: null });
    http.expectOne('/svc/workforce/api/me/absences/sick').flush({ id: 'a1' });
    expect(await none).toEqual({ certificateFailed: false });

    const file = new File(['x'], 'au.pdf', { type: 'application/pdf' });
    const electronic = api.reportSickNote({ kind: 'First', from: '2026-12-01', to: '2026-12-03', electronic: true, file });
    http.expectOne('/svc/workforce/api/me/absences/sick').flush({ id: 'a2' });
    expect(await electronic).toEqual({ certificateFailed: false });
    http.expectNone('/svc/workforce/api/me/absences/a2/certificates');
  });

  it('marks the AU as an eAU and takes the mark back', async () => {
    const on = api.setElectronic('a1', true);
    const request = http.expectOne('/svc/workforce/api/me/absences/a1/electronic-certificate');
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual({ electronicCertificate: true });
    request.flush({ id: 'a1' });
    await on;
  });

  it('requests vacation', async () => {
    const done = api.requestVacation('2026-12-21', '2026-12-31');
    const request = http.expectOne('/svc/workforce/api/me/absences/vacation');
    expect(request.request.body).toEqual({ from: '2026-12-21', to: '2026-12-31' });
    request.flush({ id: 'a3' });
    await done;
  });

  it('withdraws vacation and ends a sick note', async () => {
    const withdraw = api.withdraw('a3');
    http.expectOne('/svc/workforce/api/me/absences/a3/withdraw').flush({ id: 'a3' });
    await withdraw;

    const end = api.endSick('a1', '2026-12-04');
    const request = http.expectOne('/svc/workforce/api/me/absences/a1/end');
    expect(request.request.body).toEqual({ to: '2026-12-04' });
    request.flush({ id: 'a1' });
    await end;
  });

  it('uploads a certificate as a form with the period of the certificate', async () => {
    const file = new File([new Uint8Array([1, 2, 3])], 'au.pdf', { type: 'application/pdf' });

    const done = api.uploadCertificate('a1', file, '2026-12-01', '2026-12-05');
    const request = http.expectOne('/svc/workforce/api/me/absences/a1/certificates');
    const form = request.request.body as FormData;
    expect(form.get('certifiedFrom')).toBe('2026-12-01');
    expect(form.get('certifiedTo')).toBe('2026-12-05');
    expect((form.get('file') as File).name).toBe('au.pdf');
    request.flush({ id: 'c1' });
    await done;
  });
});
