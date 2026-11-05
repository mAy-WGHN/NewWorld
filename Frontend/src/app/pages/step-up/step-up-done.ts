import { Component, OnInit, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { StepUpError, StepUpFailure, StepUpService } from '../../core/auth/step-up.service';

const MESSAGES: Record<StepUpFailure, string> = {
  'no-link': 'Für dich ist noch kein Microsoft-Konto hinterlegt. Bitte wende dich an die Personalabteilung.',
  unavailable: 'Die Bestätigung mit Microsoft ist gerade nicht verfügbar. Bitte versuche es später noch einmal.',
  'not-confirmed': 'Die Bestätigung hat nicht geklappt. Bitte versuche es noch einmal.',
  error: 'Die Bestätigung hat nicht geklappt. Bitte versuche es noch einmal.',
};

/**
 * The way back from Microsoft after the second factor (`/step-up/done?result=ok|failed`). On success the person continues where they were;
 * otherwise they are told in plain words and can try again. No detail of the failure comes back from Microsoft, on purpose.
 */
@Component({
  selector: 'app-step-up-done',
  imports: [MatButtonModule, RouterLink],
  template: `
    <h1 class="m-0 mb-2 text-2xl font-medium">Bestätigung mit Microsoft</h1>
    @if (message(); as text) {
      <p class="m-0 mb-4 text-on-surface-variant" role="alert" data-testid="step-up-error">{{ text }}</p>
      <div class="flex flex-wrap gap-2">
        <button mat-flat-button type="button" data-action="retry" [disabled]="busy()" (click)="retry()">Noch einmal versuchen</button>
        <a mat-stroked-button routerLink="/">Zur Startseite</a>
      </div>
    } @else {
      <p class="m-0 text-on-surface-variant" role="status" data-testid="step-up-working">Einen Moment, deine Bestätigung wird abgeschlossen …</p>
    }
  `,
})
export class StepUpDonePage implements OnInit {
  private readonly stepUp = inject(StepUpService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  protected readonly message = signal<string | null>(null);
  protected readonly busy = signal(false);

  async ngOnInit(): Promise<void> {
    if (this.route.snapshot.queryParamMap.get('result') !== 'ok') {
      this.message.set(MESSAGES['not-confirmed']);
      return;
    }
    try {
      await this.router.navigateByUrl(await this.stepUp.complete(), { replaceUrl: true });
    } catch (error) {
      this.message.set(MESSAGES[error instanceof StepUpError ? error.failure : 'error']);
    }
  }

  protected async retry(): Promise<void> {
    this.busy.set(true);
    try {
      await this.stepUp.begin(this.stepUp.pendingReturn());
    } catch (error) {
      this.message.set(MESSAGES[error instanceof StepUpError ? error.failure : 'error']);
    } finally {
      this.busy.set(false);
    }
  }
}
