import { NgTemplateOutlet } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { AuthService } from '../../core/auth/auth.service';
import { Report, categoryLabel, envelopeMessage, formatCreated, locationNames, severityLabel, statusLabel } from './report-models';
import { ReportsApi } from './reports.api';

type Kind = 'open' | 'done' | 'archive';

/** A list that is loaded page by page. */
interface Bucket {
  items: Report[];
  total: number;
}

const EMPTY: Bucket = { items: [], total: 0 };

/**
 * "Meldungen": the reports within the reach of the person. Open reports (still to handle) come first, apart and marked yellow;
 * "Archiv" at the top right switches to the archived ones.
 */
@Component({
  selector: 'app-reports',
  imports: [NgTemplateOutlet, RouterLink],
  templateUrl: './reports.page.html',
  styleUrl: './reports.page.scss',
})
export class ReportsPage {
  private readonly api = inject(ReportsApi);
  private readonly auth = inject(AuthService);

  // What the person may do; the service checks again with every call.
  protected readonly canUpdate = computed(() => this.auth.privileges().has('report:update'));
  protected readonly canDelete = computed(() => this.auth.privileges().has('report:delete'));

  protected readonly archiveMode = signal(false);
  protected readonly open = signal<Bucket>(EMPTY);
  protected readonly done = signal<Bucket>(EMPTY);
  protected readonly archived = signal<Bucket>(EMPTY);
  protected readonly loaded = signal(false);
  protected readonly listError = signal<string | null>(null);

  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly status = signal<string | null>(null);

  /** What the person typed into a comment field but did not save yet, by report. */
  protected readonly drafts = signal<Record<string, string>>({});

  // Plain functions for the template.
  protected readonly categoryLabel = categoryLabel;
  protected readonly severityLabel = severityLabel;
  protected readonly statusLabel = statusLabel;
  protected readonly formatCreated = formatCreated;
  protected readonly locationNames = locationNames;

  constructor() {
    void this.load();
  }

  protected draft(report: Report): string {
    return this.drafts()[report.id] ?? report.comment ?? '';
  }

  protected setDraft(report: Report, event: Event): void {
    const value = (event.target as HTMLTextAreaElement).value;
    this.drafts.update((d) => ({ ...d, [report.id]: value }));
  }

  protected async toggleArchive(): Promise<void> {
    this.archiveMode.update((on) => !on);
    this.error.set(null);
    this.status.set(null);
    await this.load();
  }

  protected async markDone(report: Report): Promise<void> {
    await this.act('Meldung erledigt.', () => this.api.setStatus(report.id, 'done'));
  }

  protected async reopen(report: Report): Promise<void> {
    await this.act('Meldung wieder geöffnet.', () => this.api.setStatus(report.id, 'open'));
  }

  protected async archive(report: Report): Promise<void> {
    await this.act('Meldung archiviert.', () => this.api.archive(report.id));
  }

  protected async restore(report: Report): Promise<void> {
    await this.act('Meldung wiederhergestellt.', () => this.api.restore(report.id));
  }

  protected async saveComment(report: Report): Promise<void> {
    await this.act('Kommentar gespeichert.', () => this.api.saveComment(report.id, this.draft(report)), () =>
      this.drafts.update(({ [report.id]: _saved, ...rest }) => rest),
    );
  }

  protected async more(kind: Kind): Promise<void> {
    const bucket = this.bucket(kind);
    try {
      const page = await this.fetch(kind, bucket().items.length);
      bucket.set({ items: [...bucket().items, ...page.items], total: page.meta.total });
    } catch (error) {
      this.error.set(envelopeMessage(error));
    }
  }

  /** Runs a change, then reloads the lists from the start; shows what the portal answered if it refuses. */
  private async act(success: string, action: () => Promise<unknown>, afterSuccess?: () => void): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    this.status.set(null);
    try {
      await action();
      afterSuccess?.();
      this.status.set(success);
      await this.load();
    } catch (error) {
      this.error.set(envelopeMessage(error));
    } finally {
      this.busy.set(false);
    }
  }

  private bucket(kind: Kind) {
    return kind === 'open' ? this.open : kind === 'done' ? this.done : this.archived;
  }

  private fetch(kind: Kind, offset: number) {
    return kind === 'archive' ? this.api.listArchived(offset) : this.api.list(kind, offset);
  }

  private async load(): Promise<void> {
    try {
      if (this.archiveMode()) {
        const page = await this.api.listArchived(0);
        this.archived.set({ items: page.items, total: page.meta.total });
      } else {
        const [open, done] = await Promise.all([this.api.list('open', 0), this.api.list('done', 0)]);
        this.open.set({ items: open.items, total: open.meta.total });
        this.done.set({ items: done.items, total: done.meta.total });
      }
      this.listError.set(null);
    } catch {
      this.listError.set('Die Meldungen konnten nicht geladen werden.');
    } finally {
      this.loaded.set(true);
    }
  }
}
