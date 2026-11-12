import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { LogbookApi } from './logbook.api';

describe('LogbookApi', () => {
  let api: LogbookApi;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    api = TestBed.inject(LogbookApi);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('asks the portal for the reports of the person for a week and unwraps the answer', async () => {
    const done = api.myReports('2026-10-19');
    const request = http.expectOne((r) => r.url === '/svc/portal/api/logbook/my-reports');
    expect(request.request.method).toBe('GET');
    expect(request.request.params.get('week')).toBe('2026-10-19');
    request.flush({ success: true, data: { week: '2026-10-19', stores: [] } });

    expect(await done).toEqual({ week: '2026-10-19', stores: [] });
  });

  it('asks without a week for the current one', async () => {
    const done = api.myReports();
    const request = http.expectOne('/svc/portal/api/logbook/my-reports');
    expect(request.request.params.keys()).toEqual([]);
    request.flush({ success: true, data: { week: 'x', stores: [] } });
    await done;
  });

  it('saves a draft and hands in per store and week', async () => {
    const save = api.save('s1', '2026-10-19', { a: { optionIds: ['o'], text: null } });
    const put = http.expectOne('/svc/portal/api/logbook/reports/s1/2026-10-19');
    expect(put.request.method).toBe('PUT');
    expect(put.request.body).toEqual({ answers: { a: { optionIds: ['o'], text: null } } });
    put.flush({ success: true, data: { id: 'r1' } });
    await save;

    const submit = api.submit('s1', '2026-10-19');
    const post = http.expectOne('/svc/portal/api/logbook/reports/s1/2026-10-19/submit');
    expect(post.request.method).toBe('POST');
    post.flush({ success: true, data: { id: 'r1' } });
    await submit;
  });

  it('loads the template of a version and the history of a store', async () => {
    const template = api.template(3);
    http.expectOne('/svc/portal/api/logbook/template/3').flush({ success: true, data: { version: 3, sections: [] } });
    expect((await template).version).toBe(3);

    const history = api.history('s1');
    const request = http.expectOne((r) => r.url === '/svc/portal/api/logbook/stores/s1/reports');
    expect(request.request.params.get('take')).toBe('12');
    request.flush({ success: true, data: [] });
    expect(await history).toEqual([]);
  });

  it('lists the reports of the leaders with the filters that are set', async () => {
    const done = api.list({ open: true, state: 'followedUp' });
    const request = http.expectOne((r) => r.url === '/svc/portal/api/logbook/reports');
    expect(request.request.params.get('open')).toBe('true');
    expect(request.request.params.get('state')).toBe('followedUp');
    expect(request.request.params.has('week')).toBe(false);
    request.flush({ success: true, data: [] });
    await done;
  });

  it('sends comments, state, escalation and reminders as the service expects them', async () => {
    const comment = api.comment('r1', 'internal', 'Hallo');
    const c = http.expectOne('/svc/portal/api/logbook/reports/r1/comments');
    expect(c.request.body).toEqual({ kind: 'internal', text: 'Hallo' });
    c.flush({ success: true, data: { id: 'c1' } });
    await comment;

    const state = api.setState('r1', 'done');
    const s = http.expectOne('/svc/portal/api/logbook/reports/r1/state');
    expect(s.request.method).toBe('PUT');
    expect(s.request.body).toEqual({ state: 'done' });
    s.flush({ success: true, data: {} });
    await state;

    const escalate = api.escalate('r1', null);
    const e = http.expectOne('/svc/portal/api/logbook/reports/r1/escalate');
    expect(e.request.body).toEqual({ note: null });
    e.flush({ success: true, data: {} });
    await escalate;

    const followUp = api.followUp('r1', 'Erledigt');
    const f = http.expectOne('/svc/portal/api/logbook/reports/r1/follow-up');
    expect(f.request.body).toEqual({ note: 'Erledigt' });
    f.flush({ success: true, data: {} });
    await followUp;

    const remind = api.remind('s1', '2026-10-19');
    const r = http.expectOne('/svc/portal/api/logbook/reminders');
    expect(r.request.body).toEqual({ storeId: 's1', week: '2026-10-19' });
    r.flush({ success: true, data: { id: 'x' } });
    expect(await remind).toBeUndefined();
  });

  it('keeps only the stores that are not archived for the hierarchy', async () => {
    const done = api.stores();
    http.expectOne('/svc/portal/api/locations').flush({
      success: true,
      data: { items: [{ id: 'a', name: 'A', storeCode: '1', archived: false }, { id: 'b', name: 'B', storeCode: '2', archived: true }] },
    });

    expect((await done).map((s) => s.id)).toEqual(['a']);
  });
});
