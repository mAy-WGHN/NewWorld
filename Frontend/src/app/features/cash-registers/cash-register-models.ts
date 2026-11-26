import { HttpErrorResponse } from '@angular/common/http';

/** One cash register of a store as the portal service lists it (`GET /api/cash-registers`). */
export interface CashRegister {
  id: string;
  storeId: string;
  storeNumber: string;
  storeName: string;
  name: string;
  sealed: boolean;
  /** When the state was set (the service takes the time). */
  stateChangedAt: string;
  lastRecordedAt: string;
  /** Initials of who recorded last ("AvS"). */
  recordedBy: string;
  recordedByName: string | null;
  /** The form of the current seal: its photo and who attached it (initials, name); none while not sealed. */
  sealPhotoId: string | null;
  sealAttachedBy: string | null;
  sealAttachedByName: string | null;
}

/** A person to choose from ("who attached the seal"). */
export interface PersonOption {
  id: string;
  name: string;
}

/** What the person fills in when a seal is set: the photo and who attached the seal. Date and time are taken by the service. */
export interface SealFormValue {
  photo: File | null;
  sealedBy: PersonOption | null;
}

export const EMPTY_SEAL_FORM: SealFormValue = { photo: null, sealedBy: null };

/** What goes to the service after the photo is uploaded. */
export interface SealPayload {
  photoId: string;
  sealedBy: string;
}

export const MAX_PHOTO_BYTES = 5 * 1024 * 1024;
const PHOTO_TYPES = ['image/png', 'image/jpeg', 'image/webp'];

/** What is missing or wrong in the form of a seal (German text), or `null` if it can be sent. The service checks again. */
export function sealFormProblem(value: SealFormValue): string | null {
  if (!value.photo) {
    return 'Bitte ein Foto des Siegels hinzufügen.';
  }
  if (!PHOTO_TYPES.includes(value.photo.type)) {
    return 'Das Foto muss ein PNG-, JPEG- oder WebP-Bild sein.';
  }
  if (value.photo.size > MAX_PHOTO_BYTES) {
    return 'Das Foto ist zu groß. Erlaubt sind höchstens 5 MB.';
  }
  if (!value.sealedBy) {
    return 'Bitte angeben, wer das Siegel angebracht hat.';
  }
  return null;
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

export type CashRegisterEventKind = 'recorded' | 'changed' | 'sealed' | 'unsealed' | 'deleted';

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
  /** For a seal: the photo and who attached it. */
  photoId: string | null;
  attachedBy: string | null;
  attachedByName: string | null;
}

/** The daily reminder for registers that are not sealed; `time` is the local time of day as `HH:mm`. */
export interface ReminderSettings {
  enabled: boolean;
  time: string;
}

/** True for `HH:mm` with a real hour and minute. */
export function isValidTime(value: string): boolean {
  return /^([01]\d|2[0-3]):[0-5]\d$/.test(value);
}


const KIND_LABELS: Record<CashRegisterEventKind, string> = {
  recorded: 'Erfasst',
  changed: 'Bearbeitet und neu erfasst',
  sealed: 'Siegel erneuert',
  unsealed: 'Siegel aufgehoben',
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
