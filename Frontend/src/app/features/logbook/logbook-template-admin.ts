import { Component, computed, inject, signal } from '@angular/core';
import { LogbookApi } from './logbook.api';
import { EditSection, choiceKind, editSections, isRating, moved, newOption, newSection, sectionInputs } from './logbook-admin-models';
import { KIND_LABELS, SELECTABLE_KINDS, SectionKind, logbookError, stamp } from './logbook-models';
import { Action, LOGBOOK_UI } from './logbook-ui';

/**
 * The template of the weekly report: sections with a single choice, a multiple choice or a free text. Every change of the content is a new version;
 * reports already written keep the version they were written with. What keeps its id keeps the meaning of the answers already given, so renaming
 * an answer is safe and deleting one makes old answers show as "entfernte Antwort".
 */
@Component({
  selector: 'app-logbook-template-admin',
  imports: [...LOGBOOK_UI],
  templateUrl: './logbook-template-admin.html',
})
export class LogbookTemplateAdmin {
  private readonly api = inject(LogbookApi);

  protected readonly kinds = SELECTABLE_KINDS.map((value) => ({ value, label: KIND_LABELS[value] }));
  protected readonly sections = signal<EditSection[]>([]);
  protected readonly version = signal<number | null>(null);
  protected readonly publishedAt = signal<string | null>(null);
  protected readonly loadError = signal<string | null>(null);
  protected readonly save = new Action();
  protected readonly choiceKind = choiceKind;
  protected readonly isRating = isRating;
  protected readonly stamp = stamp;
  protected readonly loaded = computed(() => this.version() !== null);

  constructor() {
    void this.load();
  }

  private async load(): Promise<void> {
    try {
      const template = await this.api.currentTemplate();
      this.sections.set(editSections(template));
      this.version.set(template.version);
      this.publishedAt.set(template.publishedAt);
    } catch (error) {
      this.loadError.set(logbookError(error));
    }
  }

  protected value(event: Event): string {
    return (event.target as HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement).value;
  }

  private change(key: number, change: Partial<EditSection>): void {
    this.sections.update((all) => all.map((s) => (s.key === key ? { ...s, ...change } : s)));
    this.save.reset();
  }

  protected set(section: EditSection, field: 'title' | 'hint', event: Event): void {
    this.change(section.key, { [field]: this.value(event) });
  }

  protected setKind(section: EditSection, kind: string): void {
    if (isRating(section)) {
      return;
    }
    this.change(section.key, { kind: kind as SectionKind });
  }

  protected setRequired(section: EditSection, required: boolean): void {
    if (isRating(section)) {
      return;
    }
    this.change(section.key, { required });
  }

  protected addSection(): void {
    this.sections.update((all) => [...all, newSection()]);
    this.save.reset();
  }

  protected removeSection(section: EditSection): void {
    if (isRating(section)) {
      return; // the rating is always there
    }
    this.sections.update((all) => all.filter((s) => s.key !== section.key));
    this.save.reset();
  }

  protected moveSection(index: number, by: number): void {
    this.sections.update((all) => moved(all, index, by));
    this.save.reset();
  }

  protected addOption(section: EditSection): void {
    this.change(section.key, { options: [...section.options, newOption()] });
  }

  protected removeOption(section: EditSection, key: number): void {
    this.change(section.key, { options: section.options.filter((o) => o.key !== key) });
  }

  protected moveOption(section: EditSection, index: number, by: number): void {
    this.change(section.key, { options: moved(section.options, index, by) });
  }

  protected setOption(section: EditSection, key: number, event: Event): void {
    const label = this.value(event);
    this.change(section.key, { options: section.options.map((o) => (o.key === key ? { ...o, label } : o)) });
  }

  protected async submit(): Promise<void> {
    await this.save.run(async () => {
      const template = await this.api.saveTemplate(sectionInputs(this.sections()));
      this.sections.set(editSections(template));
      this.version.set(template.version);
      this.publishedAt.set(template.publishedAt);
    }, 'Die Vorlage ist gespeichert. Neue Berichte nutzen sie ab jetzt.');
  }
}
