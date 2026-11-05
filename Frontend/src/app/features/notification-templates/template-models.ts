/** One text of a notification: title and body. */
export interface TextPair {
  title: string;
  body: string;
}

/** The texts per channel; a text that is `null` is the one of the file (default). */
export interface TemplateTexts {
  inApp: TextPair | null;
  push: TextPair | null;
  email: TextPair | null;
}

/** A variable a text may use, written with a dollar sign in front (`$USER.firstName`). Class 2 is sensitive. */
export interface TemplateVariable {
  name: string;
  class: 1 | 2;
  source: 'Recipient' | 'Sender';
}

/** A template as the maintenance sees it (`GET /api/notification-templates`). */
export interface NotificationTemplate {
  key: string;
  mandatory: boolean;
  /** The text itself is sensitive: push text and mail subject are fixed (and the mail text, unless `mailMayContainSensitive`). */
  sensitiveText: boolean;
  mailMayContainSensitive: boolean;
  customized: boolean;
  default: TemplateTexts;
  effective: TemplateTexts;
  variables: TemplateVariable[];
  changedAt: string | null;
  changedBy: string | null;
}

export type ChannelKey = 'inApp' | 'push' | 'email';
export type PartKey = 'title' | 'body';

export const CHANNELS: readonly { key: ChannelKey; label: string; hint: string }[] = [
  { key: 'inApp', label: 'Im Portal', hint: 'Die Mitteilung in der Liste und in der Snackbar.' },
  { key: 'push', label: 'Push', hint: 'Der Hinweis auf dem Gerät. Nur Variablen ohne sensible Daten.' },
  { key: 'email', label: 'E-Mail', hint: 'Betreff und Text der E-Mail.' },
];

/** What HR may change in a template; the service checks the same rules again with every save. */
export interface Editable {
  inAppTitle: boolean;
  inAppBody: boolean;
  pushTitle: boolean;
  pushBody: boolean;
  emailTitle: boolean;
  emailBody: boolean;
}

export function editable(template: Pick<NotificationTemplate, 'sensitiveText' | 'mailMayContainSensitive'>): Editable {
  if (!template.sensitiveText) {
    return { inAppTitle: true, inAppBody: true, pushTitle: true, pushBody: true, emailTitle: true, emailBody: true };
  }
  // A sensitive text: push and mail subject stay as they are; the mail text may change where the recipients may get it by mail.
  return { inAppTitle: true, inAppBody: true, pushTitle: false, pushBody: false, emailTitle: false, emailBody: template.mailMayContainSensitive };
}

export const isEditable = (e: Editable, channel: ChannelKey, part: PartKey): boolean =>
  e[`${channel}${part === 'title' ? 'Title' : 'Body'}` as keyof Editable];

/** Variables that may stand in this field: no sensitive ones in push texts and mail subjects, and in the mail text only where allowed. */
export function allowedVariables(template: NotificationTemplate, channel: ChannelKey, part: PartKey): TemplateVariable[] {
  return template.variables.filter((v) => {
    if (v.class === 1) {
      return true;
    }
    if (channel === 'inApp') {
      return true;
    }
    return channel === 'email' && part === 'body' && template.mailMayContainSensitive;
  });
}

/**
 * What goes to the service for a save: the changed texts. A fixed text is sent as `null` (the file's text); in a sensitive template whose mail text
 * may change, the mail subject is the fixed one of the file.
 */
export function toSaveRequest(template: NotificationTemplate, draft: Record<ChannelKey, TextPair>): TemplateTexts {
  const e = editable(template);
  return {
    inApp: draft.inApp,
    push: e.pushTitle || e.pushBody ? draft.push : null,
    email: e.emailTitle ? draft.email : e.emailBody ? { title: template.default.email!.title, body: draft.email.body } : null,
  };
}

/** The draft to start from: the texts in force now. */
export function draftOf(template: NotificationTemplate): Record<ChannelKey, TextPair> {
  const t = template.effective;
  return {
    inApp: { ...(t.inApp ?? template.default.inApp!) },
    push: { ...(t.push ?? template.default.push!) },
    email: { ...(t.email ?? template.default.email!) },
  };
}

/** Whether the draft differs from the texts in force (nothing to save otherwise). */
export function isChanged(template: NotificationTemplate, draft: Record<ChannelKey, TextPair>): boolean {
  const now = draftOf(template);
  return (['inApp', 'push', 'email'] as const).some((c) => now[c].title !== draft[c].title || now[c].body !== draft[c].body);
}

/** Inserts `text` at the caret (or in place of the selection) and returns the new value and where the caret goes. */
export function insertAt(value: string, start: number, end: number, text: string): { value: string; caret: number } {
  const from = Math.max(0, Math.min(start, value.length));
  const to = Math.max(from, Math.min(end, value.length));
  return { value: value.slice(0, from) + text + value.slice(to), caret: from + text.length };
}

const PROBLEMS: Record<string, string> = {
  EmptyText: 'Titel und Text dürfen nicht leer sein.',
  UnknownVariable: 'Eine Variable ist hier nicht bekannt. Bitte nur die Variablen aus der Liste verwenden.',
  SensitiveInPushOrSubject: 'Variablen mit sensiblen Daten dürfen nicht in Push-Texten und E-Mail-Betreffs stehen.',
  SensitiveInMail: 'Variablen mit sensiblen Daten dürfen in dieser E-Mail nicht stehen.',
  StrayBraces: 'Geschweifte Klammern sind in Push- und E-Mail-Texten nur bei Variablen erlaubt.',
  ReservedName: 'Dieser Name ist für die Empfängerin oder den Empfänger reserviert.',
  TooLong: 'Der Text ist zu lang (Titel höchstens 200 Zeichen, Text höchstens 2000).',
  SensitiveTextLocked: 'Push-Text und E-Mail-Betreff dieser Mitteilung sind fest vorgegeben.',
  UnknownTemplate: 'Diese Vorlage gibt es nicht (mehr).',
};

export const GENERIC_ERROR = 'Das hat nicht geklappt. Bitte erneut versuchen.';

export const problemMessage = (title: string | undefined): string => (title && PROBLEMS[title]) || GENERIC_ERROR;
