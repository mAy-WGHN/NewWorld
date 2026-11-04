import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ReportsApi } from './reports.api';

describe('ReportsApi', () => {
  let api: ReportsApi;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    api = TestBed.inject(ReportsApi);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  const page = { success: true, data: { items: [{ id: 'r1' }], meta: { total: 7, offset: 0, limit: 20 } } };

  it('loads the open reports of the portal without the archived ones', async () => {
    const done = api.list('open', 0);
    const request = http.expectOne((r) => r.url === '/svc/portal/api/reports');
    expect(request.request.params.get('status')).toBe('open');
    expect(request.request.params.get('archived')).toBe('false');
    expect(request.request.params.get('offset')).toBe('0');
    expect(request.request.params.get('limit')).toBe('20');
    request.flush(page);

    const result = await done;
    expect(result.items).toEqual([{ id: 'r1' }]);
    expect(result.meta.total).toBe(7);
  });

  it('asks for the next page with an offset', async () => {
    const done = api.list('done', 20, 10);
    const request = http.expectOne((r) => r.url === '/svc/portal/api/reports');
    expect(request.request.params.get('status')).toBe('done');
    expect(request.request.params.get('offset')).toBe('20');
    expect(request.request.params.get('limit')).toBe('10');
    request.flush(page);
    await done;
  });

  it('loads the archive', async () => {
    const done = api.listArchived(40);
    const request = http.expectOne((r) => r.url === '/svc/portal/api/reports/archived');
    expect(request.request.params.get('offset')).toBe('40');
    request.flush(page);

    expect((await done).items).toHaveLength(1);
  });

  it('sets the state of a report', async () => {
    const done = api.setStatus('r1', 'done');
    const request = http.expectOne('/svc/portal/api/reports/r1');
    expect(request.request.method).toBe('PATCH');
    expect(request.request.body).toEqual({ status: 'done' });
    request.flush({ success: true, data: { id: 'r1', status: 'done' } });

    expect((await done).status).toBe('done');
  });

  it('saves the comment of a report', async () => {
    const done = api.saveComment('r1', 'Rückruf folgt');
    const request = http.expectOne('/svc/portal/api/reports/r1');
    expect(request.request.body).toEqual({ comment: 'Rückruf folgt' });
    request.flush({ success: true, data: { id: 'r1' } });
    await done;
  });

  it('archives and restores a report', async () => {
    const archive = api.archive('r1');
    const deleted = http.expectOne('/svc/portal/api/reports/r1');
    expect(deleted.request.method).toBe('DELETE');
    deleted.flush(null, { status: 204, statusText: 'No Content' });
    await archive;

    const restore = api.restore('r1');
    const patched = http.expectOne('/svc/portal/api/reports/r1');
    expect(patched.request.method).toBe('PATCH');
    expect(patched.request.body).toEqual({ archived: false });
    patched.flush({ success: true, data: { id: 'r1' } });
    await restore;
  });
});
