import { HttpErrorResponse } from '@angular/common/http';
import { todayIso } from '../../core/dates';
import { envelopeMessage } from '../reports/report-models';

// ---------------------------------------------------------------- template

export type SectionKind = 'singleChoice' | 'multiChoice' | 'freeText';

export interface LogbookOption {
  id: string;
  label: string;
}

export interface LogbookSection {
  id: string;
  title: string;
  hint: string | null;
  kind: SectionKind;
  required: boolean;
  options: LogbookOption[];
}

export interface LogbookTemplate {
  version: number;
  publishedAt: string | null;
  sections: LogbookSection[];
}

export const KIND_LABELS: Record<SectionKind, string> = {
  singleChoice: 'Eine Antwort wählen',
  multiChoice: 'Mehrere Antworten wählen',
  freeText: 'Freitext',
};

// ---------------------------------------------------------------- reports

export type Phase = 'draft' | 'submitted';
export type ReportState = 'new' | 'inProgress' | 'followedUp' | 'done';
export type ViewerLevel = 'none' | 'regional' | 'district';

export interface LogbookAnswer {
  optionIds: string[] | null;
  text: string | null;
}

export interface LogbookReport {
  id: string;
  storeId: string;
  week: string;
  templateVersion: number;
  phase: Phase;
  state: ReportState;
  answers: Record<string, LogbookAnswer>;
  submittedAt: string | null;
  submittedByName: string | null;
  revisedAt: string | null;
  revisedByName: string | null;
}

/** A store of the person with the report of the week (`GET /api/logbook/my-reports`). */
export interface StoreWeek {
  storeId: string;
  storeNumber: string;
  storeName: string;
  templateVersion: number;
  canSubmit: boolean;
  report: LogbookReport | null;
}

export interface MyReports {
  week: string;
  stores: StoreWeek[];
}

export interface ReportSummary {
  id: string;
  week: string;
  phase: Phase;
  state: ReportState;
  submittedAt: string | null;
  revisedAt: string | null;
}

export interface ReviewItem {
  id: string;
  storeId: string;
  storeNumber: string;
  storeName: string;
  week: string;
  state: ReportState;
  submittedAt: string | null;
  revisedAt: string | null;
  followedUpAt: string | null;
  revisedSinceMyReply: boolean;
  commentCount: number;
  escalated: boolean;
}

export interface LogbookComment {
  id: string;
  kind: 'reply' | 'internal';
  level: string;
  authorId: string;
  authorName: string;
  text: string;
  createdAt: string;
  own: boolean;
}

export interface ReportDetail extends LogbookReport {
  storeNumber: string;
  storeName: string;
  followedUpAt: string | null;
  followedUpByName: string | null;
  followUpNote: string | null;
  escalatedAt: string | null;
  escalatedByName: string | null;
  escalationNote: string | null;
  revisedSinceMyReply: boolean;
  viewerLevel: ViewerLevel;
  comments: LogbookComment[];
}

export interface ReviewFilter {
  week?: string;
  open?: boolean;
  state?: ReportState;
}

export interface MissingStore {
  storeId: string;
  storeNumber: string;
  storeName: string;
  remindersSent: number;
  lastReminderAt: string | null;
  hasStoreManagers: boolean;
  reportStarted: boolean;
}

export interface Missing {
  week: string;
  stores: MissingStore[];
}

// ---------------------------------------------------------------- hierarchy and settings

export interface Leader {
  id: string;
  name: string;
}

export interface DistrictDto {
  id: string;
  name: string;
  leaders: Leader[];
}

export interface RegionDto {
  id: string;
  districtId: string;
  name: string;
  leaders: Leader[];
}

export interface StoreAssignment {
  storeId: string;
  storeNumber: string;
  storeName: string;
  districtId: string;
  regionId: string | null;
}

export interface Structure {
  districts: DistrictDto[];
  regions: RegionDto[];
  stores: StoreAssignment[];
}

/** A store of the portal (`GET /api/locations`). */
export interface StoreOption {
  id: string;
  name: string;
  storeCode: string;
  archived: boolean;
}

export interface ReminderPoint {
  daysAfterDue: number;
  time: string;
}

export interface ReminderSettings {
  enabled: boolean;
  enabledAt: string | null;
  dueDay: string;
  dueTime: string;
  points: ReminderPoint[];
  maxReminders: number;
}

export const WEEKDAYS: { value: string; label: string }[] = [
  { value: 'monday', label: 'Montag' },
  { value: 'tuesday', label: 'Dienstag' },
  { value: 'wednesday', label: 'Mittwoch' },
  { value: 'thursday', label: 'Donnerstag' },
  { value: 'friday', label: 'Freitag' },
  { value: 'saturday', label: 'Samstag' },
  { value: 'sunday', label: 'Sonntag' },
];

// ---------------------------------------------------------------- labels

export const STATE_LABELS: Record<ReportState, string> = {
  new: 'Neu',
  inProgress: 'In Bearbeitung',
  followedUp: 'Nachbereitet',
  done: 'Erledigt',
};

export const stateLabel = (state: ReportState): string => STATE_LABELS[state] ?? state;

export const phaseLabel = (phase: Phase): string => (phase === 'submitted' ? 'Abgegeben' : 'Entwurf');

/** Newest things first: followed up (the store manager did something), then new, in progress, done. */
const STATE_ORDER: Record<ReportState, number> = { followedUp: 0, new: 1, inProgress: 2, done: 3 };

