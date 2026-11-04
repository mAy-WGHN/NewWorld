import { Component, computed, inject, signal } from '@angular/core';
import { MatCheckboxChange, MatCheckboxModule } from '@angular/material/checkbox';
import { MatCardModule } from '@angular/material/card';
import { MatChipsModule } from '@angular/material/chips';
import { AuthService } from '../../core/auth/auth.service';
import { GENERIC_ERROR, problemTitle } from '../absences/absence-models';
import { NotificationType, channelLabel, typeLabel } from './notification-models';
import { NotificationsApi } from './notifications.api';

/** "Was möchtest du erhalten?": the choice per kind of notification and channel. Used on the notifications page and in the settings. */
@Component({
  selector: 'app-notification-preferences',
  imports: [MatCheckboxModule, MatCardModule, MatChipsModule],
  template: `
    @if (error(); as message) {
      <p class="mb-3 rounded-xl border border-error px-4 py-2.5 text-error" role="alert">{{ message }}</p>
    }
    @if (canRead() && types().length) {
      <section data-testid="preferences" aria-labelledby="choices-title">
        <h2 id="choices-title" class="m-0 mb-1 text-lg font-medium">Was möchtest du erhalten?</h2>
        <p class="m-0 mb-3 text-sm text-on-surface-variant">Pflichtmitteilungen lassen sich nicht abschalten.</p>
        <mat-card appearance="outlined" class="overflow-x-auto">
          <table class="w-full border-collapse">
            <thead>
              <tr class="text-sm text-on-surface-variant">
                <th scope="col" class="px-4 py-2.5 text-left font-medium">Mitteilung</th>
                @for (c of types()[0].channels; track c.channel) {
                  <th scope="col" class="px-4 py-2.5 text-center font-medium">{{ channelLabel(c.channel) }}</th>
                }
              </tr>
            </thead>
            <tbody>
              @for (t of types(); track t.templateKey) {
                <tr class="border-t border-outline-variant">
                  <th scope="row" class="px-4 py-1.5 text-left font-medium">
                    {{ typeLabel(t.templateKey) }}
                    @if (t.mandatory) {
                      <mat-chip-set class="ml-2 inline-block align-middle" aria-label="Pflicht"><mat-chip>Pflicht</mat-chip></mat-chip-set>
                    }
                  </th>
                  @for (c of t.channels; track c.channel) {
                    <td class="px-4 py-1.5 text-center">
                      @for (version of [rebuild()]; track version) {
                        <mat-checkbox [attr.data-key]="t.templateKey" [attr.data-channel]="c.channel" [checked]="c.enabled" [disabled]="t.mandatory || !canChange()"
                                      [attr.aria-label]="typeLabel(t.templateKey) + ': ' + channelLabel(c.channel)" (change)="toggle(t, c.channel, $event)" />
                      }
                    </td>
                  }
                </tr>
              }
            </tbody>
          </table>
        </mat-card>
      </section>
    }
  `,
})
export class NotificationPreferences {
  private readonly api = inject(NotificationsApi);
  private readonly auth = inject(AuthService);

  protected readonly canRead = computed(() => this.auth.privileges().has('preference:read-own'));
  protected readonly canChange = computed(() => this.auth.privileges().has('preference:update-own'));

  protected readonly types = signal<NotificationType[]>([]);
  /** Counted up to build the check boxes again (see `toggle`). */
  protected readonly rebuild = signal(0);
  protected readonly error = signal<string | null>(null);

  protected readonly typeLabel = typeLabel;
  protected readonly channelLabel = channelLabel;

  constructor() {
    if (this.canRead()) {
      void this.load();
    }
  }

  protected async toggle(type: NotificationType, channel: string, event: MatCheckboxChange): Promise<void> {
    const enabled = event.checked;
    this.error.set(null);
    this.setChoice(type.templateKey, channel, enabled);
    try {
      await this.api.setPreference(type.templateKey, channel, enabled);
    } catch (error) {
      this.setChoice(type.templateKey, channel, !enabled);
      this.rebuild.update((n) => n + 1); // the boxes are built again from the data, so none keeps a value the service refused
      this.error.set(problemTitle(error) === 'Mandatory' ? 'Diese Mitteilung ist Pflicht und lässt sich nicht abschalten.' : GENERIC_ERROR);
    }
  }

  private setChoice(key: string, channel: string, enabled: boolean): void {
    this.types.update((list) =>
      list.map((t) => (t.templateKey === key ? { ...t, channels: t.channels.map((c) => (c.channel === channel ? { ...c, enabled } : c)) } : t)),
    );
  }

  private async load(): Promise<void> {
    try {
      this.types.set(await this.api.preferences());
    } catch {
      this.error.set('Die Einstellungen konnten nicht geladen werden.');
    }
  }
}
