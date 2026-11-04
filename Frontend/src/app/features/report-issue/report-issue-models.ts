import { HttpErrorResponse } from '@angular/common/http';
import { GENERIC_ERROR } from '../absences/absence-models';

/** A store to choose in the report form (`GET /api/locations`). */
export interface Location {
  id: string;
  name: string;
  storeCode: string;
  archived: boolean;
}

export type CategoryId = 'bug' | 'virus' | 'theft' | 'other' | 'suggestion';
export type Severity = 'low' | 'medium' | 'high' | 'critical';

/** Which extra fields a kind of report has: none, the day and time it happened (theft), or the details of a virus. */
export type Extra = 'none' | 'noticed' | 'virus';

export interface IssueCategory {
  id: CategoryId;
  /** Name of the button and title of the form. */
  label: string;
  icon: string;
  /** The question above the description. */
  placeholder: string;
  /** `choose`: the person picks it (errors only); otherwise the severity is fixed by the kind. */
  severity: Severity | 'choose';
  extra: Extra;
}

/** The five kinds of the existing system ("Störung melden"), in its order. */
export const CATEGORIES: readonly IssueCategory[] = [
  { id: 'bug', label: 'Fehler melden', icon: 'bug_report', placeholder: 'Was funktioniert nicht?', severity: 'choose', extra: 'none' },
  { id: 'virus', label: 'Virus melden', icon: 'security', placeholder: 'Was ist aufgefallen?', severity: 'critical', extra: 'virus' },
  { id: 'theft', label: 'Diebstahl melden', icon: 'report', placeholder: 'Was ist passiert?', severity: 'high', extra: 'noticed' },
  { id: 'other', label: 'Sonstige Verstöße melden', icon: 'gavel', placeholder: 'Was ist passiert?', severity: 'medium', extra: 'none' },
  { id: 'suggestion', label: 'Verbesserungsvorschlag melden', icon: 'lightbulb', placeholder: 'Was schlägst du vor?', severity: 'low', extra: 'none' },
];

export const categoryById = (id: CategoryId): IssueCategory => CATEGORIES.find((c) => c.id === id)!;

export const SEVERITY_OPTIONS: readonly { value: Severity; label: string }[] = [
  { value: 'low', label: 'Niedrig' },
  { value: 'medium', label: 'Mittel' },
  { value: 'high', label: 'Hoch' },
  { value: 'critical', label: 'Kritisch' },
];

/** What the person typed (everything as text, like the fields). */
export interface IssueForm {
  personnelNumber: string;
  description: string;
  severity: string;
  device: string;
  noticedDate: string;
  noticedTime: string;
  noticedHow: string;
  priorActivity: string;
}

export interface CreateReportRequest {
  personnelNumber: number;
  category: CategoryId;
  severity: Severity;
  description: string;
  locationIds: string[];
  device?: string | null;
  noticedAt?: string | null;
  noticedHow?: string | null;
  priorActivity?: string | null;
}

/**
 * Day (`yyyy-MM-dd`) and time (`HH:mm`) as the local moment, given as a UTC time stamp, as the existing system sends it. Nothing without both;
 * a day alone is the start of that day, a time alone is that time today.
 */
export function combineDateAndTime(date: string, time: string, now: Date = new Date()): string | null {
  if (!date && !time) {
    return null;
  }
  const [year, month, day] = date ? date.split('-').map(Number) : [now.getFullYear(), now.getMonth() + 1, now.getDate()];
  const [hours, minutes] = time ? time.split(':').map(Number) : [0, 0];
  return new Date(year, month - 1, day, hours, minutes).toISOString();
}

export type IssueErrors = Partial<Record<'personnelNumber' | 'description' | 'locations', string>>;

/** The messages for what is missing, in the words of the existing system. Empty if the report is complete. */
export function validateIssue(form: IssueForm, locationIds: readonly string[]): IssueErrors {
  const errors: IssueErrors = {};
  if (!/^\d+$/.test(form.personnelNumber.trim()) || Number(form.personnelNumber) < 1) {
    errors.personnelNumber = 'Bitte deine Personalnummer angeben.';
  }
  if (!form.description.trim()) {
    errors.description = 'Bitte kurz beschreiben, worum es geht.';
  }
  if (locationIds.length === 0) {
    errors.locations = 'Bitte mindestens eine Filiale auswählen.';
  }
  return errors;
}

const orNull = (text: string): string | null => text.trim() || null;

/** The request for the portal. The severity is the fixed one of the kind unless the person may choose it (errors). */
export function buildRequest(category: IssueCategory, form: IssueForm, locationIds: string[]): CreateReportRequest {
  const request: CreateReportRequest = {
    personnelNumber: Number(form.personnelNumber),
    category: category.id,
    severity: category.severity === 'choose' ? (form.severity as Severity) : category.severity,
    description: form.description.trim(),
    locationIds,
  };
  if (category.extra === 'virus') {
    request.device = orNull(form.device);
    request.noticedAt = combineDateAndTime(form.noticedDate, form.noticedTime);
    request.noticedHow = orNull(form.noticedHow);
    request.priorActivity = orNull(form.priorActivity);
  } else if (category.extra === 'noticed') {
    request.noticedAt = combineDateAndTime(form.noticedDate, form.noticedTime);
  }
  return request;
}

export const locationLabel = (location: Location): string => `${location.name} (${location.storeCode})`;

const normalize = (text: string): string => text.normalize('NFD').replace(/\p{M}/gu, '').toLowerCase().trim();

/** The stores that fit the search (name or number, no accents or case), without those already chosen. */
export function filterLocations(all: readonly Location[], query: string, chosenIds: readonly string[]): Location[] {
  const q = normalize(query);
  return all.filter((l) => !chosenIds.includes(l.id) && (!q || normalize(l.name).includes(q) || normalize(l.storeCode).includes(q)));
}

/** What went wrong, for the person: the message of the portal and the messages of its fields. */
export function errorTexts(error: unknown): string {
  if (error instanceof HttpErrorResponse) {
    if (error.status === 429) {
      return 'Es wurden zu viele Meldungen in kurzer Zeit gesendet. Bitte kurz warten und noch einmal versuchen.';
    }
    if (typeof error.error === 'object' && error.error !== null) {
      const { message, errors } = error.error as { message?: unknown; errors?: { message?: unknown }[] };
      const fields = (errors ?? []).map((e) => e.message).filter((m): m is string => typeof m === 'string');
      const parts = [typeof message === 'string' ? message : '', ...fields].filter(Boolean);
      if (parts.length) {
        return parts.join(' ');
      }
    }
  }
  return GENERIC_ERROR;
}
