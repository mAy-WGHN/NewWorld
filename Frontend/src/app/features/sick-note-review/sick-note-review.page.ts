import { Component, computed, inject, signal } from '@angular/core';
import { AuthService } from '../../core/auth/auth.service';
import { PAGE_UI } from '../../shared/ui/material';
import { errorMessage, formatDate, formatPeriod, kindLabel, problemTitle } from '../absences/absence-models';
import { formatDateTime } from '../notifications/notification-models';
import {
  CertificateDocument,
  REJECTION_REASONS,
  STATUS_FILTERS,
  SickNoteOverview,
  certificateStatusLabel,
  formatSize,
  isOpenForReview,
  rejectionLabel,
} from './sick-note-review-models';
import { SickNoteReviewApi } from './sick-note-review.api';

const PAGE_SIZE = 25;
/** How long the object URL of an opened file lives (the new tab has loaded it long before). */
const OBJECT_URL_MS = 60_000;

/**
 * "Krankmeldungen und AU" for HR: every sick note with the state of its certificate. What waits for the check is marked yellow and
 * listed first by the default filter. A row opens to the uploaded documents: open the file, confirm it or turn it down with one of the
 * fixed reasons. Who may see which people is decided by the service (reach), the buttons only follow the privileges.
 */
@Component({
  selector: 'app-sick-note-review',
  imports: [...PAGE_UI],
  templateUrl: './sick-note-review.page.html',
})
export class SickNoteReviewPage {
  private readonly api = inject(SickNoteReviewApi);
  private readonly auth = inject(AuthService);

  protected readonly canReview = computed(() => this.auth.privileges().has('certificate:review'));
  protected readonly canDelete = computed(() => this.auth.privileges().has('certificate:delete'));

  protected readonly filters = STATUS_FILTERS;
  protected readonly reasons = REJECTION_REASONS;
  protected readonly filter = signal<string | null>('Submitted');

  protected readonly items = signal<SickNoteOverview[]>([]);
  protected readonly total = signal(0);
  protected readonly loaded = signal(false);
  protected readonly loadingMore = signal(false);
  protected readonly listError = signal<string | null>(null);
  private page = 1;

  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly status = signal<string | null>(null);

  /** The opened row, its documents (`null` while they load) and what is just being decided. */
  protected readonly expanded = signal<string | null>(null);
  protected readonly documents = signal<CertificateDocument[] | null>(null);
  protected readonly documentsError = signal<string | null>(null);
  protected readonly rejecting = signal<{ id: string; reason: string } | null>(null);
  protected readonly deleting = signal<string | null>(null);

  protected readonly hasMore = computed(() => this.items().length < this.total());

  protected readonly formatPeriod = formatPeriod;
  protected readonly kindLabel = kindLabel;
  protected readonly formatDate = formatDate;
  protected readonly formatDateTime = formatDateTime;
  protected readonly formatSize = formatSize;
  protected readonly statusLabel = certificateStatusLabel;
  protected readonly rejectionLabel = rejectionLabel;
  protected readonly isOpenForReview = isOpenForReview;

  constructor() {
    void this.load();
  }

  protected async setFilter(value: string | null): Promise<void> {
    this.filter.set(value);
    this.collapse();
    await this.load();
  }

  protected async load(): Promise<void> {
    this.page = 1;
    try {
      const result = await this.api.list(this.filter(), 1, PAGE_SIZE);
      this.items.set(result.items);
      this.total.set(result.total);
      this.listError.set(null);
    } catch {
      this.listError.set('Die Krankmeldungen konnten nicht geladen werden.');
    } finally {
      this.loaded.set(true);
    }
  }

  protected async more(): Promise<void> {
    this.loadingMore.set(true);
    try {
      const result = await this.api.list(this.filter(), this.page + 1, PAGE_SIZE);
      this.page += 1;
      this.items.update((list) => [...list, ...result.items]);
      this.total.set(result.total);
    } catch {
      this.listError.set('Die Krankmeldungen konnten nicht geladen werden.');
    } finally {
      this.loadingMore.set(false);
    }
  }

  protected async toggle(row: SickNoteOverview): Promise<void> {
    if (this.expanded() === row.absenceId) {
      this.collapse();
      return;
    }
    this.collapse();
    this.expanded.set(row.absenceId);
    await this.loadDocuments(row.absenceId);
  }

  private collapse(): void {
    this.expanded.set(null);
    this.documents.set(null);
    this.documentsError.set(null);
    this.rejecting.set(null);
    this.deleting.set(null);
  }

  private async loadDocuments(absenceId: string): Promise<void> {
    try {
      this.documents.set(await this.api.documents(absenceId));
      this.documentsError.set(null);
    } catch {
      this.documents.set([]);
      this.documentsError.set('Die Dokumente konnten nicht geladen werden.');
    }
  }

  /** Opens the file in a new tab. The browser gets it as a blob so the sign-in token stays out of the address. */
  protected async open(document: CertificateDocument): Promise<void> {
    await this.run(async () => {
      const blob = await this.api.content(document.absenceId, document.id);
      const url = URL.createObjectURL(blob);
      window.open(url, '_blank', 'noopener');
      setTimeout(() => URL.revokeObjectURL(url), OBJECT_URL_MS);
    }, null);
  }

  protected async verify(document: CertificateDocument): Promise<void> {
    await this.decide(document, () => this.api.verify(document.absenceId, document.id), 'AU bestätigt.');
  }

  protected askReject(document: CertificateDocument): void {
    this.error.set(null);
    this.rejecting.set({ id: document.id, reason: REJECTION_REASONS[0].value });
  }

  protected setReason(event: Event): void {
    const current = this.rejecting();
    if (current) {
      this.rejecting.set({ ...current, reason: (event.target as HTMLSelectElement).value });
    }
  }

  protected cancelReject(): void {
    this.rejecting.set(null);
  }

  protected async reject(document: CertificateDocument): Promise<void> {
    const current = this.rejecting();
    if (!current || current.id !== document.id) {
      return;
    }
    await this.decide(document, () => this.api.reject(document.absenceId, document.id, current.reason), 'AU abgelehnt. Die Person wird benachrichtigt.');
  }

  protected askDelete(document: CertificateDocument): void {
    this.error.set(null);
    this.deleting.set(document.id);
  }

  protected cancelDelete(): void {
    this.deleting.set(null);
  }

  protected async remove(document: CertificateDocument): Promise<void> {
    await this.decide(document, () => this.api.remove(document.absenceId, document.id), 'Dokument gelöscht.');
  }

  /** Runs a change, then loads the documents and the list again. */
  private async decide(document: CertificateDocument, change: () => Promise<unknown>, done: string): Promise<void> {
    await this.run(async () => {
      await change();
      this.rejecting.set(null);
      this.deleting.set(null);
      await this.loadDocuments(document.absenceId);
      await this.load();
    }, done);
  }

  private async run(action: () => Promise<void>, done: string | null): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    this.status.set(null);
    try {
      await action();
      this.status.set(done);
    } catch (e) {
      this.error.set(errorMessage(problemTitle(e)));
    } finally {
      this.busy.set(false);
    }
  }
}
