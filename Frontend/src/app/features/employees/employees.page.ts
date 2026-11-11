import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, inject, signal } from '@angular/core';
import { AuthService } from '../../core/auth/auth.service';
import { PAGE_UI } from '../../shared/ui/material';
import { formatDate, problemTitle } from '../absences/absence-models';
import { formatDateTime } from '../notifications/notification-models';
import { LocationsApi } from '../report-issue/locations.api';
import { Location } from '../report-issue/report-issue-models';
import {
  ChangeRequest,
  CreatedEmployee,
  EmployeeCard,
  EmployeeForm,
  EmployeeSummary,
  GENDERS,
  LIST_FIELDS,
  ListKey,
  ReferenceEntry,
  TextKey,
  cardStateLabel,
  changedFields,
  emptyForm,
  employeeErrorMessage,
  isGuid,
  toRequest,
  validateForm,
} from './employee-models';
import { EmployeesApi } from './employees.api';

type TabId = 'requests' | 'create' | 'card';

interface TextField {
  key: TextKey;
  label: string;
  type: 'text' | 'date' | 'email' | 'tel' | 'number';
  required?: boolean;
  autocomplete?: string;
}

/** The form fields typed as text; the selection lists and the switches have their own blocks. */
export const TEXT_FIELDS: readonly TextField[] = [
  { key: 'firstName', label: 'Vorname', type: 'text', required: true, autocomplete: 'off' },
  { key: 'lastName', label: 'Nachname', type: 'text', required: true, autocomplete: 'off' },
  { key: 'receiptName', label: 'Name auf dem Kassenbon', type: 'text' },
  { key: 'birthDate', label: 'Geburtsdatum', type: 'date' },
  { key: 'street', label: 'Straße', type: 'text' },
  { key: 'postalCode', label: 'PLZ', type: 'text' },
  { key: 'city', label: 'Ort', type: 'text' },
  { key: 'privatePhone', label: 'Private Telefonnummer', type: 'tel' },
  { key: 'privateEmail', label: 'Private E-Mail', type: 'email' },
  { key: 'businessEmail', label: 'Geschäftliche E-Mail', type: 'email' },
  { key: 'hireDate', label: 'Eintritt', type: 'date', required: true },
  { key: 'exitDate', label: 'Austritt', type: 'date' },
  { key: 'supervisorPersonnelNumber', label: 'Personalnummer des Vorgesetzten', type: 'text' },
  { key: 'vacationDaysPerYear', label: 'Urlaubstage pro Jahr', type: 'number' },
  { key: 'note', label: 'Notiz', type: 'text' },
];

/**
 * "Mitarbeitende": what the service offers around the employee card. Tabs only show for the privileges the person has (the service
 * decides anyway): change requests to decide, a new person, and one card, found by name or personnel number (`GET /api/employees?query=`, only
 * people in the reach of the person; the ID of a created person comes back from the creation and is linked here).
 */
@Component({
  selector: 'app-employees',
  imports: [...PAGE_UI],
  templateUrl: './employees.page.html',
})
export class EmployeesPage {
  private readonly api = inject(EmployeesApi);
  private readonly locationsApi = inject(LocationsApi);
  private readonly auth = inject(AuthService);

  protected readonly canDecide = computed(() => this.auth.privileges().has('employee:decide-change'));
  protected readonly canCreate = computed(() => this.auth.privileges().has('employee:create'));
  protected readonly canRead = computed(() => this.auth.privileges().has('employee:read'));
  protected readonly canWageType = computed(() => this.auth.privileges().has('employee:read-wage-type'));

  protected readonly tabs = computed(() => {
    const tabs: { id: TabId; label: string }[] = [];
    if (this.canDecide()) {
      tabs.push({ id: 'requests', label: 'Änderungsanträge' });
    }
    if (this.canCreate()) {
      tabs.push({ id: 'create', label: 'Neue Person anlegen' });
    }
    if (this.canRead()) {
      tabs.push({ id: 'card', label: 'Karte ansehen' });
    }
    return tabs;
  });
  private readonly chosen = signal<TabId | null>(null);
  protected readonly active = computed<TabId | null>(() => {
    const tabs = this.tabs();
    return tabs.find((t) => t.id === this.chosen())?.id ?? tabs[0]?.id ?? null;
  });

  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly status = signal<string | null>(null);

