import { Component, computed, inject, signal } from '@angular/core';
import { ProfileService } from '../../core/profile.service';
import { IconComponent } from '../../shared/icon';
import { ReportsApi } from '../reports/reports.api';
import { LocationsApi } from './locations.api';
import {
  CATEGORIES,
  IssueCategory,
  IssueErrors,
  IssueForm,
  Location,
  SEVERITY_OPTIONS,
  buildRequest,
  errorTexts,
  validateIssue,
} from './report-issue-models';
import { StorePicker } from './store-picker';

type Step = 'choose' | 'form' | 'done';

const emptyForm = (personnelNumber: string): IssueForm => ({
  personnelNumber,
  description: '',
  severity: 'medium',
  device: '',
  noticedDate: '',
  noticedTime: '',
  noticedHow: '',
  priorActivity: '',
});

/**
 * "Störung melden", like in the existing system: choose what to report (error, virus, theft, other violation, suggestion), then a short form
 * with the fields of that kind. The severity is fixed by the kind, except for an error where the person chooses it.
 */
@Component({
  selector: 'app-report-issue',
  imports: [IconComponent, StorePicker],
  templateUrl: './report-issue.page.html',
  styleUrl: './report-issue.page.scss',
})
export class ReportIssuePage {
  private readonly reports = inject(ReportsApi);
  private readonly locationsApi = inject(LocationsApi);
  private readonly profile = inject(ProfileService);

  protected readonly categories = CATEGORIES;
  protected readonly severities = SEVERITY_OPTIONS;

  protected readonly step = signal<Step>('choose');
  protected readonly category = signal<IssueCategory>(CATEGORIES[0]);
  protected readonly form = signal<IssueForm>(emptyForm(''));
  protected readonly chosen = signal<Location[]>([]);

  protected readonly locations = signal<Location[]>([]);
  protected readonly locationsError = signal<string | null>(null);

  protected readonly errors = signal<IssueErrors>({});
  protected readonly submitError = signal<string | null>(null);
  protected readonly busy = signal(false);

  protected readonly chosenIds = computed(() => this.chosen().map((l) => l.id));

  constructor() {
    void this.loadLocations();
  }

  protected start(category: IssueCategory): void {
    this.category.set(category);
    this.form.set(emptyForm(this.profile.personnelNumber() ?? ''));
    this.chosen.set([]);
    this.errors.set({});
    this.submitError.set(null);
    this.step.set('form');
  }

  protected backToChoice(): void {
    this.errors.set({});
    this.submitError.set(null);
    this.step.set('choose');
  }

  protected set(field: keyof IssueForm, event: Event): void {
    const value = (event.target as HTMLInputElement | HTMLTextAreaElement | HTMLSelectElement).value;
    this.form.update((f) => ({ ...f, [field]: value }));
  }

  protected async submit(event: Event): Promise<void> {
    event.preventDefault();
    if (this.busy() || this.step() !== 'form') {
      return;
    }
    const errors = validateIssue(this.form(), this.chosenIds());
    this.errors.set(errors);
    if (Object.keys(errors).length > 0) {
      return;
    }
    this.busy.set(true);
    this.submitError.set(null);
    try {
      await this.reports.create(buildRequest(this.category(), this.form(), this.chosenIds()));
      this.step.set('done');
    } catch (error) {
      this.submitError.set(errorTexts(error));
    } finally {
      this.busy.set(false);
    }
  }

  private async loadLocations(): Promise<void> {
    try {
      this.locations.set(await this.locationsApi.list());
      this.locationsError.set(null);
    } catch {
      this.locationsError.set('Die Filialen konnten nicht geladen werden. Bitte die Seite neu laden.');
    }
  }
}
