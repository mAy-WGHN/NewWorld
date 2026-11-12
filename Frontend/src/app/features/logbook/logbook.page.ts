import { Component, computed, inject } from '@angular/core';
import { AuthService } from '../../core/auth/auth.service';
import { LogbookEvaluationView } from './logbook-evaluation';
import { LogbookInbox } from './logbook-inbox';
import { LogbookMine } from './logbook-mine';
import { LogbookMissing } from './logbook-missing';
import { LOGBOOK_UI } from './logbook-ui';

/**
 * "Berichtsheft": the weekly report of the stores. Store managers (`logbook:write`) fill in and hand in their report; regional and district leaders
 * (`logbook:review`) read what comes in and remind the stores that have not handed in. Each tab only loads when it is opened. The screen only guides
 * the person, the service decides who sees what.
 */
@Component({
  selector: 'app-logbook',
  imports: [LogbookMine, LogbookInbox, LogbookMissing, LogbookEvaluationView, ...LOGBOOK_UI],
  template: `
    <app-page-head title="Berichtsheft" />
    <mat-tab-group animationDuration="0ms" [mat-stretch-tabs]="false" data-testid="tabs">
      @if (canWrite()) {
        <mat-tab label="Mein Bericht">
          <ng-template matTabContent>
            <div class="pt-4"><app-logbook-mine /></div>
          </ng-template>
        </mat-tab>
      }
      @if (canReview()) {
        <mat-tab label="Eingang">
          <ng-template matTabContent>
            <div class="pt-4"><app-logbook-inbox /></div>
          </ng-template>
        </mat-tab>
        <mat-tab label="Fehlende Berichte">
          <ng-template matTabContent>
            <div class="pt-4"><app-logbook-missing /></div>
          </ng-template>
        </mat-tab>
      }
      @if (canWrite() || canReview()) {
        <mat-tab label="Auswertung">
          <ng-template matTabContent>
            <div class="pt-4"><app-logbook-evaluation /></div>
          </ng-template>
        </mat-tab>
      }
    </mat-tab-group>
  `,
})
export class LogbookPage {
  private readonly auth = inject(AuthService);

  protected readonly canWrite = computed(() => this.auth.privileges().has('logbook:write'));
  protected readonly canReview = computed(() => this.auth.privileges().has('logbook:review'));
}
