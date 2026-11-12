import { Component, computed, inject, signal } from '@angular/core';
import { AuthService } from '../../core/auth/auth.service';
import { LogbookEvaluationView } from './logbook-evaluation';
import { LogbookInbox } from './logbook-inbox';
import { LogbookMine } from './logbook-mine';
import { LogbookMissing } from './logbook-missing';
import { LOGBOOK_UI } from './logbook-ui';
import { LogbookApi } from './logbook.api';

/**
 * "Berichtsheft": the weekly report of the stores. Store managers (`logbook:write`) fill in and hand in their report; regional and district leaders
 * (`logbook:review`) read what comes in and remind the stores that have not handed in. Each tab only loads when it is opened. The screen only guides
 * the person, the service decides who sees what. Which reports a leader sees comes from the hierarchy of the Berichtsheft (who leads which region or
 * district), not from the role: a leader who is not entered there sees nothing, and this page says so instead of showing empty lists.
 */
@Component({
  selector: 'app-logbook',
  imports: [LogbookMine, LogbookInbox, LogbookMissing, LogbookEvaluationView, ...LOGBOOK_UI],
  template: `
    <app-page-head title="Berichtsheft" />
    @if (reachesNothing()) {
      <p class="mb-4 rounded-xl border border-outline-variant bg-attention px-4 py-3 text-on-attention" role="note" data-testid="no-reach">
        Du bist im Berichtsheft noch keiner Region und keinem Bezirk als Leitung zugeordnet, deshalb siehst du keine Berichte. Das richtet die
        Berichtsheft-Verwaltung unter „Hierarchie“ ein (Regionalleitung bei der Region, Bezirksleitung beim Bezirk eintragen und die Filialen zuordnen).
        Die Rolle allein reicht dafür nicht.
      </p>
    }
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
  private readonly api = inject(LogbookApi);

  protected readonly canWrite = computed(() => this.auth.privileges().has('logbook:write'));
  protected readonly canReview = computed(() => this.auth.privileges().has('logbook:review'));

  /** Whether a person with the role of a leader is not a leader in the hierarchy (known only after the service answered). */
  protected readonly reachesNothing = signal(false);

  constructor() {
    if (this.canReview()) {
      void this.checkReach();
    }
  }

  private async checkReach(): Promise<void> {
    try {
      this.reachesNothing.set((await this.api.me()).stores.length === 0);
    } catch {
      // without an answer there is no hint; the lists show their own errors
    }
  }
}
