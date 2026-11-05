import { HttpErrorResponse } from '@angular/common/http';
import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { AuthService } from '../../core/auth/auth.service';
import { PrivilegeSet } from '../../core/auth/privileges';
import { Faq, FaqImage } from './faq-models';
import { FaqApi } from './faq.api';
import { FaqPage } from './faq.page';

const IMG = '3f2b8c1e-1111-4222-8333-944455556666';
const faq = (id: string, title: string, description: string, archived = false): Faq => ({ id, title, description, order: 0, archived, createdAt: '2026-11-01T08:00:00+00:00' });

const f1 = faq('f1', 'Wie beantrage ich Urlaub?', 'Im **Portal** unter Abwesenheiten.');
const f2 = faq('f2', 'Wo melde ich eine Störung?', `Mit dem Formular.\n\n![Formular](https://portal.example/api/faqs/images/${IMG})`);
const f3 = faq('f3', 'Gefährlich', '<img src=x onerror=alert(1)> [x](javascript:alert(1))');
const old = faq('a1', 'Alte Frage', 'Alt', true);
const image: FaqImage = { id: IMG, url: `https://portal.example/api/faqs/images/${IMG}`, createdAt: '2026-11-01T08:00:00+00:00', status: 'active', faqId: 'f2', faqTitle: f2.title };

const grant = (...keys: string[]) => new PrivilegeSet(keys.map((key) => ({ key, unrestricted: true, storeIds: [], employeeIds: [] })));
const envelope = (message: string, status = 403) => new HttpErrorResponse({ status, error: { success: false, message, errors: [] } });
const ALL = ['faq:read', 'faq:create', 'faq:update', 'faq:delete'];

