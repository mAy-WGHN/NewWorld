import { Component, computed, inject, signal, viewChild } from '@angular/core';
import { MatExpansionModule } from '@angular/material/expansion';
import { AuthService } from '../../core/auth/auth.service';
import { PAGE_UI } from '../../shared/ui/material';
import { envelopeMessage } from '../reports/report-models';
import { formatSize } from '../sick-note-review/sick-note-review-models';
import { FaqContentView } from './faq-content-view';
import { FaqEditor } from './faq-editor';
import { FaqAttachments } from './faq-attachments';
import { FaqFileDrop } from './faq-file-drop';
import { ATTACHMENT_ACCEPT, Faq, FaqAttachment, FaqDoc, FaqImage, IMAGE_ACCEPT, MAX_ATTACHMENTS, attachmentProblem, docHasContent, emptyDoc, filterFaqs, imageProblem, imageStatusLabel, move } from './faq-models';
import { FaqApi } from './faq.api';

type Mode = 'list' | 'archive' | 'images';

/** What the person is writing: a new entry (`id` null) or a change of an existing one. */
interface Draft {
  id: string | null;
  title: string;
  /** The answer as the editor gave it back last (the start document of the editor). */
  content: FaqDoc;
  /** The files of the entry that are attached already (a change). */
  attachments: FaqAttachment[];
  /** Files chosen for a new entry; they are attached when it is saved. */
  pending: File[];
}

/**
 * "FAQ": questions and answers for everybody, searchable. People with the privileges can also write, change, order and archive entries and
 * manage the images of the answers. The controls are only hidden without the privileges; the portal checks every call again.
 */
@Component({
  selector: 'app-faq',
  imports: [MatExpansionModule, FaqContentView, FaqEditor, FaqAttachments, FaqFileDrop, ...PAGE_UI],
  templateUrl: './faq.page.html',
})
export class FaqPage {
  private readonly api = inject(FaqApi);
  private readonly auth = inject(AuthService);

  protected readonly canRead = computed(() => this.auth.privileges().has('faq:read'));
  protected readonly canCreate = computed(() => this.auth.privileges().has('faq:create'));
  protected readonly canUpdate = computed(() => this.auth.privileges().has('faq:update'));
  protected readonly canDelete = computed(() => this.auth.privileges().has('faq:delete'));

  protected readonly mode = signal<Mode>('list');
  protected readonly faqs = signal<Faq[]>([]);
  protected readonly archived = signal<Faq[]>([]);
  protected readonly images = signal<FaqImage[]>([]);
  protected readonly loaded = signal(false);
  protected readonly listError = signal<string | null>(null);

  protected readonly search = signal('');
  protected readonly visible = computed(() => filterFaqs(this.faqs(), this.search()));

  protected readonly draft = signal<Draft | null>(null);
  protected readonly confirmDelete = signal<string | null>(null);
  protected readonly confirmImageDelete = signal<string | null>(null);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly status = signal<string | null>(null);
  private readonly editor = viewChild(FaqEditor);

  protected readonly confirmAttachmentDelete = signal<string | null>(null);
  private attachQueue: Promise<void> = Promise.resolve();

  protected readonly attachmentAccept = ATTACHMENT_ACCEPT;
  protected readonly maxAttachments = MAX_ATTACHMENTS;
  protected readonly formatSize = formatSize;
  protected readonly imageAccept = IMAGE_ACCEPT;
  protected readonly imageStatusLabel = imageStatusLabel;

  constructor() {
    void this.load();
  }

  protected setSearch(event: Event): void {
    this.search.set((event.target as HTMLInputElement).value);
  }

  protected async show(mode: Mode): Promise<void> {
    this.mode.set(this.mode() === mode ? 'list' : mode);
    this.error.set(null);
    this.status.set(null);
    this.confirmDelete.set(null);
    this.confirmImageDelete.set(null);
    await this.load();
  }

  // --- writing ---

  protected startCreate(): void {
    this.mode.set('list');
    this.draft.set({ id: null, title: '', content: emptyDoc(), attachments: [], pending: [] });
    this.error.set(null);
    this.status.set(null);
  }

  protected startEdit(faq: Faq): void {
    this.draft.set({ id: faq.id, title: faq.title, content: faq.content ?? emptyDoc(), attachments: [...(faq.attachments ?? [])], pending: [] });
    this.confirmAttachmentDelete.set(null);
    this.error.set(null);
    this.status.set(null);
  }

  protected cancelEdit(): void {
    this.draft.set(null);
  }

  protected setTitle(event: Event): void {
    const value = (event.target as HTMLInputElement).value;
    this.draft.update((d) => d && { ...d, title: value });
  }

  protected setContent(content: FaqDoc): void {
    this.draft.update((d) => d && { ...d, content });
  }

  protected async save(): Promise<void> {
    const draft = this.draft();
    if (!draft) {
      return;
    }
    if (!draft.title.trim() || !docHasContent(draft.content)) {
      this.error.set('Bitte gib eine Frage und eine Antwort ein.');
      return;
    }
    let failures: string[] = [];
    await this.act(draft.id ? 'Eintrag gespeichert.' : 'Eintrag angelegt.', async () => {
      if (draft.id) {
        await this.api.update(draft.id, { title: draft.title, content: draft.content });
      } else {
        const created = await this.api.create(draft.title, draft.content);
        failures = await this.attachPending(created.id, draft.pending);
      }
      this.draft.set(null);
    });
    if (failures.length > 0) {
      this.error.set(`Der Eintrag ist angelegt, aber diese Dateien konnten nicht angeheftet werden: ${failures.join('; ')}. Hefte sie über "Ändern" an.`);
    }
  }

