import { Component, computed, inject } from '@angular/core';
import { AuthService } from '../../core/auth/auth.service';
import { LogbookReminderAdmin } from './logbook-reminder-admin';
import { LogbookStructureAdmin } from './logbook-structure-admin';
import { LogbookTemplateAdmin } from './logbook-template-admin';
import { LOGBOOK_UI } from './logbook-ui';

/**
 * "Berichtsheft-Verwaltung": the hierarchy (`logbook:manage-structure`), the template of the report (`logbook:manage-template`) and the automatic
 * reminder (`logbook:manage-reminders`). Each part shows for the people who hold its privilege and loads when its tab is opened.
 */
@Component({
  selector: 'app-logbook-admin',
  imports: [LogbookStructureAdmin, LogbookTemplateAdmin, LogbookReminderAdmin, ...LOGBOOK_UI],
  template: `
    <app-page-head title="Berichtsheft-Verwaltung" />
    <mat-tab-group animationDuration="0ms" [mat-stretch-tabs]="false" data-testid="tabs">
      @if (canStructure()) {
        <mat-tab label="Hierarchie">
          <ng-template matTabContent>
            <div class="pt-4"><app-logbook-structure-admin /></div>
          </ng-template>
        </mat-tab>
      }
      @if (canTemplate()) {
        <mat-tab label="Vorlage">
          <ng-template matTabContent>
            <div class="pt-4"><app-logbook-template-admin /></div>
          </ng-template>
        </mat-tab>
      }
      @if (canReminders()) {
        <mat-tab label="Erinnerung">
          <ng-template matTabContent>
            <div class="pt-4"><app-logbook-reminder-admin /></div>
          </ng-template>
        </mat-tab>
      }
    </mat-tab-group>
  `,
})
export class LogbookAdminPage {
  private readonly auth = inject(AuthService);

  protected readonly canStructure = computed(() => this.auth.privileges().has('logbook:manage-structure'));
  protected readonly canTemplate = computed(() => this.auth.privileges().has('logbook:manage-template'));
  protected readonly canReminders = computed(() => this.auth.privileges().has('logbook:manage-reminders'));
}