  // ---- change requests
  protected readonly requests = signal<ChangeRequest[]>([]);
  protected readonly requestsLoaded = signal(false);
  protected readonly requestsError = signal<string | null>(null);
  protected readonly rejecting = signal<{ id: string; reason: string } | null>(null);

  // ---- new person
  protected readonly textFields = TEXT_FIELDS;
  protected readonly genders = GENDERS;
  protected readonly form = signal<EmployeeForm>(emptyForm());
  protected readonly fieldErrors = signal<Partial<Record<keyof EmployeeForm, string>>>({});
  protected readonly stores = signal<Location[]>([]);
  protected readonly lists = signal<Partial<Record<ListKey, ReferenceEntry[]>>>({});
  protected readonly formError = signal<string | null>(null);
  /** A person created here always starts "pending BC" (the answer of the service carries no state). */
  protected readonly created = signal<CreatedEmployee | null>(null);
  /** The lists the person may see: the wage type only with its privilege. */
  protected readonly listFields = computed(() => LIST_FIELDS.filter((f) => f.key !== 'wageTypeId' || this.canWageType()));

  // ---- card
  protected readonly searchText = signal('');
  /** The hits of the last search; `null` before the first search. */
  protected readonly results = signal<EmployeeSummary[] | null>(null);
  protected readonly cardId = signal('');
  protected readonly card = signal<EmployeeCard | null>(null);
  protected readonly cardError = signal<string | null>(null);

  protected readonly formatDate = formatDate;
  protected readonly formatDateTime = formatDateTime;
  protected readonly changedFields = changedFields;
  protected readonly cardStateLabel = cardStateLabel;

  constructor() {
    if (this.canDecide()) {
      void this.loadRequests();
    }
    if (this.canCreate()) {
      void this.loadFormData();
    }
  }

  protected select(tab: TabId): void {
    this.chosen.set(tab);
    this.error.set(null);
    this.status.set(null);
  }

  // ---------------------------------------------------------------- change requests

  protected async loadRequests(): Promise<void> {
    try {
      this.requests.set(await this.api.changeRequests());
      this.requestsError.set(null);
    } catch {
      this.requestsError.set('Die Änderungsanträge konnten nicht geladen werden.');
    } finally {
      this.requestsLoaded.set(true);
    }
  }

  protected approve(request: ChangeRequest): Promise<void> {
    return this.decide(() => this.api.approve(request.id), 'Antrag genehmigt. Die Angaben sind in der Karte übernommen.');
  }

  protected askReject(request: ChangeRequest): void {
    this.error.set(null);
    this.rejecting.set({ id: request.id, reason: '' });
  }

  protected setRejectReason(event: Event): void {
    const current = this.rejecting();
    if (current) {
      this.rejecting.set({ ...current, reason: (event.target as HTMLInputElement).value });
    }
  }

  protected cancelReject(): void {
    this.rejecting.set(null);
  }

  protected async reject(request: ChangeRequest): Promise<void> {
    const current = this.rejecting();
    if (!current || current.id !== request.id) {
      return;
    }
    await this.decide(() => this.api.reject(request.id, current.reason.trim() || null), 'Antrag abgelehnt. Die Person wird benachrichtigt.');
  }

  private async decide(change: () => Promise<unknown>, done: string): Promise<void> {
    await this.run(async () => {
      await change();
      this.rejecting.set(null);
      await this.loadRequests();
    }, done);
  }

  // ---------------------------------------------------------------- new person

  private async loadFormData(): Promise<void> {
    try {
      this.stores.set((await this.locationsApi.list()).filter((s) => !s.archived).sort((a, b) => a.name.localeCompare(b.name, 'de')));
    } catch {
      this.formError.set('Die Filialen konnten nicht geladen werden.');
    }
    const loaded: Partial<Record<ListKey, ReferenceEntry[]>> = {};
    let failed = false;
    await Promise.all(
      this.listFields().map(async (field) => {
        try {
          loaded[field.key] = (await this.api.list(field.kind)).filter((e) => e.isActive).sort((a, b) => a.name.localeCompare(b.name, 'de'));
        } catch {
          failed = true;
        }
      }),
    );
    this.lists.set(loaded);
    if (failed) {
      this.formError.set('Nicht alle Auswahllisten konnten geladen werden.');
    }
  }