export const stateOrder = (state: ReportState): number => STATE_ORDER[state] ?? 9;

// ---------------------------------------------------------------- weeks

function parse(iso: string): Date {
  const [y, m, d] = iso.split('-').map(Number);
  return new Date(y, m - 1, d);
}

function format(date: Date): string {
  return todayIso(date);
}

/** The Monday of the week of a day (`yyyy-MM-dd`), the way the service names a week. */
export function mondayOf(iso: string): string {
  const date = parse(iso);
  const back = (date.getDay() + 6) % 7;
  return format(new Date(date.getFullYear(), date.getMonth(), date.getDate() - back));
}

export function currentWeek(now: Date = new Date()): string {
  return mondayOf(todayIso(now));
}

export function shiftWeek(iso: string, weeks: number): string {
  const date = parse(iso);
  return format(new Date(date.getFullYear(), date.getMonth(), date.getDate() + 7 * weeks));
}

/** The calendar week number (ISO 8601) of the week that starts on this Monday. */
export function weekNumber(iso: string): number {
  const date = parse(iso);
  const thursday = new Date(date.getFullYear(), date.getMonth(), date.getDate() + 3);
  const firstThursday = new Date(thursday.getFullYear(), 0, 4);
  const start = new Date(firstThursday.getFullYear(), 0, 4 - ((firstThursday.getDay() + 6) % 7) + 3);
  return 1 + Math.round((thursday.getTime() - start.getTime()) / (7 * 24 * 3600 * 1000));
}

const dm = (date: Date): string => `${String(date.getDate()).padStart(2, '0')}.${String(date.getMonth() + 1).padStart(2, '0')}.`;

/** "KW 43 · 19.10. bis 25.10.2026". */
export function weekLabel(iso: string): string {
  const monday = parse(iso);
  const sunday = new Date(monday.getFullYear(), monday.getMonth(), monday.getDate() + 6);
  return `KW ${weekNumber(iso)} · ${dm(monday)} bis ${dm(sunday)}${sunday.getFullYear()}`;
}

/** A date and time as people read it in Germany; empty for nothing. */
export function stamp(value: string | null): string {
  if (!value) {
    return '';
  }
  const date = new Date(value);
  const p = (n: number): string => String(n).padStart(2, '0');
  return `${p(date.getDate())}.${p(date.getMonth() + 1)}.${date.getFullYear()}, ${p(date.getHours())}:${p(date.getMinutes())} Uhr`;
}

// ---------------------------------------------------------------- answers

export interface AnswerDraft {
  optionIds: string[];
  text: string;
}

export type AnswerDrafts = Record<string, AnswerDraft>;

/** What the form shows: the saved answers, empty where nothing was answered. */
export function draftsFrom(template: LogbookTemplate, answers: Record<string, LogbookAnswer> | undefined): AnswerDrafts {
  const result: AnswerDrafts = {};
  for (const section of template.sections) {
    const saved = answers?.[section.id];
    result[section.id] = { optionIds: [...(saved?.optionIds ?? [])], text: saved?.text ?? '' };
  }
  return result;
}

function hasContent(section: LogbookSection, draft: AnswerDraft | undefined): boolean {
  if (!draft) {
    return false;
  }
  return section.kind === 'freeText' ? draft.text.trim().length > 0 : draft.optionIds.length > 0;
}

/** The request body: only the sections that were answered (the service checks the rest). */
export function toRequest(template: LogbookTemplate, drafts: AnswerDrafts): Record<string, LogbookAnswer> {
  const answers: Record<string, LogbookAnswer> = {};
  for (const section of template.sections) {
    const draft = drafts[section.id];
    if (!hasContent(section, draft)) {
      continue;
    }
    answers[section.id] = section.kind === 'freeText' ? { optionIds: [], text: draft.text.trim() } : { optionIds: [...draft.optionIds], text: null };
  }
  return answers;
}

/** The required sections that are still empty (handing in is only possible without any). */
export function missingRequired(template: LogbookTemplate, drafts: AnswerDrafts): LogbookSection[] {
  return template.sections.filter((s) => s.required && !hasContent(s, drafts[s.id]));
}

export interface ShownAnswer {
  section: LogbookSection;
  labels: string[];
  text: string | null;
}

/** The answers of a report in the order of the template, for reading. */
export function shownAnswers(template: LogbookTemplate, answers: Record<string, LogbookAnswer>): ShownAnswer[] {
  return template.sections.map((section) => {
    const answer = answers[section.id];
    const labels = (answer?.optionIds ?? []).map((id) => section.options.find((o) => o.id === id)?.label ?? '(entfernte Antwort)');
    return { section, labels, text: answer?.text?.trim() || null };
  });
}

// ---------------------------------------------------------------- errors

/** The problem as the person should read it: the messages of the fields for a validation error, otherwise the message of the service. */
export function logbookError(error: unknown): string {
  if (error instanceof HttpErrorResponse && error.status === 422 && typeof error.error === 'object' && error.error !== null) {
    const errors = (error.error as { errors?: { message?: unknown }[] }).errors;
    const messages = (errors ?? []).map((e) => e.message).filter((m): m is string => typeof m === 'string' && m.length > 0);
    if (messages.length > 0) {
      return [...new Set(messages)].join(' ');
    }
  }
  return envelopeMessage(error);
}

/** A new id for something the person adds in the administration (the service takes ids from the client). */
export function newId(): string {
  return crypto.randomUUID();
}