describe('FaqPage', () => {
  let api: Record<'list' | 'listArchived' | 'create' | 'update' | 'reorder' | 'remove' | 'uploadImage' | 'listImages' | 'deleteImage', ReturnType<typeof vi.fn>>;
  let fixture: ComponentFixture<FaqPage>;
  let root: HTMLElement;

  async function create(privileges = grant(...ALL)): Promise<void> {
    TestBed.configureTestingModule({
      providers: [
        { provide: FaqApi, useValue: api },
        { provide: AuthService, useValue: { privileges: signal(privileges) } },
      ],
    });
    fixture = TestBed.createComponent(FaqPage);
    root = fixture.nativeElement as HTMLElement;
    await settle();
  }

  async function settle(): Promise<void> {
    for (let i = 0; i < 4; i++) {
      await fixture.whenStable();
      await new Promise((r) => setTimeout(r));
    }
  }

  const q = <T extends Element = HTMLElement>(selector: string): T | null => root.querySelector<T>(selector);
  const ids = () => [...root.querySelectorAll('[data-testid="faq-row"]')].map((e) => e.getAttribute('data-id'));

  async function click(selector: string): Promise<void> {
    q<HTMLButtonElement>(selector)!.click();
    await settle();
  }

  async function type(selector: string, value: string): Promise<void> {
    const box = q<HTMLInputElement | HTMLTextAreaElement>(selector)!;
    box.value = value;
    box.dispatchEvent(new Event('input', { bubbles: true }));
    await settle();
  }

  beforeEach(() => {
    api = {
      list: vi.fn().mockResolvedValue([f1, f2]),
      listArchived: vi.fn().mockResolvedValue([old]),
      create: vi.fn().mockResolvedValue(f1),
      update: vi.fn().mockResolvedValue(f1),
      reorder: vi.fn((orderedIds: string[]) => Promise.resolve(orderedIds.map((id) => [f1, f2].find((f) => f.id === id)!))),
      remove: vi.fn().mockResolvedValue(undefined),
      uploadImage: vi.fn().mockResolvedValue({ token: IMG, url: image.url }),
      listImages: vi.fn().mockResolvedValue([image]),
      deleteImage: vi.fn().mockResolvedValue(undefined),
    };
  });

  it('is titled "FAQ" and lists the entries', async () => {
    await create(grant());

    expect(q('h1')!.textContent).toContain('FAQ');
    expect(ids()).toEqual(['f1', 'f2']);
    expect(root.textContent).toContain('Wie beantrage ich Urlaub?');
  });

  it('renders the answer (bold text, own images) without using innerHTML of the text', async () => {
    api['list'].mockResolvedValue([f1, f2, f3]);
    await create(grant());

    expect(q('[data-id="f1"] strong')!.textContent).toBe('Portal');
    expect(q<HTMLImageElement>('[data-id="f2"] img')!.getAttribute('src')).toBe(image.url);
    // Markup in the text stays text; unsafe links and foreign images become no elements.
    expect(q('[data-id="f3"] img')).toBeNull();
    expect(q('[data-id="f3"] a')).toBeNull();
    expect(q('[data-id="f3"]')!.textContent).toContain('<img src=x onerror=alert(1)>');
  });

  it('filters by the search text and says so when nothing matches', async () => {
    await create(grant());

    await type('[data-testid="search"]', 'störung');
    expect(ids()).toEqual(['f2']);

    await type('[data-testid="search"]', 'zzz');
    expect(ids()).toEqual([]);
    expect(q('[data-testid="no-match"]')!.textContent).toContain('zzz');
  });

  it('says so when there are no entries', async () => {
    api['list'].mockResolvedValue([]);
    await create(grant());

    expect(q('[data-testid="empty"]')).not.toBeNull();
  });

  it('tells the person if the entries cannot be loaded', async () => {
    api['list'].mockRejectedValue(new HttpErrorResponse({ status: 500 }));
    await create(grant());

    expect(q('[data-testid="list-error"]')!.textContent).toContain('geladen');
  });

  it('shows no management controls without privileges', async () => {
    await create(grant());

    expect(q('[data-testid="new-entry"]')).toBeNull();
    expect(q('[data-testid="archive-button"]')).toBeNull();
    expect(q('[data-testid="images-button"]')).toBeNull();
    expect(q('[data-action="edit"]')).toBeNull();
    expect(q('[data-action="archive"]')).toBeNull();
    expect(q('[data-action="delete"]')).toBeNull();
    expect(q('[data-action="up"]')).toBeNull();
  });

  it('shows only what each privilege allows', async () => {
    await create(grant('faq:update'));

    expect(q('[data-action="edit"]')).not.toBeNull();
    expect(q('[data-action="delete"]')).toBeNull();
    expect(q('[data-testid="new-entry"]')).toBeNull();
  });

  it('creates an entry', async () => {
    await create();

    await click('[data-testid="new-entry"]');
    await type('#faq-title', 'Neue Frage');
    await type('#faq-description', 'Neue Antwort');
    api['list'].mockClear();
    await click('[data-testid="save-entry"]');

    expect(api['create']).toHaveBeenCalledWith('Neue Frage', 'Neue Antwort');
    expect(api['list']).toHaveBeenCalled();
    expect(q('[data-testid="editor"]')).toBeNull();
  });

  it('does not send an entry without question or answer', async () => {
    await create();

    await click('[data-testid="new-entry"]');
    await type('#faq-title', 'Nur Frage');
    await click('[data-testid="save-entry"]');

    expect(api['create']).not.toHaveBeenCalled();
    expect(q('[data-testid="error"]')!.textContent).toContain('Frage und eine Antwort');
  });

  it('changes an entry', async () => {
    await create();

    await click('[data-id="f1"] [data-action="edit"]');
    expect(q<HTMLInputElement>('#faq-title')!.value).toBe(f1.title);
    await type('#faq-title', 'Geänderte Frage');
    await click('[data-testid="save-entry"]');

    expect(api['update']).toHaveBeenCalledWith('f1', { title: 'Geänderte Frage', description: f1.description });
  });

  it('shows the answer of the portal when saving is refused', async () => {
    api['update'].mockRejectedValue(envelope('Dafür fehlt dir das Recht.'));
    await create();

    await click('[data-id="f1"] [data-action="edit"]');
    await click('[data-testid="save-entry"]');

    expect(q('[data-testid="error"]')!.textContent).toContain('Dafür fehlt dir das Recht.');
    expect(q('[data-testid="editor"]')).not.toBeNull();
  });

  it('archives an entry', async () => {
    await create();

    await click('[data-id="f1"] [data-action="archive"]');

    expect(api['update']).toHaveBeenCalledWith('f1', { archived: true });
  });

  it('deletes only after a second click', async () => {
    await create();

    await click('[data-id="f1"] [data-action="delete"]');
    expect(api['remove']).not.toHaveBeenCalled();
    expect(q('[data-id="f1"] [data-action="delete"]')!.textContent).toContain('Wirklich löschen');

    await click('[data-id="f1"] [data-action="delete"]');
    expect(api['remove']).toHaveBeenCalledWith('f1');
  });

  it('moves an entry down and sends the whole order', async () => {
    await create();
    api['list'].mockResolvedValue([f2, f1]); // what the portal answers after the change

    await click('[data-id="f1"] [data-action="down"]');

    expect(api['reorder']).toHaveBeenCalledWith(['f2', 'f1']);
    expect(ids()).toEqual(['f2', 'f1']);
  });

  it('cannot move the first entry up or the last down, and not while searching', async () => {
    await create();

    expect(q<HTMLButtonElement>('[data-id="f1"] [data-action="up"]')!.disabled).toBe(true);
    expect(q<HTMLButtonElement>('[data-id="f2"] [data-action="down"]')!.disabled).toBe(true);

    await type('[data-testid="search"]', 'urlaub');
    expect(q('[data-action="up"]')).toBeNull();
  });

  it('shows the archive and restores an entry', async () => {
    await create();

    await click('[data-testid="archive-button"]');
    expect(ids()).toEqual(['a1']);

    await click('[data-id="a1"] [data-action="restore"]');
    expect(api['update']).toHaveBeenCalledWith('a1', { archived: false });
  });

  it('says so when the archive is empty', async () => {
    api['listArchived'].mockResolvedValue([]);
    await create();

    await click('[data-testid="archive-button"]');

    expect(q('[data-testid="empty-archive"]')).not.toBeNull();
  });

  it('lists the images and deletes one after a second click', async () => {
    await create();

    await click('[data-testid="images-button"]');
    expect(root.querySelectorAll('[data-testid="image-row"]')).toHaveLength(1);
    expect(q('[data-testid="image-status"]')!.textContent).toContain('In Verwendung');

    await click('[data-action="delete-image"]');
    expect(api['deleteImage']).not.toHaveBeenCalled();
    await click('[data-action="delete-image"]');
    expect(api['deleteImage']).toHaveBeenCalledWith(IMG);
  });

  it('shows the answer of the portal when an image is still in use', async () => {
    api['deleteImage'].mockRejectedValue(envelope('Das Bild wird noch in einer aktiven FAQ-Antwort verwendet.', 409));
    await create();

    await click('[data-testid="images-button"]');
    await click('[data-action="delete-image"]');
    await click('[data-action="delete-image"]');

    expect(q('[data-testid="error"]')!.textContent).toContain('noch in einer aktiven');
  });

  it('uploads a PNG', async () => {
    await create();
    await click('[data-testid="images-button"]');

    const file = new File(['x'], 'bild.png', { type: 'image/png' });
    (fixture.componentInstance as unknown as { upload(f: File): Promise<void> }).upload(file);
    await settle();

    expect(api['uploadImage']).toHaveBeenCalledWith(file);
    expect(q('[data-testid="status"]')!.textContent).toContain('hochgeladen');
  });

  it('refuses a file that is no allowed image without calling the portal', async () => {
    await create();
    await click('[data-testid="images-button"]');

    (fixture.componentInstance as unknown as { upload(f: File): Promise<void> }).upload(new File(['x'], 'au.pdf', { type: 'application/pdf' }));
    await settle();

    expect(api['uploadImage']).not.toHaveBeenCalled();
    expect(q('[data-testid="error"]')!.textContent).toContain('PNG');
  });

  it('refuses an image over 5 MB', async () => {
    await create();
    await click('[data-testid="images-button"]');

    const big = new File(['x'], 'gross.png', { type: 'image/png' });
    Object.defineProperty(big, 'size', { value: 6 * 1024 * 1024 });
    (fixture.componentInstance as unknown as { upload(f: File): Promise<void> }).upload(big);
    await settle();

    expect(api['uploadImage']).not.toHaveBeenCalled();
    expect(q('[data-testid="error"]')!.textContent).toContain('zu groß');
  });

  it('hides the upload and the delete without their privileges', async () => {
    await create(grant('faq:read'));

    await click('[data-testid="images-button"]');

    expect(q('[data-testid="image-drop"]')).toBeNull();
    expect(q('[data-action="delete-image"]')).toBeNull();
    expect(root.querySelectorAll('[data-testid="image-row"]')).toHaveLength(1);
  });

  it('inserts an image into the answer being written', async () => {
    await create();

    await click('[data-id="f1"] [data-action="edit"]');
    await click('[data-testid="images-button"]');
    await click('[data-action="insert-image"]');

    expect(q<HTMLTextAreaElement>('#faq-description')!.value).toContain(`![Bild](${image.url})`);
  });
});
