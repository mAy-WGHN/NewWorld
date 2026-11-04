import { HttpErrorResponse } from '@angular/common/http';
import { GENERIC_ERROR } from '../absences/absence-models';

export interface ReportLocation {
  id: string;
  name: string;
  storeCode: string;
  archived: boolean;
}

/** A report of the portal (`GET /api/reports`). Enums come in lower case; times are UTC without time zone (`yyyy-MM-ddTHH:mm:ss`). */
export interface Report {
  id: string;
  personnelNumber: number;
  status: string;
  source: string;
  category: string;
  description: string;
  severity: string;
  locations: ReportLocation[];
  device: string | null;
  noticedAt: string | null;
  noticedHow: string | null;
  priorActivity: string | null;
  comment: string | null;
  archived: boolean;
  parentReportId: string | null;
  childReportIds: string[];
  createdAt: string;
  updatedAt: string;
}

export interface ReportPage {
  items: Report[];
  meta: { total: number; offset: number; limit: number | null };
}

const CATEGORIES: Record<string, string> = { virus: 'Virus', bug: 'Fehler', theft: 'Diebstahl', other: 'Sonstiges', suggestion: 'Vorschlag' };
const SEVERITIES: Record<string, string> = { low: 'Niedrig', medium: 'Mittel', high: 'Hoch', critical: 'Kritisch' };
const STATUSES: Record<string, string> = { open: 'Offen', done: 'Erledigt' };

/** An unknown value is shown as it is, so something new of the service never disappears silently. */
export const categoryLabel = (category: string): string => CATEGORIES[category] ?? category;
export const severityLabel = (severity: string): string => SEVERITIES[severity] ?? severity;
export const statusLabel = (status: string): string => STATUSES[status] ?? status;

/** The day a report was filed: `2026-11-04T09:30:00` becomes `04.11.2026` (no Date object: no time zone can shift the day). */
export function formatCreated(iso: string): string {
  const [year, month, day] = iso.slice(0, 10).split('-');
  return `${day}.${month}.${year}`;
}

export const locationNames = (report: Report): string => (report.locations.length ? report.locations.map((l) => l.name).join(', ') : 'Keine Filiale');

/** The `message` of an error answer of the portal (`{success:false,message,errors[]}`). */
export function envelopeMessage(error: unknown): string {
  if (error instanceof HttpErrorResponse && typeof error.error === 'object' && error.error !== null) {
    const message = (error.error as { message?: unknown }).message;
    if (typeof message === 'string' && message) {
      return message;
    }
  }
  return GENERIC_ERROR;
}
