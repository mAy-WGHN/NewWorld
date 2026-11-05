/** The private part of the own card. Present for the own card (`GET /api/me/employee-card`). */
export interface EmployeePrivate {
  /** `Female`, `Male`, `Diverse` or `Unknown`. */
  gender: string;
  birthDate: string | null;
  street: string | null;
  postalCode: string | null;
  city: string | null;
  privatePhone: string | null;
  privateEmail: string | null;
}

/**
 * The own employee card as far as the person may see it. The service also sends ids of selection lists (function, role, ...); the person
 * cannot resolve them, so they are not modelled here. The wage type is never sent for the own card.
 */
export interface EmployeeCard {
  employeeId: string;
  personnelNumber: string;
  firstName: string;
  lastName: string;
  receiptName: string;
  businessEmail: string | null;
  hireDate: string;
  exitDate: string | null;
  private: EmployeePrivate | null;
}

export type ChangeRequestStatus = 'Pending' | 'Approved' | 'Rejected' | 'Withdrawn' | string;

export interface ChangeFields {
  street: string | null;
  postalCode: string | null;
  city: string | null;
  privatePhone: string | null;
  privateEmail: string | null;
}

/** One change request of the person (`GET /api/me/employee-card/change-requests`). */
export interface ChangeRequest {
  id: string;
  employeeId: string;
  status: ChangeRequestStatus;
  fields: ChangeFields;
  message: string | null;
  createdAt: string;
  decidedAt: string | null;
  rejectionReason: string | null;
}

/** What is sent: only the changed fields, plus the free message. */
export interface ChangeRequestInput {
  street?: string;
  postalCode?: string;
  city?: string;
  privatePhone?: string;
  privateEmail?: string;
  message?: string;
}

/** The values in the form (all text, empty = nothing). */
export interface ChangeForm {
  street: string;
  postalCode: string;
  city: string;
  privatePhone: string;
  privateEmail: string;
  message: string;
}

export const MAX_MESSAGE = 500;

const FIELD_KEYS = ['street', 'postalCode', 'city', 'privatePhone', 'privateEmail'] as const;
type FieldKey = (typeof FIELD_KEYS)[number];

const STATUSES: Record<string, string> = {
  Pending: 'Wartet auf die Personalabteilung',
  Approved: 'Übernommen',
  Rejected: 'Abgelehnt',
  Withdrawn: 'Zurückgezogen',
};

export const requestStatusLabel = (status: string): string => STATUSES[status] ?? status;

const GENDERS: Record<string, string> = { Female: 'Weiblich', Male: 'Männlich', Diverse: 'Divers' };

export const genderLabel = (gender: string | null | undefined): string => (gender && GENDERS[gender]) || '';

export const FIELD_LABELS: Record<FieldKey, string> = {
  street: 'Straße',
  postalCode: 'PLZ',
  city: 'Ort',
  privatePhone: 'Private Telefonnummer',
  privateEmail: 'Private E-Mail',
};

/** The form prefilled with the current values. */
export function formFromCard(card: EmployeeCard | null): ChangeForm {
  const p = card?.private;
  return {
    street: p?.street ?? '',
    postalCode: p?.postalCode ?? '',
    city: p?.city ?? '',
    privatePhone: p?.privatePhone ?? '',
    privateEmail: p?.privateEmail ?? '',
    message: '',
  };
}

/** Only what differs from the card (and is not empty) goes into the request. */
export function buildInput(form: ChangeForm, card: EmployeeCard | null): ChangeRequestInput {
  const current = formFromCard(card);
  const input: ChangeRequestInput = {};
  for (const key of FIELD_KEYS) {
    const value = form[key].trim();
    if (value !== '' && value !== current[key].trim()) {
      input[key] = value;
    }
  }
  const message = form.message.trim();
  if (message !== '') {
    input.message = message;
  }
  return input;
}

const EMAIL = /^[^@\s]+@[^@\s]+\.[^@\s]+$/;
const PHONE = /^[0-9 +\-/().]+$/;

/** Same limits as the service; returns a plain-language reason or `null`. The service checks again. */
export function validateInput(input: ChangeRequestInput): string | null {
  if (Object.keys(input).length === 0) {
    return 'Bitte ändere mindestens ein Feld oder schreibe eine Mitteilung.';
  }
  if ((input.street?.length ?? 0) > 200) {
    return 'Die Straße ist zu lang.';
  }
  if ((input.postalCode?.length ?? 0) > 10) {
    return 'Die PLZ ist zu lang.';
  }
  if ((input.city?.length ?? 0) > 100) {
    return 'Der Ort ist zu lang.';
  }
  if (input.privatePhone !== undefined && (!PHONE.test(input.privatePhone) || (input.privatePhone.match(/\d/g)?.length ?? 0) < 5 || input.privatePhone.length > 30)) {
    return 'Bitte gib eine gültige Telefonnummer an.';
  }
  if (input.privateEmail !== undefined && (!EMAIL.test(input.privateEmail) || input.privateEmail.length > 200)) {
    return 'Bitte gib eine gültige E-Mail-Adresse an.';
  }
  if ((input.message?.length ?? 0) > MAX_MESSAGE) {
    return `Die Mitteilung darf höchstens ${MAX_MESSAGE} Zeichen lang sein.`;
  }
  return null;
}

/** The fields a request asks to change, as "Label: value" pairs. */
export function requestedFields(fields: ChangeFields): { label: string; value: string }[] {
  return FIELD_KEYS.filter((key) => !!fields[key]).map((key) => ({ label: FIELD_LABELS[key], value: fields[key]! }));
}

const REQUEST_ERRORS: Record<string, string> = {
  PendingRequestExists: 'Du hast schon einen offenen Antrag. Ziehe ihn zurück oder warte auf die Entscheidung der Personalabteilung.',
  Invalid: 'Bitte prüfe deine Angaben: Es muss sich etwas ändern oder eine Mitteilung dabei sein, E-Mail und Telefonnummer müssen gültig sein.',
  NoHandler: 'Es ist niemand in der Personalabteilung dafür hinterlegt. Bitte wende dich direkt an die Personalabteilung.',
  NotPending: 'Der Antrag ist schon entschieden oder zurückgezogen.',
  NotFound: 'Der Antrag wurde nicht gefunden.',
  NotAllowed: 'Das darfst du nicht.',
};

/** Plain-language text for the error title of the service; `fallback` is used for unknown titles. */
export const requestErrorMessage = (title: string | undefined, fallback: string): string => (title && REQUEST_ERRORS[title]) || fallback;
