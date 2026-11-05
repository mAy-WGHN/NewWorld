import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { FaqApi } from './faq.api';

describe('FaqApi', () => {
  let api: FaqApi;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    api = TestBed.inject(FaqApi);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('loads the active entries', async () => {
    const done = api.list();
    http.expectOne('/svc/portal/api/faqs').flush({ success: true, data: [{ id: 'f1', title: 'Frage' }] });

    expect(await done).toEqual([{ id: 'f1', title: 'Frage' }]);
  });

  it('attaches a file to an entry as a form', async () => {
    const file = new File(['x'], 'Formular.pdf', { type: 'application/pdf' });
    const done = api.uploadAttachment('f1', file);
    const request = http.expectOne('/svc/portal/api/faqs/f1/attachments');

    expect(request.request.method).toBe('POST');
    expect((request.request.body as FormData).get('file')).toBe(file);
    request.flush({ success: true, data: { id: 'a1', fileName: 'Formular.pdf' } });

    expect(await done).toEqual({ id: 'a1', fileName: 'Formular.pdf' });
  });

  it('removes an attached file', async () => {
    const done = api.deleteAttachment({ id: 'a1' } as never, 'f1');
    const request = http.expectOne('/svc/portal/api/faqs/f1/attachments/a1');

    expect(request.request.method).toBe('DELETE');
    request.flush(null, { status: 204, statusText: 'No Content' });
    await done;
  });

  it('fetches an attached file as data from the path the portal gave', async () => {
    const done = api.downloadAttachment({ url: '/api/faqs/f1/attachments/a1' });
    const request = http.expectOne('/svc/portal/api/faqs/f1/attachments/a1');

    expect(request.request.responseType).toBe('blob');
    request.flush(new Blob(['content']));

    expect(await (await done).text()).toBe('content');
  });

  it('loads the archive', async () => {
    const done = api.listArchived();
    http.expectOne('/svc/portal/api/faqs/archived').flush({ success: true, data: [] });

    expect(await done).toEqual([]);
  });

  it('creates an entry', async () => {
    const done = api.create('Frage', 'Antwort');
    const request = http.expectOne('/svc/portal/api/faqs');
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ title: 'Frage', description: 'Antwort' });
    request.flush({ success: true, data: { id: 'f1' } });

    expect((await done).id).toBe('f1');
  });

  it('changes and archives an entry', async () => {
    const done = api.update('f1', { archived: true });
    const request = http.expectOne('/svc/portal/api/faqs/f1');
    expect(request.request.method).toBe('PATCH');
    expect(request.request.body).toEqual({ archived: true });
    request.flush({ success: true, data: { id: 'f1' } });
    await done;
  });

  it('sends the whole new order', async () => {
    const done = api.reorder(['b', 'a']);
    const request = http.expectOne('/svc/portal/api/faqs/reorder');
    expect(request.request.method).toBe('PATCH');
    expect(request.request.body).toEqual({ orderedIds: ['b', 'a'] });
    request.flush({ success: true, data: [{ id: 'b' }, { id: 'a' }] });

    expect((await done).map((f) => f.id)).toEqual(['b', 'a']);
  });

  it('deletes an entry', async () => {
    const done = api.remove('f1');
    const request = http.expectOne('/svc/portal/api/faqs/f1');
    expect(request.request.method).toBe('DELETE');
    request.flush(null, { status: 204, statusText: 'No Content' });
    await done;
  });

  it('uploads an image as form data with the field "file"', async () => {
    const file = new File(['x'], 'bild.png', { type: 'image/png' });
    const done = api.uploadImage(file);
    const request = http.expectOne('/svc/portal/api/admin/faq/images');
    expect(request.request.method).toBe('POST');
    expect((request.request.body as FormData).get('file')).toBeInstanceOf(File);
    request.flush({ success: true, data: { token: 't', url: 'https://x/api/faqs/images/t' } });

    expect((await done).url).toContain('/api/faqs/images/');
  });

  it('lists images, optionally by status, and deletes one', async () => {
    const all = api.listImages();
    http.expectOne((r) => r.url === '/svc/portal/api/admin/faq/images' && !r.params.has('status')).flush({ success: true, data: [] });
    await all;

    const orphaned = api.listImages('orphaned');
    http.expectOne((r) => r.url === '/svc/portal/api/admin/faq/images' && r.params.get('status') === 'orphaned').flush({ success: true, data: [] });
    await orphaned;

    const removed = api.deleteImage('i1');
    const request = http.expectOne('/svc/portal/api/admin/faq/images/i1');
    expect(request.request.method).toBe('DELETE');
    request.flush(null, { status: 204, statusText: 'No Content' });
    await removed;
  });
});
