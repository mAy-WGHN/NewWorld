import { Component, computed, inject, signal } from '@angular/core';
import { AuthService } from '../../core/auth/auth.service';
import { PAGE_UI } from '../../shared/ui/material';
import { formatDate, problemTitle } from '../absences/absence-models';
import { formatDateTime } from '../notifications/notification-models';
import {
  ChangeForm,
  ChangeRequest,
  EmployeeCard,
  MAX_MESSAGE,
  buildInput,
  formFromCard,
  genderLabel,
  requestErrorMessage,
  requestStatusLabel,
  requestedFields,
  validateInput,
} from './employee-card-models';
import { EmployeeCardApi } from './employee-card.api';

type TextField = keyof ChangeForm;

/**
 * "Meine Karte": the own employee card and the change requests. The person cannot change data themself; they ask and HR decides. Only
 * the changed fields are sent. The privileges only hide the form; the service checks every call.
 */
@Component({
  selector: 'app-employee-card',
  imports: [...PAGE_UI],
  templateUrl: './employee-card.page.html',
})
export class EmployeeCardPage {
  private readonly api = inject(EmployeeCardApi);
  private readonly auth = inject(AuthService);

  protected readonly canRequest = computed(() => this.auth.privileges().has('employee:request-change-own'));

  protected readonly loaded = signal(false);
  protected readonly card = signal<EmployeeCard | null>(null);
  protected readonly loadError = signal<string | null>(null);
  protected readonly requests = signal<ChangeRequest[]>([]);
  protected readonly requestsError = signal<string | null>(null);

  protected readonly form = signal<ChangeForm>(formFromCard(null));
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly status = signal<string | null>(null);

  protected readonly pending = computed(() => this.requests().find((r) => r.status === 'Pending') ?? null);
  protected readonly maxMessage = MAX_MESSAGE;

  protected readonly formatDate = formatDate;
  protected readonly formatDateTime = formatDateTime;
  protected readonly genderLabel = genderLabel;
  protected readonly statusLabel = requestStatusLabel;
  protected readonly requestedFields = requestedFields;

  constructor() {
    void this.load();
  }

  protected async load(): Promise<void> {
    try {
      this.card.set(await this.api.card());
      this.loadError.set(null);
    } catch {
      this.card.set(null);
      this.loadError.set('Deine Karte konnte nicht geladen werden.');
    }
    this.form.set(formFromCard(this.card()));
    if (this.canRequest() && this.card()) {
      try {
        this.requests.set(await this.api.requests());
        this.requestsError.set(null);
      } catch {
        this.requestsError.set('Deine Anträge konnten nicht geladen werden.');
      }
    }
    this.loaded.set(true);
  }

  protected set(field: TextField, event: Event): void {
    const value = (event.target as HTMLInputElement | HTMLTextAreaElement).value;
    this.form.update((f) => ({ ...f, [field]: value }));
  }

  protected async submit(): Promise<void> {
    const input = buildInput(this.form(), this.card());
    const invalid = validateInput(input);
    if (invalid) {
      this.status.set(null);
      this.error.set(invalid);
      return;
    }
    await this.run(() => this.api.request(input), 'Dein Antrag wurde gesendet. Die Personalabteilung entscheidet darüber.', 'Der Antrag konnte nicht gesendet werden.');
  }

  protected async withdraw(request: ChangeRequest): Promise<void> {
    await this.run(() => this.api.withdraw(request.id), 'Dein Antrag wurde zurückgezogen.', 'Der Antrag konnte nicht zurückgezogen werden.');
  }

  private async run(action: () => Promise<unknown>, done: string, failed: string): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    this.status.set(null);
    try {
      await action();
      await this.load();
      this.status.set(done);
    } catch (e) {
      this.error.set(requestErrorMessage(problemTitle(e), failed));
    } finally {
      this.busy.set(false);
    }
  }
}