  protected setText(key: TextKey, event: Event): void {
    this.form.update((f) => ({ ...f, [key]: (event.target as HTMLInputElement).value }));
  }

  protected setSelect(key: ListKey | 'gender' | 'mainStoreId', event: Event): void {
    this.form.update((f) => ({ ...f, [key]: (event.target as HTMLSelectElement).value }));
  }

  protected setFlag(key: 'qtradoExport' | 'webTimeTrackingAllowed' | 'passwordChangeRequired', event: Event): void {
    this.form.update((f) => ({ ...f, [key]: (event.target as HTMLInputElement).checked }));
  }

  protected async submit(): Promise<void> {
    const errors = validateForm(this.form());
    this.fieldErrors.set(errors);
    if (Object.keys(errors).length > 0) {
      this.error.set('Bitte die markierten Angaben prüfen.');
      return;
    }
    this.created.set(null);
    await this.run(async () => {
      const person = await this.api.create(toRequest(this.form(), this.canWageType()));
      this.created.set(person);
      this.form.set(emptyForm());
    }, null);
  }

  protected openCreated(): void {
    const created = this.created();
    if (created) {
      this.cardId.set(created.employeeId);
      this.select('card');
      void this.loadCard();
    }
  }

  // ---------------------------------------------------------------- card

  protected setSearchText(event: Event): void {
    this.searchText.set((event.target as HTMLInputElement).value);
  }

  /** Searches people in reach by name or personnel number (the service needs two characters at least and only returns people the person may see). */
  protected async search(): Promise<void> {
    this.card.set(null);
    this.results.set(null);
    const text = this.searchText().trim();
    if (text.replace(/\s+/g, '').length < 2) {
      this.cardError.set('Bitte mindestens zwei Zeichen vom Namen oder von der Personalnummer eingeben.');
      return;
    }
    this.busy.set(true);
    try {
      this.results.set(await this.api.search(text));
      this.cardError.set(null);
    } catch (e) {
      this.cardError.set(e instanceof HttpErrorResponse && e.status === 403 ? 'Das darfst du nicht.' : 'Die Suche ist fehlgeschlagen.');
    } finally {
      this.busy.set(false);
    }
  }

  protected async openPerson(person: EmployeeSummary): Promise<void> {
    this.cardId.set(person.employeeId);
    await this.loadCard();
  }

  protected async loadCard(): Promise<void> {
    this.card.set(null);
    if (!isGuid(this.cardId())) {
      this.cardError.set('Bitte eine Person aus der Suche auswählen.');
      return;
    }
    this.busy.set(true);
    try {
      this.card.set(await this.api.card(this.cardId().trim()));
      this.cardError.set(null);
    } catch (e) {
      this.cardError.set(e instanceof HttpErrorResponse && e.status === 404 ? 'Zu dieser Person gibt es keine Karte.' : e instanceof HttpErrorResponse && e.status === 403 ? 'Das darfst du nicht.' : 'Die Karte konnte nicht geladen werden.');
    } finally {
      this.busy.set(false);
    }
  }

  protected cardValue(card: EmployeeCard, key: ListKey): string | null {
    return key === 'wageTypeId' ? (card.wage?.wageTypeId ?? null) : card[key];
  }

  /** The name of a list entry of the card, if the lists are loaded (they are for those who may create). */
  protected entryName(key: ListKey, id: string | null): string {
    return id ? (this.lists()[key]?.find((e) => e.id === id)?.name ?? id) : '–';
  }

  private async run(action: () => Promise<void>, done: string | null): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    this.status.set(null);
    try {
      await action();
      this.status.set(done);
    } catch (e) {
      this.error.set(employeeErrorMessage(problemTitle(e)));
    } finally {
      this.busy.set(false);
    }
  }
}