  // --- attachments ---

  /** Attaches the files that were chosen for a new entry, one after the other; says which ones did not work. */
  private async attachPending(faqId: string, files: File[]): Promise<string[]> {
    const failures: string[] = [];
    for (const file of files) {
      try {
        await this.api.uploadAttachment(faqId, file);
      } catch (error) {
        failures.push(`${file.name} (${envelopeMessage(error)})`);
      }
    }
    return failures;
  }

  /** A file for the entry in the editor: attached at once to an existing entry, collected for a new one (attached when it is saved). */
  protected attach(file: File): Promise<void> {
    this.attachQueue = this.attachQueue.then(() => this.attachOne(file));
    return this.attachQueue;
  }

  private async attachOne(file: File): Promise<void> {
    const draft = this.draft();
    if (!draft) {
      return;
    }
    const problem = attachmentProblem(file);
    if (problem) {
      this.error.set(`${file.name}: ${problem}`);
      this.status.set(null);
      return;
    }
    if (draft.attachments.length + draft.pending.length >= MAX_ATTACHMENTS) {
      this.error.set(`An einen Eintrag können höchstens ${MAX_ATTACHMENTS} Dateien angeheftet werden.`);
      this.status.set(null);
      return;
    }
    this.error.set(null);
    if (!draft.id) {
      this.draft.update((d) => d && { ...d, pending: [...d.pending, file] });
      return;
    }
    this.busy.set(true);
    try {
      const attachment = await this.api.uploadAttachment(draft.id, file);
      this.draft.update((d) => (d && d.id === draft.id ? { ...d, attachments: [...d.attachments, attachment] } : d));
      this.status.set(`${file.name} angeheftet.`);
      void this.load();
    } catch (error) {
      this.error.set(`${file.name}: ${envelopeMessage(error)}`);
      this.status.set(null);
    } finally {
      this.busy.set(false);
    }
  }

  protected removePending(index: number): void {
    this.draft.update((d) => d && { ...d, pending: d.pending.filter((_, i) => i !== index) });
  }

  /** Taking a file away needs a second click. */
  protected async removeAttachment(attachment: FaqAttachment): Promise<void> {
    const draft = this.draft();
    if (!draft?.id) {
      return;
    }
    if (this.confirmAttachmentDelete() !== attachment.id) {
      this.confirmAttachmentDelete.set(attachment.id);
      return;
    }
    this.confirmAttachmentDelete.set(null);
    await this.act('Datei entfernt.', async () => {
      await this.api.deleteAttachment(attachment, draft.id!);
      this.draft.update((d) => d && { ...d, attachments: d.attachments.filter((a) => a.id !== attachment.id) });
    });
  }

  protected async archive(faq: Faq): Promise<void> {
    await this.act('Eintrag archiviert.', () => this.api.update(faq.id, { archived: true }));
  }

  protected async restore(faq: Faq): Promise<void> {
    await this.act('Eintrag wiederhergestellt.', () => this.api.update(faq.id, { archived: false }));
  }

  /** Deleting an entry for good needs a second click ("Wirklich löschen"). */
  protected async remove(faq: Faq): Promise<void> {
    if (this.confirmDelete() !== faq.id) {
      this.confirmDelete.set(faq.id);
      return;
    }
    this.confirmDelete.set(null);
    await this.act('Eintrag gelöscht.', () => this.api.remove(faq.id));
  }

  protected async moveEntry(index: number, delta: number): Promise<void> {
    const moved = move(this.faqs(), index, delta);
    if (moved === this.faqs()) {
      return;
    }
    await this.act('Reihenfolge gespeichert.', async () => this.faqs.set(await this.api.reorder(moved.map((f) => f.id))));
  }

  // --- images ---

  protected async upload(file: File): Promise<void> {
    const problem = imageProblem(file);
    if (problem) {
      this.error.set(problem);
      this.status.set(null);
      return;
    }
    await this.act('Bild hochgeladen.', () => this.api.uploadImage(file));
  }

  protected insertImage(image: FaqImage): void {
    const editor = this.editor();
    if (!editor) {
      return;
    }
    editor.insertImage(image.url, 'Bild');
    this.status.set('Bild in die Antwort eingefügt.');
  }

  protected async deleteImage(image: FaqImage): Promise<void> {
    if (this.confirmImageDelete() !== image.id) {
      this.confirmImageDelete.set(image.id);
      return;
    }
    this.confirmImageDelete.set(null);
    await this.act('Bild gelöscht.', () => this.api.deleteImage(image.id));
  }

  // --- plumbing ---

  /** Runs a change, then reloads; shows what the portal answered if it refuses. */
  private async act(success: string, action: () => Promise<unknown>): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    this.status.set(null);
    try {
      await action();
      this.status.set(success);
      await this.load();
    } catch (error) {
      this.error.set(envelopeMessage(error));
    } finally {
      this.busy.set(false);
    }
  }

  private async load(): Promise<void> {
    try {
      const mode = this.mode();
      if (mode === 'archive') {
        this.archived.set(await this.api.listArchived());
      } else if (mode === 'images') {
        this.images.set(await this.api.listImages());
      } else {
        this.faqs.set(await this.api.list());
      }
      this.listError.set(null);
    } catch {
      this.listError.set('Die FAQ konnte nicht geladen werden.');
    } finally {
      this.loaded.set(true);
    }
  }
}
