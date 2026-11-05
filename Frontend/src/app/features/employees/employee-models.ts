import { GENERIC_ERROR, errorMessage } from '../absences/absence-models';

/** A change request as HR sees it (`GET /api/employee-change-requests`). Old values are not given, only the new ones. */
export interface ChangeFields {
  street: string | null;
  postalCode: string | null;
  city: string | null;
  privatePhone: string | null;
  privateEmail: string | null;
}

export interface ChangeRequest {
  id: string;
  employeeId: string;
  personnelNumber: string | null;
  personName: string | null;
  status: string;
  fields: ChangeFields;
  message: string | null;
  createdAt: string;
  decidedAt: string | null;
  rejectionReason: string | null;
}

const FIELD_LABELS: readonly { key: keyof ChangeFields; label: string }[] = [
  { key: 'street', label: 'Straße' },
  { key: 'postalCode', label: 'PLZ' },
  { key: 'city', label: 'Ort' },
  { key: 'privatePhone', label: 'Private Telefonnummer' },
  { key: 'privateEmail', label: 'Private E-Mail' },
];

/** The fields a request really changes (filled ones only), with plain labels. */
export function changedFields(fields: ChangeFields): { label: string; value: string }[] {
  return FIELD_LABELS.filter((f) => !!fields[f.key]?.trim()).map((f) => ({ label: f.label, value: fields[f.key]!.trim() }));
}

export interface ReferenceEntry {
  id: string;
  kind: string;
  code: string;
  name: string;
  isActive: boolean;
}

/** The selection lists of the card: kind string of the route and the label of the field. */
export type ListKey = 'functionId' | 'roleId' | 'regionId' | 'workplaceId' | 'employmentRelationshipId' | 'employmentCodeId' | 'authorizationGroupId' | 'wageTypeId';

export const LIST_FIELDS: readonly { key: ListKey; kind: string; label: string }[] = [
  { key: 'functionId', kind: 'function', label: 'Funktion' },
  { key: 'roleId', kind: 'role', label: 'Rolle' },
  { key: 'regionId', kind: 'region', label: 'Region' },
  { key: 'workplaceId', kind: 'workplace', label: 'Arbeitsort' },
  { key: 'employmentRelationshipId', kind: 'employment-relationship', label: 'Arbeitsverhältnis' },
  { key: 'employmentCodeId', kind: 'employment-code', label: 'Beschäftigungsverhältniscode' },
  { key: 'authorizationGroupId', kind: 'authorization-group', label: 'Berechtigungsgruppe' },
  { key: 'wageTypeId', kind: 'wage-type', label: 'Lohnart' },
];

export const GENDERS: readonly { value: string; label: string }[] = [
  { value: 'Female', label: 'Weiblich' },
  { value: 'Male', label: 'Männlich' },
  { value: 'Diverse', label: 'Divers' },
];

export type TextKey =
  | 'firstName' | 'lastName' | 'receiptName' | 'birthDate' | 'street' | 'postalCode' | 'city' | 'privatePhone' | 'privateEmail' | 'businessEmail'
  | 'hireDate' | 'exitDate' | 'supervisorPersonnelNumber' | 'vacationDaysPerYear' | 'note';

/** What the person typed: everything as text, like the fields. */
export interface EmployeeForm extends Record<TextKey, string>, Record<ListKey, string> {
  gender: string;
  mainStoreId: string;
  qtradoExport: boolean;
  webTimeTrackingAllowed: boolean;
  passwordChangeRequired: boolean;
}

export const emptyForm = (): EmployeeForm => ({
  firstName: '', lastName: '', receiptName: '', birthDate: '', street: '', postalCode: '', city: '', privatePhone: '', privateEmail: '', businessEmail: '',
  hireDate: '', exitDate: '', supervisorPersonnelNumber: '', vacationDaysPerYear: '', note: '',
  functionId: '', roleId: '', regionId: '', workplaceId: '', employmentRelationshipId: '', employmentCodeId: '', authorizationGroupId: '', wageTypeId: '',
  gender: '', mainStoreId: '', qtradoExport: false, webTimeTrackingAllowed: true, passwordChangeRequired: true,
});

/** The request of `POST /api/employees` (see `EmployeeRequest` in the service). */
export interface EmployeeRequest {
  firstName: string;
  lastName: string;
  receiptName: string | null;
  gender: string;
  birthDate: string | null;
  street: string | null;
  postalCode: string | null;
  city: string | null;
  privatePhone: string | null;
  privateEmail: string | null;
  businessEmail: string | null;
  mainStoreId: string | null;
  functionId: string | null;
  roleId: string | null;
  regionId: string | null;
  workplaceId: string | null;
  employmentRelationshipId: string | null;
  employmentCodeId: string | null;
  authorizationGroupId: string | null;
  wageTypeId: string | null;
  hireDate: string;
  exitDate: string | null;
  supervisorPersonnelNumber: string | null;
  qtradoExport: boolean;
  webTimeTrackingAllowed: boolean;
  passwordChangeRequired: boolean;
  note: string | null;
  vacationDaysPerYear: number | null;
}

export interface CreatedEmployee {
  employeeId: string;
  personnelNumber: string;
}

