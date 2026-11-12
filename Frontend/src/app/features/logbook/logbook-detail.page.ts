import { Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { LogbookApi } from './logbook.api';
import { LogbookComment, LogbookTemplate, ReportDetail, logbookError, phaseLabel, shownAnswers, stamp, stateLabel, weekLabel } from './logbook-models';
import { Action, LOGBOOK_UI } from './logbook-ui';

const LEVEL_LABELS: Record<string, string> = { regional: 'Regionalleitung', district: 'Bezirksleitung', none: 'Filialleitung' };

/**
 * One handed-in report (`/logbook/reports/:id`). Everybody sees the answers; what else they see is decided by the service: the store manager only the
 * replies of the regional leader, a regional leader all replies and their own internal comments, a district leader everything. Leaders answer, comment
 * internally, set the state and (regional leaders) escalate; the store manager's follow-up is on the page "Berichtsheft".
 */
@Component({
  selector: 'app-logbook-detail',
  imports: [RouterLink, ...LOGBOOK_UI],
  templateUrl: './logbook-detail.page.html',
})
export class LogbookDetailPage {
  private readonly api = inject(LogbookApi);
  private readonly id = inject(ActivatedRoute).snapshot.paramMap.get('id') ?? '';

  protected readonly report = signal<ReportDetail | null>(null);
  protected readonly template = signal<LogbookTemplate | null>(null);
  protected readonly loadError = signal<string | null>(null);

  protected readonly reply = signal('');
  protected readonly internal = signal('');
  protected readonly escalateNote = signal('');
  protected readonly escalating = signal(false);

  protected readonly act = new Action();

  protected readonly isLeader = computed(() => (this.report()?.viewerLevel ?? 'none') !== 'none');
  protected readonly canEscalate = computed(() => this.report()?.viewerLevel === 'regional' && !this.report()?.escalatedAt);
  protected readonly answers = computed(() => {
    const r = this.report();
    const t = this.template();
    return r && t ? shownAnswers(t, r.answers) : [];
  });
  protected readonly replies = computed(() => (this.report()?.comments ?? []).filter((c) => c.kind === 'reply'));
  protected readonly internals = computed(() => (this.report()?.comments ?? []).filter((c) => c.kind === 'internal'));

  protected readonly phaseLabel = phaseLabel;
  protected readonly stateLabel = stateLabel;
  protected readonly weekLabel = weekLabel;
  protected readonly stamp = stamp;

  constructor() {
    void this.load();
  }

  protected levelLabel(comment: LogbookComment): string {
    return LEVEL_LABELS[comment.level] ?? comment.level;
  }

  private async load(): Promise<void> {
    this.loadError.set(null);
    try {
      const report = await this.api.detail(this.id);
      if (this.template()?.version !== report.templateVersion) {
        this.template.set(await this.api.template(report.templateVersion));
      }
      this.report.set(report);
    } catch (error) {
      this.loadError.set(logbookError(error));
    }
  }

  protected write(target: 'reply' | 'internal' | 'escalate', event: Event): void {
    const value = (event.target as HTMLTextAreaElement).value;
    (target === 'reply' ? this.reply : target === 'internal' ? this.internal : this.escalateNote).set(value);
  }

  protected async send(kind: 'reply' | 'internal'): Promise<void> {
    const text = (kind === 'reply' ? this.reply() : this.internal()).trim();
    const ok = await this.act.run(() => this.api.comment(this.id, kind, text), kind === 'reply' ? 'Die Antwort ist gesendet.' : 'Der interne Kommentar ist gespeichert.');
    if (ok) {
      (kind === 'reply' ? this.reply : this.internal).set('');
      await this.load();
    }
  }

  protected async setState(state: 'inProgress' | 'done'): Promise<void> {
    const ok = await this.act.run(() => this.api.setState(this.id, state), state === 'done' ? 'Der Bericht ist als erledigt markiert.' : 'Der Bericht ist wieder in Bearbeitung.');
    if (ok) {
      await this.load();
    }
  }

  protected async escalate(): Promise<void> {
    const note = this.escalateNote().trim();
    const ok = await this.act.run(() => this.api.escalate(this.id, note || null), 'Die Bezirksleitung ist informiert.');
    if (ok) {
      this.escalating.set(false);
      this.escalateNote.set('');
      await this.load();
    }
  }
}
