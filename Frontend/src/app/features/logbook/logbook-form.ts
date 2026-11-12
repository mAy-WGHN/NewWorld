import { Component, computed, effect, inject, input, output, signal, untracked } from '@angular/core';
import { RouterLink } from '@angular/router';
import { LogbookApi } from './logbook.api';
import {
  AnswerDrafts,
  LogbookTemplate,
  StoreWeek,
  draftsFrom,
  missingRequired,
  phaseLabel,
  stamp,
  stateLabel,
  toRequest,
} from './logbook-models';
import { Action, LOGBOOK_UI } from './logbook-ui';

/**
 * The weekly report of one store (decision 49): the sections of the template as radio buttons, checkboxes or a text field. A draft is saved as often as
 * wanted and handed in when all required sections are answered; a handed-in report can still be changed and is marked "nachbereitet" with a note once
 * the store manager has dealt with what the leaders answered.
 */
@Component({
  selector: 'app-logbook-form',
  imports: [RouterLink, ...LOGBOOK_UI],
  templateUrl: './logbook-form.html',
})
export class LogbookForm {
  private readonly api = inject(LogbookApi);

  readonly store = input.required<StoreWeek>();
  readonly week = input.required<string>();
  readonly template = input.required<LogbookTemplate>();
  /** The report changed on the service (saved, handed in, followed up): the page loads it again. */
  readonly changed = output<void>();

  protected readonly drafts = signal<AnswerDrafts>({});
  protected readonly followNote = signal('');
  protected readonly save = new Action();
  protected readonly follow = new Action();

  protected readonly report = computed(() => this.store().report);
  protected readonly submitted = computed(() => this.report()?.phase === 'submitted');
  protected readonly missing = computed(() => missingRequired(this.template(), this.drafts()));
  protected readonly dirty = computed(() => {
    const now = JSON.stringify(toRequest(this.template(), this.drafts()));
    return now !== JSON.stringify(toRequest(this.template(), draftsFrom(this.template(), this.report()?.answers)));
  });
  /** Handing in: everything required is answered and the service allows it (the store has somebody to read it). */
  protected readonly canSubmit = computed(() => this.store().canSubmit && this.missing().length === 0);

  protected readonly phaseLabel = phaseLabel;
  protected readonly stateLabel = stateLabel;
  protected readonly stamp = stamp;

  constructor() {
    // A new week, store or saved report starts the form from what is saved.
    effect(() => {
      const template = this.template();
      const report = this.report();
      untracked(() => {
        this.drafts.set(draftsFrom(template, report?.answers));
        this.followNote.set('');
      });
    });
  }

  protected chosen(sectionId: string): string | null {
    return this.drafts()[sectionId]?.optionIds[0] ?? null;
  }

  protected isChecked(sectionId: string, optionId: string): boolean {
    return this.drafts()[sectionId]?.optionIds.includes(optionId) ?? false;
  }

  protected text(sectionId: string): string {
    return this.drafts()[sectionId]?.text ?? '';
  }

  protected choose(sectionId: string, optionId: string): void {
    this.patch(sectionId, { optionIds: [optionId] });
  }

  protected toggle(sectionId: string, optionId: string, on: boolean): void {
    const current = this.drafts()[sectionId]?.optionIds ?? [];
    this.patch(sectionId, { optionIds: on ? [...new Set([...current, optionId])] : current.filter((id) => id !== optionId) });
  }

  protected write(sectionId: string, event: Event): void {
    this.patch(sectionId, { text: (event.target as HTMLTextAreaElement).value });
  }

  protected writeNote(event: Event): void {
    this.followNote.set((event.target as HTMLTextAreaElement).value);
  }

  private patch(sectionId: string, change: Partial<AnswerDrafts[string]>): void {
    this.drafts.update((d) => ({ ...d, [sectionId]: { ...(d[sectionId] ?? { optionIds: [], text: '' }), ...change } }));
    this.save.reset();
  }

  protected async saveDraft(): Promise<void> {
    const ok = await this.save.run(() => this.persist(), this.submitted() ? 'Die Änderungen sind gespeichert.' : 'Der Entwurf ist gespeichert.');
    if (ok) {
      this.changed.emit();
    }
  }

  protected async hand(): Promise<void> {
    const ok = await this.save.run(async () => {
      await this.persist();
      await this.api.submit(this.store().storeId, this.week());
    }, 'Der Bericht ist abgegeben.');
    if (ok) {
      this.changed.emit();
    }
  }

  private persist(): Promise<unknown> {
    return this.api.save(this.store().storeId, this.week(), toRequest(this.template(), this.drafts()));
  }

  protected async followUp(): Promise<void> {
    const report = this.report();
    if (!report) {
      return;
    }
    const ok = await this.follow.run(() => this.api.followUp(report.id, this.followNote().trim()), 'Der Bericht ist als nachbereitet markiert.');
    if (ok) {
      this.changed.emit();
    }
  }
}