export interface EmployeeCard {
  employeeId: string;
  personnelNumber: string;
  firstName: string;
  lastName: string;
  receiptName: string;
  businessEmail: string | null;
  mainStoreId: string | null;
  functionId: string | null;
  roleId: string | null;
  regionId: string | null;
  workplaceId: string | null;
  employmentRelationshipId: string | null;
  employmentCodeId: string | null;
  authorizationGroupId: string | null;
  hireDate: string;
  exitDate: string | null;
  supervisorId: string | null;
  qtradoExport: boolean;
  webTimeTrackingAllowed: boolean;
  passwordChangeRequired: boolean;
  note: string | null;
  state: string;
  private: { gender: string; birthDate: string | null; street: string | null; postalCode: string | null; city: string | null; privatePhone: string | null; privateEmail: string | null } | null;
  wage: { wageTypeId: string | null } | null;
}

const orNull = (value: string): string | null => (value.trim() === '' ? null : value.trim());

/** Required fields first; returns German messages per field (empty object = fine). */
export function validateForm(form: EmployeeForm): Partial<Record<keyof EmployeeForm, string>> {
  const errors: Partial<Record<keyof EmployeeForm, string>> = {};
  if (!form.firstName.trim()) {
    errors.firstName = 'Bitte den Vornamen eintragen.';
  }
  if (!form.lastName.trim()) {
    errors.lastName = 'Bitte den Nachnamen eintragen.';
  }
  if (!form.gender) {
    errors.gender = 'Bitte das Geschlecht wählen.';
  }
  if (!form.hireDate) {
    errors.hireDate = 'Bitte das Eintrittsdatum eintragen.';
  }
  if (form.exitDate && form.hireDate && form.exitDate < form.hireDate) {
    errors.exitDate = 'Der Austritt liegt vor dem Eintritt.';
  }
  const email = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;
  if (form.privateEmail.trim() && !email.test(form.privateEmail.trim())) {
    errors.privateEmail = 'Bitte eine gültige E-Mail-Adresse eintragen.';
  }
  if (form.businessEmail.trim() && !email.test(form.businessEmail.trim())) {
    errors.businessEmail = 'Bitte eine gültige E-Mail-Adresse eintragen.';
  }
  if (form.vacationDaysPerYear.trim()) {
    const days = Number(form.vacationDaysPerYear.replace(',', '.'));
    if (!Number.isFinite(days) || days < 0 || days > 366) {
      errors.vacationDaysPerYear = 'Bitte eine Zahl zwischen 0 und 366 eintragen.';
    }
  }
  return errors;
}

/** Only filled fields are sent; empty ones become `null`. The wage type only with the privilege. */
export function toRequest(form: EmployeeForm, mayWageType: boolean): EmployeeRequest {
  return {
    firstName: form.firstName.trim(),
    lastName: form.lastName.trim(),
    receiptName: orNull(form.receiptName),
    gender: form.gender,
    birthDate: orNull(form.birthDate),
    street: orNull(form.street),
    postalCode: orNull(form.postalCode),
    city: orNull(form.city),
    privatePhone: orNull(form.privatePhone),
    privateEmail: orNull(form.privateEmail),
    businessEmail: orNull(form.businessEmail),
    mainStoreId: orNull(form.mainStoreId),
    functionId: orNull(form.functionId),
    roleId: orNull(form.roleId),
    regionId: orNull(form.regionId),
    workplaceId: orNull(form.workplaceId),
    employmentRelationshipId: orNull(form.employmentRelationshipId),
    employmentCodeId: orNull(form.employmentCodeId),
    authorizationGroupId: orNull(form.authorizationGroupId),
    wageTypeId: mayWageType ? orNull(form.wageTypeId) : null,
    hireDate: form.hireDate,
    exitDate: orNull(form.exitDate),
    supervisorPersonnelNumber: orNull(form.supervisorPersonnelNumber),
    qtradoExport: form.qtradoExport,
    webTimeTrackingAllowed: form.webTimeTrackingAllowed,
    passwordChangeRequired: form.passwordChangeRequired,
    note: orNull(form.note),
    vacationDaysPerYear: form.vacationDaysPerYear.trim() ? Number(form.vacationDaysPerYear.replace(',', '.')) : null,
  };
}

const EMPLOYEE_ERRORS: Record<string, string> = {
  Invalid: 'Eine Angabe fehlt oder ist nicht sinnvoll. Bitte die Eingaben prüfen.',
  NumberSeriesNotSet: 'Der Nummernkreis ist noch nicht eingerichtet. Die Personalabteilung muss zuerst die zuletzt vergebene Personalnummer eintragen.',
  NumberSeriesExhausted: 'Alle Personalnummern des Bereichs sind vergeben. Der Bereich muss erweitert werden.',
  DuplicateNumber: 'Diese Personalnummer gibt es schon.',
  UnknownReference: 'Ein gewählter Listeneintrag gibt es nicht mehr oder er ist deaktiviert. Bitte neu wählen.',
  UnknownStore: 'Die gewählte Filiale ist nicht bekannt.',
  UnknownSupervisor: 'Zu dieser Personalnummer des Vorgesetzten gibt es keine Person.',
  NotAllowed: 'Das darfst du nicht.',
  NotFound: 'Der Eintrag wurde nicht gefunden.',
  NotPending: 'Der Antrag wurde schon entschieden oder zurückgezogen.',
  NoHandler: 'Für diese Person ist niemand zuständig.',
};

/** Plain-language text for the error code of the service (own codes first, then the shared ones). */
export function employeeErrorMessage(title: string | undefined): string {
  if (title && EMPLOYEE_ERRORS[title]) {
    return EMPLOYEE_ERRORS[title];
  }
  const shared = errorMessage(title);
  return shared || GENERIC_ERROR;
}

export const isGuid = (value: string): boolean => /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(value.trim());

export const cardStateLabel = (state: string): string => (state === 'PendingBc' ? 'Wartet auf Übernahme durch Business Central' : state === 'Synced' ? 'In Business Central angelegt' : state);
