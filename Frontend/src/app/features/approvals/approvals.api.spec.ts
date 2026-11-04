import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ApprovalsApi } from './approvals.api';

describe('ApprovalsApi', () => {
  let api: ApprovalsApi;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    api = TestBed.inject(ApprovalsApi);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('loads the vacation requests that wait for the own decision', async () => {
    const done = api.pending();
    http.expectOne('/svc/workforce/api/approvals/pending').flush([{ absenceId: 'a1', employeeId: 'e1', employeeName: 'Erika Mustermann', from: '2026-12-21', to: '2026-12-31' }]);

    expect((await done)[0].employeeName).toBe('Erika Mustermann');
  });

  it('approves a request without a body', async () => {
    const done = api.approve('a1');
    const request = http.expectOne('/svc/workforce/api/absences/a1/approve');
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toBeNull();
    request.flush({ id: 'a1', status: 'Approved' });
    await done;
  });

  it('rejects a request without a body', async () => {
    const done = api.reject('a1');
    const request = http.expectOne('/svc/workforce/api/absences/a1/reject');
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toBeNull();
    request.flush({ id: 'a1', status: 'Rejected' });
    await done;
  });
});
