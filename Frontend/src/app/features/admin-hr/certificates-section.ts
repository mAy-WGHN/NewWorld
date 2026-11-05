import { Component, computed, inject, signal } from '@angular/core';
import { AuthService } from '../../core/auth/auth.service';
import { PAGE_UI } from '../../shared/ui/material';
import { formatDateTime } from '../notifications/notification-models';
import { AccessLogEntry, CertificateReminders, CertificateRetention, accessActionLabel, describeError, loadFailed, wholeNumber } from './admin-hr-models';
import { AdminHrApi } from './admin-hr.api';

const LOG_PAGE_SIZE = 50;

/**
 * "Krankmeldung und AU" (decision 20): how long the files are kept after the end of the sick note, when people are reminded to hand in the AU,
 * and, with `certificate:read-access-log`, who did what to which document (never the content). Retention and reminders need
 * `certificate:manage-retention`.
 */
@Component({
  selector: 'app-certificates-section',
  imports: [...PAGE_UI],
  templateUrl: './certificates-section.html',
})
export class CertificatesSection {
  private readonly api = inject(AdminHrApi);
  private readonly auth = inject(AuthService);

  protected readonly canManage = computed(() => this.auth.privileges().has('certificate:manage-retention'));
  protected readonly canReadLog = computed(() => this.auth.privileges().has('certificate:read-access-log'));

  protected readonly formatDateTime = formatDateTime;
  protected readonly actionLabel = accessActionLabel;

  // retention
  protected readonly retention = signal<CertificateRetention | null>(null);
  protected readonly days = signal('');
  protected readonly retentionBusy = signal(false);
  protected readonly retentionError = signal<string | null>(null);
  protected readonly retentionStatus = signal<string | null>(null);

  // reminders
  protected readonly reminders = signal<CertificateReminders | null>(null);
  protected readonly first = signal('');
  protected readonly every = signal('');
  protected readonly max = signal('');
  protected readonly remindersBusy = signal(false);
  protected readonly remindersError = signal<string | null>(null);
  protected readonly remindersStatus = signal<string | null>(null);

  // access log
  protected readonly log = signal<AccessLogEntry[]>([]);
  protected readonly logTotal = signal(0);
  protected readonly logLoaded = signal(false);
  protected readonly logError = signal<string | null>(null);
  protected readonly logBusy = signal(false);
  protected readonly from = signal('');
  protected readonly to = signal('');
  private logPage = 1;
  protected readonly hasMore = computed(() => this.log().length < this.logTotal());

  constructor() {
    if (this.canManage()) {
      void this.loadRetention();
      void this.loadReminders();
    }
    if (this.canReadLog()) {
      void this.loadLog(true);
    }
  }

  private async loadRetention(): Promise<void> {
    try {
      const r = await this.api.retention();
      this.retention.set(r);
      this.days.set(String(r.retentionAfterEndDays));
    } catch {
      this.retentionError.set(loadFailed('Die Löschfrist'));
    }
  }

  private async loadReminders(): Promise<void> {
    try {
      const r = await this.api.reminders();
      this.setReminders(r);
    } catch {
      this.remindersError.set(loadFailed('Die Einstellungen der Erinnerungen'));
    }
  }

  private setReminders(r: CertificateReminders): void {
    this.reminders.set(r);
    this.first.set(String(r.firstReminderAfterReturnDays));
    this.every.set(String(r.reminderEveryDays));
    this.max.set(String(r.maxReminders));
  }

  protected value(target: { set(v: string): void }, event: Event): void {
    target.set((event.target as HTMLInputElement).value);
  }

  protected async saveRetention(): Promise<void> {
    const days = wholeNumber(this.days(), 0, 3650, 'Die Löschfrist');
    if (typeof days === 'string') {
      this.retentionError.set(days);
      return;
    }
    this.retentionBusy.set(true);
    this.retentionError.set(null);
    this.retentionStatus.set(null);
    try {
      const saved = await this.api.saveRetention(days);
      this.retention.set(saved);
      this.days.set(String(saved.retentionAfterEndDays));
      this.retentionStatus.set('Die Löschfrist ist gespeichert.');
    } catch (e) {
      this.retentionError.set(describeError(e));
    } finally {
      this.retentionBusy.set(false);
    }
  }

  protected async saveReminders(): Promise<void> {
    const first = wholeNumber(this.first(), 0, 90, 'Die Zahl der Tage bis zur ersten Erinnerung');
    const every = typeof first === 'string' ? first : wholeNumber(this.every(), 1, 90, 'Der Abstand der Erinnerungen');
    const max = typeof every === 'string' ? every : wholeNumber(this.max(), 0, 90, 'Die Höchstzahl der Erinnerungen');
    if (typeof first === 'string' || typeof every === 'string' || typeof max === 'string') {
      this.remindersError.set([first, every, max].find((x): x is string => typeof x === 'string') ?? null);
      return;
    }
    this.remindersBusy.set(true);
    this.remindersError.set(null);
    this.remindersStatus.set(null);
    try {
      this.setReminders(await this.api.saveReminders({ firstReminderAfterReturnDays: first, reminderEveryDays: every, maxReminders: max }));
      this.remindersStatus.set('Die Erinnerungen sind gespeichert.');
    } catch (e) {
      this.remindersError.set(describeError(e));
    } finally {
      this.remindersBusy.set(false);
    }
  }

  protected async loadLog(reset: boolean): Promise<void> {
    if (this.logBusy()) {
      return;
    }
    this.logBusy.set(true);
    this.logError.set(null);
    const page = reset ? 1 : this.logPage + 1;
    try {
      // The dates are whole days: from the start of the first to the end of the last (local time).
      const from = this.from() ? new Date(`${this.from()}T00:00:00`).toISOString() : null;
      const to = this.to() ? new Date(`${this.to()}T23:59:59.999`).toISOString() : null;
      const result = await this.api.accessLog(from, to, page, LOG_PAGE_SIZE);
      this.log.update((old) => (reset ? result.items : [...old, ...result.items]));
      this.logTotal.set(result.total);
      this.logPage = page;
      this.logLoaded.set(true);
    } catch {
      this.logError.set(loadFailed('Das Zugriffsprotokoll'));
    } finally {
      this.logBusy.set(false);
    }
  }
}
