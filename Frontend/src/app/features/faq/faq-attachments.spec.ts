import { ComponentFixture, TestBed } from '@angular/core/testing';
import { FaqAttachment, attachmentIcon, attachmentProblem, opensInTab } from './faq-models';
import { FaqAttachments } from './faq-attachments';
import { FaqApi } from './faq.api';

const attachment = (over: Partial<FaqAttachment> = {}): FaqAttachment => ({
  id: 'a1', fileName: 'Formular.pdf', contentType: 'application/pdf', length: 2048, isImage: false, url: '/api/faqs/f1/attachments/a1', ...over,
});
const photo = attachment({ id: 'p1', fileName: 'Foto.png', contentType: 'image/png', isImage: true, url: '/api/faqs/f1/attachments/p1' });
const word = attachment({ id: 'w1', fileName: 'Anleitung.docx', contentType: 'application/vnd.openxmlformats-officedocument.wordprocessingml.document', length: 1536, url: '/api/faqs/f1/attachments/w1' });

describe('FaqAttachments', () => {
  let api: Record<'downloadAttachment', ReturnType<typeof vi.fn>>;
  let fixture: ComponentFixture<FaqAttachments>;
  let root: HTMLElement;
  let revoked: string[];

  async function create(attachments: FaqAttachment[]): Promise<void> {
    TestBed.configureTestingModule({ providers: [{ provide: FaqApi, useValue: api }] });
    fixture = TestBed.createComponent(FaqAttachments);
    fixture.componentRef.setInput('attachments', attachments);
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

  beforeEach(() => {
    revoked = [];
    let n = 0;
    URL.createObjectURL = vi.fn(() => `blob:file-${++n}`);
    URL.revokeObjectURL = vi.fn((url: string) => void revoked.push(url));
    api = { downloadAttachment: vi.fn().mockResolvedValue(new Blob(['x'])) };
  });

  it('shows nothing without attachments', async () => {
    await create([]);

    expect(q('[data-testid="attachments"]')).toBeNull();
  });

  it('shows images as previews and the other files as buttons with name and size', async () => {
    await create([photo, attachment(), word]);

    expect(q('[data-testid="attachment-image"] img')!.getAttribute('src')).toBe('blob:file-1');
    const files = [...root.querySelectorAll('[data-testid="attachment-file"]')].map((e) => e.textContent!.replace(/\s+/g, ' ').trim());
    expect(files).toEqual(['picture_as_pdf Formular.pdf (2,0 KB)', 'description Anleitung.docx (1,5 KB)']);
  });

  it('fetches only the images for the previews, not the other files', async () => {
    await create([photo, attachment(), word]);

    expect(api['downloadAttachment']).toHaveBeenCalledTimes(1);
    expect(api['downloadAttachment']).toHaveBeenCalledWith(photo);
  });

  it('opens a PDF in a new tab', async () => {
    const open = vi.spyOn(window, 'open').mockReturnValue(null);
    await create([attachment()]);

    q<HTMLButtonElement>('[data-testid="attachment-file"]')!.click();
    await settle();

    expect(api['downloadAttachment']).toHaveBeenCalledWith(attachment());
    expect(open).toHaveBeenCalledWith('blob:file-1', '_blank', 'noopener');
    open.mockRestore();
  });

  it('opens an image from its preview in a new tab', async () => {
    const open = vi.spyOn(window, 'open').mockReturnValue(null);
    await create([photo]);

    q<HTMLButtonElement>('[data-testid="attachment-image"]')!.click();
    await settle();

    expect(open).toHaveBeenCalledWith(expect.stringMatching(/^blob:/), '_blank', 'noopener');
    open.mockRestore();
  });

  it('downloads an Office document with its name instead of opening it', async () => {
    const open = vi.spyOn(window, 'open').mockReturnValue(null);
    const clicked: { download: string; href: string }[] = [];
    const click = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(function (this: HTMLAnchorElement) {
      clicked.push({ download: this.download, href: this.href });
    });
    await create([word]);

    q<HTMLButtonElement>('[data-testid="attachment-file"]')!.click();
    await settle();

    expect(open).not.toHaveBeenCalled();
    expect(clicked).toEqual([{ download: 'Anleitung.docx', href: 'blob:file-1' }]);
    open.mockRestore();
    click.mockRestore();
  });

  it('says so when a file cannot be fetched, and the page stays usable', async () => {
    api['downloadAttachment'].mockRejectedValue(new Error('403'));
    await create([attachment()]);

    q<HTMLButtonElement>('[data-testid="attachment-file"]')!.click();
    await settle();

    expect(q('[data-testid="attachment-error"]')!.textContent).toContain('Formular.pdf');
    expect(q('[data-testid="attachment-error"]')!.textContent).toContain('konnte nicht geladen werden');
  });

  it('keeps the name as the button if a preview cannot be loaded', async () => {
    api['downloadAttachment'].mockRejectedValue(new Error('403'));
    await create([photo]);

    expect(q('[data-testid="attachment-image"] img')).toBeNull();
    expect(q('[data-testid="attachment-image"]')!.textContent).toContain('Foto.png');
  });

  it('gives the addresses of the previews back when it goes away', async () => {
    await create([photo]);

    fixture.destroy();

    expect(revoked).toContain('blob:file-1');
  });
});

describe('attachment helpers', () => {
  const file = (name: string, size = 10) => ({ name, size });

  it('accepts PDF, images and Office documents by their ending, in any case', () => {
    for (const name of ['a.pdf', 'a.PDF', 'a.png', 'a.jpg', 'a.JPEG', 'a.webp', 'a.docx', 'a.xlsx', 'a.pptx', 'a.doc', 'a.xls', 'a.ppt']) {
      expect(attachmentProblem(file(name))).toBeNull();
    }
  });

  it('refuses everything else, also a file without an ending', () => {
    for (const name of ['a.exe', 'a.html', 'a.svg', 'a.zip', 'a.docm', 'a.txt', 'a', 'pdf']) {
      expect(attachmentProblem(file(name))).toContain('Erlaubt sind PDF');
    }
  });

  it('refuses an empty file and one over 10 MB', () => {
    expect(attachmentProblem(file('a.pdf', 0))).toContain('leer oder zu groß');
    expect(attachmentProblem(file('a.pdf', 10 * 1024 * 1024 + 1))).toContain('leer oder zu groß');
    expect(attachmentProblem(file('a.pdf', 10 * 1024 * 1024))).toBeNull();
  });

  it('opens PDF and images in a tab, but not Office documents', () => {
    expect(opensInTab(photo)).toBe(true);
    expect(opensInTab(attachment())).toBe(true);
    expect(opensInTab(word)).toBe(false);
  });

  it('picks an icon per kind of file', () => {
    expect(attachmentIcon(photo)).toBe('image');
    expect(attachmentIcon(attachment())).toBe('picture_as_pdf');
    expect(attachmentIcon(word)).toBe('description');
    expect(attachmentIcon({ isImage: false, contentType: 'application/vnd.ms-excel' })).toBe('table_chart');
    expect(attachmentIcon({ isImage: false, contentType: 'application/vnd.openxmlformats-officedocument.presentationml.presentation' })).toBe('slideshow');
  });
});
