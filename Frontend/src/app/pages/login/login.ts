import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { AuthService, LoginError } from '../../core/auth/auth.service';

/** Only paths inside the app are allowed as a return address (no other site). */
export function safeReturnUrl(value: string | null): string {
  return value && value.startsWith('/') && !value.startsWith('//') && !value.startsWith('/login') ? value : '/';
}

@Component({
  selector: 'app-login',
  templateUrl: './login.html',
  styleUrl: './login.scss',
})
export class LoginPage {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  protected readonly personnelNumber = signal('');
  protected readonly pin = signal('');
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  protected set(field: 'personnelNumber' | 'pin', event: Event): void {
    (field === 'pin' ? this.pin : this.personnelNumber).set((event.target as HTMLInputElement).value);
  }

  protected async submit(event: Event): Promise<void> {
    event.preventDefault();
    if (this.busy() || !this.personnelNumber().trim() || !this.pin()) {
      return;
    }
    this.busy.set(true);
    this.error.set(null);
    try {
      await this.auth.login(this.personnelNumber().trim(), this.pin());
      this.pin.set('');
      await this.router.navigateByUrl(safeReturnUrl(this.route.snapshot.queryParamMap.get('returnUrl')));
    } catch (error) {
      this.pin.set('');
      this.error.set(this.message(error));
    } finally {
      this.busy.set(false);
    }
  }

  private message(error: unknown): string {
    if (error instanceof LoginError) {
      switch (error.failure) {
        case 'invalid':
          return 'Personalnummer oder PIN ist falsch.';
        case 'locked':
          return error.retryAfterSeconds
            ? `Zu viele Versuche. Bitte in ${Math.ceil(error.retryAfterSeconds / 60)} Minute(n) erneut versuchen.`
            : 'Zu viele Versuche. Bitte später erneut versuchen.';
        case 'unavailable':
          return 'Die Anmeldung ist gerade nicht möglich. Bitte später erneut versuchen.';
      }
    }
    return 'Die Anmeldung ist fehlgeschlagen. Bitte erneut versuchen.';
  }
}
