import { Component, inject, signal } from '@angular/core';
import { LogbookApi } from './logbook.api';
import { ReminderPoint, ReminderSettings, WEEKDAYS, logbookError, stamp } from './logbook-models';
import { Action, LOGBOOK_UI } from './logbook-ui';

/**
 * The automatic reminder (decision 49): on or off, the due day and time of the report, the reminder points (days after the due day and a time, local
 * time), and the most reminders per store and week (manual ones count with). Switching it on starts it now: points before that are not sent. The text
 * of the reminder is maintained with the notification texts of the personnel department.
 */
@Component({
  selector: 'app-logbook-reminder-admin',
  imports: [...LOGBOOK_UI],
  templateUrl: './logbook-reminder-admin.html',
})
export class LogbookReminderAdmin {
  private readonly api = inject(LogbookApi);

  protected readonly weekdays = WEEKDAYS;
  protected readonly enabled = signal(true);
  protected readonly enabledAt = signal<string | null>(null);
  protected readonly dueDay = signal('friday');
  protected readonly dueTime = signal('18:00');
  protected readonly max = signal(2);
  protected readonly points = signal<ReminderPoint[]>([]);
  protected readonly loaded = signal(false);
  protected readonly loadError = signal<string | null>(null);
  protected readonly save = new Action();
  protected readonly stamp = stamp;

  constructor() {
    void this.load();
  }

  private async load(): Promise<void> {
    try {
      this.fill(await this.api.reminderSettings());
      this.loaded.set(true);
    } catch (error) {
      this.loadError.set(logbookError(error));
    }
  }

  private fill(s: ReminderSettings): void {
    this.enabled.set(s.enabled);
    this.enabledAt.set(s.enabledAt);
    this.dueDay.set(s.dueDay);
    this.dueTime.set(s.dueTime);
    this.max.set(s.maxReminders);
    this.points.set(s.points.map((p) => ({ ...p })));
  }

  protected value(event: Event): string {
    return (event.target as HTMLInputElement | HTMLSelectElement).value;
  }

  protected addPoint(): void {
    this.points.update((p) => [...p, { daysAfterDue: 0, time: '09:00' }]);
    this.save.reset();
  }

  protected removePoint(index: number): void {
    this.points.update((p) => p.filter((_, i) => i !== index));
    this.save.reset();
  }

  protected setPoint(index: number, change: Partial<ReminderPoint>): void {
    this.points.update((p) => p.map((point, i) => (i === index ? { ...point, ...change } : point)));
    this.save.reset();
  }

  protected async submit(): Promise<void> {
    await this.save.run(async () => {
      this.fill(
        await this.api.saveReminderSettings({
          enabled: this.enabled(),
          dueDay: this.dueDay(),
          dueTime: this.dueTime(),
          points: this.points(),
          maxReminders: this.max(),
        }),
      );
    }, 'Die Einstellungen sind gespeichert.');
  }
}
