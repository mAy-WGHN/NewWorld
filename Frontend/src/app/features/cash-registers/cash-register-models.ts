import { HttpErrorResponse } from '@angular/common/http';

/** One cash register of a store as the portal service lists it (`GET /api/cash-registers`). */
export interface CashRegister {
  id: string;
  storeId: string;
  storeNumber: string;
  storeName: string;
  name: string;
  sealed: boolean;
  sealedAt: string | null;
  lastRecordedAt: string;
  /** Initials of who recorded last ("AvS"). */
  recordedBy: string;
  recordedByName: string | null;
}

export interface CashRegisterStore {
  id: string;
  number: string;
  name: string;
}

export interface CashRegisterSummary {
  stores: number;
  registers: number;
  sealed: number;
  notSealed: number;
}

export interface CashRegisterList {
  summary: CashRegisterSummary;
  stores: CashRegisterStore[];
  items: CashRegister[];
}

export type CashRegisterEventKind = 'recorded' | 'changed' | 'sealed' | 'deleted';

export interface CashRegisterEvent {
  id: string;
  storeId: string;
  storeNumber: string;
  storeName: string;
  registerId: string;
  registerName: string;
  kind: CashRegisterEventKind;
  at: string;
  by: string;
  byName: string | null;
}

export type StatusFilter = 'all' | 'sealed' | 'notSealed';

export const STATUS_FILTERS: { value: StatusFilter; label: string }[] = [
  { value: 'all', label: 'Alle' },
  { value: 'sealed', label: 'Versiegelt' },
  { value: 'notSealed', label: 'Nicht versiegelt' },
];

const KIND_LABELS: Record<CashRegisterEventKind, string> = {
  recorded: 'Erfasst',
  changed: 'Bearbeitet und neu erfasst',
  sealed: 'Siegel erneuert',
  deleted: 'Gelöscht',
};

export function kindLabel(kind: string): string {
  return KIND_LABELS[kind as CashRegisterEventKind] ?? kind;
}

/** "26.11.26, 12:43" like the list shows it (local time of the browser). */
export function formatDateTime(iso: string): string {
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) {
    return iso;
  }
  return date.toLocaleString('de-DE', { day: '2-digit', month: '2-digit', year: '2-digit', hour: '2-digit', minute: '2-digit' });
}

/** The rows the filters keep: status, store (empty = all) and a search in store and register. */
export function filterRegisters(items: CashRegister[], status: StatusFilter, storeId: string, query: string): CashRegister[] {
  const needle = query.trim().toLocaleLowerCase('de');
  return items.filter(
    (r) =>
      (status === 'all' || (status === 'sealed') === r.sealed) &&
      (!storeId || r.storeId === storeId) &&
      (!needle || `${r.storeNumber} ${r.storeName} ${r.name}`.toLocaleLowerCase('de').includes(needle)),
  );
}

const GENERIC_ERROR = 'Es ist ein Fehler aufgetreten. Bitte versuche es noch einmal.';

/** The German text of a failed call: validation problems of the form, else the message of the service. */
export function cashRegisterError(error: unknown): string {
  if (error instanceof HttpErrorResponse && typeof error.error === 'object' && error.error !== null) {
    const body = error.error as { message?: unknown; errors?: { message?: unknown }[] };
    const messages = (body.errors ?? []).map((e) => e.message).filter((m): m is string => typeof m === 'string' && m.length > 0);
    if (messages.length > 0) {
      return [...new Set(messages)].join(' ');
    }
    if (typeof body.message === 'string' && body.message) {
      return body.message;
    }
  }
  return GENERIC_ERROR;
}
