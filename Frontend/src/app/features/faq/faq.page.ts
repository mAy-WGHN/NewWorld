import { Component, computed, inject, signal } from '@angular/core';
import { MatExpansionModule } from '@angular/material/expansion';
import { AuthService } from '../../core/auth/auth.service';
import { PAGE_UI } from '../../shared/ui/material';
import { envelopeMessage } from '../reports/report-models';
import { FaqAnswer } from './faq-answer';
import { FaqFileDrop } from './faq-file-drop';
import { Faq, FaqImage, IMAGE_ACCEPT, filterFaqs, imageProblem, imageStatusLabel, move } from './faq-models';
import { FaqApi } from './faq.api';

type Mode = 'list' | 'archive' | 'images';

/** What the person is writing: a new entry (`id` null) or a change of an existing one. */
interface Draft {
  id: string | null;
  title: string;
  description: string;
}

/**
 * "FAQ": questions and answers for everybody, searchable. People with the privileges can also write, change, order and archive entries and
 * manage the images of the answers. The controls are only hidden without the privileges; the portal checks every call again.
 */
@Component({
  selector: 'app-faq',
  imports: [MatExpansionModule, FaqAnswer, FaqFileDrop, ...PAGE_UI],
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
    this.draft.set({ id: null, title: '', description: '' });
    this.error.set(null);
    this.status.set(null);
  }

  protected startEdit(faq: Faq): void {
    this.draft.set({ id: faq.id, title: faq.title, description: faq.description });
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

  protected setDescription(event: Event): void {
    const value = (event.target as HTMLTextAreaElement).value;
    this.draft.update((d) => d && { ...d, description: value });
  }

  protected async save(): Promise<void> {
    const draft = this.draft();
    if (!draft) {
      return;
    }
    if (!draft.title.trim() || !draft.description.trim()) {
      this.error.set('Bitte gib eine Frage und eine Antwort ein.');
      return;
    }
    await this.act(draft.id ? 'Eintrag gespeichert.' : 'Eintrag angelegt.', async () => {
      await (draft.id ? this.api.update(draft.id, { title: draft.title, description: draft.description }) : this.api.create(draft.title, draft.description));
      this.draft.set(null);
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
    this.draft.update((d) => d && { ...d, description: `${d.description}${d.description && !d.description.endsWith('\n') ? '\n\n' : ''}![Bild](${image.url})\n` });
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
