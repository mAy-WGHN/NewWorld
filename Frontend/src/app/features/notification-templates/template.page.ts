import { Component, computed, inject, signal } from '@angular/core';
import { PAGE_UI } from '../../shared/ui/material';
import { problemTitle } from '../absences/absence-models';
import { formatDateTime, typeLabel } from '../notifications/notification-models';
import { TemplateApi } from './template.api';
import {
  CHANNELS,
  ChannelKey,
  Editable,
  NotificationTemplate,
  PartKey,
  TemplateTexts,
  TextPair,
  TemplateVariable,
  allowedVariables,
  draftOf,
  editable,
  insertAt,
  isChanged,
  isEditable,
  problemMessage,
  toSaveRequest,
} from './template-models';

type Draft = Record<ChannelKey, TextPair>;

/**
 * "Benachrichtigungstexte" for HR: the texts of every notification, per channel. The files are the defaults; a change only replaces the
 * texts, never the rules. Variables are written with a dollar sign and added with a click at the place of the caret. What is fixed
 * (push text and mail subject of sensitive notifications) is shown but cannot be changed. The preview shows the result without saving;
 * the service checks every text again when it is saved.
 */
@Component({
  selector: 'app-notification-templates',
  imports: [...PAGE_UI],
  templateUrl: './template.page.html',
})
export class TemplatePage {
  private readonly api = inject(TemplateApi);

  protected readonly channels = CHANNELS;
  protected readonly templates = signal<NotificationTemplate[]>([]);
  protected readonly loaded = signal(false);
  protected readonly listError = signal<string | null>(null);

  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly status = signal<string | null>(null);

  /** The template that is open for editing, its draft, where the caret is, and the last preview. */
  protected readonly editingKey = signal<string | null>(null);
  protected readonly draft = signal<Draft | null>(null);
  protected readonly focused = signal<{ channel: ChannelKey; part: PartKey } | null>(null);
  protected readonly preview = signal<TemplateTexts | null>(null);
  protected readonly confirmingReset = signal(false);

  protected readonly rows = computed(() =>
    [...this.templates()].sort((a, b) => typeLabel(a.key).localeCompare(typeLabel(b.key), 'de')),
  );
  protected readonly editing = computed(() => this.templates().find((t) => t.key === this.editingKey()) ?? null);
  protected readonly changed = computed(() => {
    const template = this.editing();
    const draft = this.draft();
    return template && draft ? isChanged(template, draft) : false;
  });
  /** The variables that fit the field with the caret (none without a field). */
  protected readonly offered = computed<TemplateVariable[]>(() => {
    const template = this.editing();
    const field = this.focused();
    return template && field && isEditable(editable(template), field.channel, field.part) ? allowedVariables(template, field.channel, field.part) : [];
  });

  protected readonly typeLabel = typeLabel;
  protected readonly formatDateTime = formatDateTime;

  constructor() {
    void this.load();
  }

  protected editableOf(template: NotificationTemplate): Editable {
    return editable(template);
  }

  protected canEdit(template: NotificationTemplate, channel: ChannelKey, part: PartKey): boolean {
    return isEditable(editable(template), channel, part);
  }

  protected textOf(channel: ChannelKey, part: PartKey): string {
    return this.draft()?.[channel][part] ?? '';
  }

  protected fieldId(channel: ChannelKey, part: PartKey): string {
    return `tpl-${channel}-${part}`;
  }

  protected async load(): Promise<void> {
    try {
      this.templates.set(await this.api.list());
      this.listError.set(null);
    } catch {
      this.listError.set('Die Benachrichtigungstexte konnten nicht geladen werden.');
    } finally {
      this.loaded.set(true);
    }
  }

  protected edit(template: NotificationTemplate): void {
    if (this.editingKey() === template.key) {
      this.close();
      return;
    }
    this.editingKey.set(template.key);
    this.draft.set(draftOf(template));
    this.focused.set(null);
    this.preview.set(null);
    this.confirmingReset.set(false);
    this.error.set(null);
    this.status.set(null);
  }

  protected close(): void {
    this.editingKey.set(null);
    this.draft.set(null);
    this.focused.set(null);
    this.preview.set(null);
    this.confirmingReset.set(false);
  }

  protected focus(channel: ChannelKey, part: PartKey): void {
    this.focused.set({ channel, part });
  }

  protected type(channel: ChannelKey, part: PartKey, event: Event): void {
    const value = (event.target as HTMLInputElement | HTMLTextAreaElement).value;
    this.draft.update((d) => (d ? { ...d, [channel]: { ...d[channel], [part]: value } } : d));
    this.preview.set(null);
  }

  /** Adds `$name` at the caret of the field that has it (or in place of the selection). */
  protected insert(variable: TemplateVariable): void {
    const field = this.focused();
    if (!field) {
      return;
    }
    const element = document.getElementById(this.fieldId(field.channel, field.part)) as HTMLInputElement | HTMLTextAreaElement | null;
    if (!element) {
      return;
    }
    const result = insertAt(element.value, element.selectionStart ?? element.value.length, element.selectionEnd ?? element.value.length, `$${variable.name}`);
    element.value = result.value;
    this.draft.update((d) => (d ? { ...d, [field.channel]: { ...d[field.channel], [field.part]: result.value } } : d));
    this.preview.set(null);
    element.focus();
    element.setSelectionRange(result.caret, result.caret);
  }

  protected async showPreview(): Promise<void> {
    const template = this.editing();
    const draft = this.draft();
    if (!template || !draft) {
      return;
    }
    await this.run(async () => {
      this.preview.set(await this.api.preview(template.key, toSaveRequest(template, draft)));
    }, null);
  }

  protected async save(): Promise<void> {
    const template = this.editing();
    const draft = this.draft();
    if (!template || !draft) {
      return;
    }
    await this.run(async () => {
      await this.api.save(template.key, toSaveRequest(template, draft));
      await this.load();
      this.close();
    }, 'Gespeichert. Die neuen Texte gelten ab sofort.');
  }

  protected askReset(): void {
    this.error.set(null);
    this.confirmingReset.set(true);
  }

  protected cancelReset(): void {
    this.confirmingReset.set(false);
  }

  protected async reset(): Promise<void> {
    const template = this.editing();
    if (!template) {
      return;
    }
    await this.run(async () => {
      await this.api.reset(template.key);
      await this.load();
      this.close();
    }, 'Zurückgesetzt. Es gelten wieder die Standardtexte.');
  }

  private async run(action: () => Promise<void>, done: string | null): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    this.status.set(null);
    try {
      await action();
      this.status.set(done);
    } catch (error) {
      this.error.set(problemMessage(problemTitle(error)));
    } finally {
      this.busy.set(false);
    }
  }
}
