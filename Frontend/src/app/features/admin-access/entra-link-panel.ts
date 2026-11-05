import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { AuthService } from '../../core/auth/auth.service';
import { AdminAccessApi } from './admin-access.api';
import { EntraLink, validateUserPrincipalName } from './admin-access-models';
import { ADMIN_UI, ActionMessages, ActionState } from './admin-ui';

/**
 * "Microsoft-Konto" of one person: the account whose Authenticator app confirms the second factor for the payroll statement. The personnel
 * administration enters the account name (e-mail address); with the first successful confirmation Access remembers the account itself, from then
 * on only that account counts. Removing the link takes the second factor away until a new one is entered.
 */
@Component({
  selector: 'app-entra-link-panel',
  imports: [...ADMIN_UI, ActionMessages],
  templateUrl: './entra-link-panel.html',
})
export class EntraLinkPanel {
  private readonly api = inject(AdminAccessApi);
  private readonly auth = inject(AuthService);

  readonly personId = input.required<string>();

  protected readonly act = new ActionState();
  protected readonly link = signal<EntraLink | null>(null);
  protected readonly loaded = signal(false);
  protected readonly loadError = signal<string | null>(null);
  protected readonly name = signal('');
  protected readonly formError = signal<string | null>(null);
  protected readonly confirmingRemove = signal(false);

  protected readonly canRead = computed(() => this.auth.privileges().has('access:person:read'));
  protected readonly canManage = computed(() => this.auth.privileges().has('access:entra-link:manage'));

  constructor() {
    effect(() => {
      const id = this.personId();
      untracked(() => void this.load(id));
    });
  }

  private async load(id: string): Promise<void> {
    this.loaded.set(false);
    this.confirmingRemove.set(false);
    this.act.clear();
    this.name.set('');
    this.formError.set(null);
    if (this.canRead()) {
      try {
        this.link.set(await this.api.entraLink(id));
        this.loadError.set(null);
      } catch (error) {
        this.link.set(null);
        // "not found" simply means nothing is linked yet
        this.loadError.set(
          error instanceof HttpErrorResponse && error.status === 404
            ? null
            : error instanceof HttpErrorResponse && error.status === 403
              ? 'Du darfst das Microsoft-Konto dieser Person nicht sehen.'
              : 'Das Microsoft-Konto konnte nicht geladen werden.',
        );
      }
    }
    this.loaded.set(true);
  }

  protected onName(event: Event): void {
    this.name.set((event.target as HTMLInputElement).value);
    this.formError.set(null);
  }

  protected async save(): Promise<void> {
    const error = validateUserPrincipalName(this.name());
    this.formError.set(error);
    if (error) {
      return;
    }
    const id = this.personId();
    const value = this.name().trim();
    const ok = await this.act.run(async () => {
      await this.api.setEntraLink(id, value);
      if (this.canRead()) {
        this.link.set(await this.api.entraLink(id));
      }
    }, 'Das Microsoft-Konto wurde eingetragen. Die Person bestätigt beim nächsten Mal mit diesem Konto.');
    if (ok) {
      this.name.set('');
    }
  }

  protected askRemove(): void {
    this.act.clear();
    this.confirmingRemove.set(true);
  }

  protected cancelRemove(): void {
    this.confirmingRemove.set(false);
  }

  protected async remove(): Promise<void> {
    const id = this.personId();
    const ok = await this.act.run(async () => {
      await this.api.clearEntraLink(id);
      this.link.set(null);
    }, 'Die Zuordnung wurde entfernt. Ohne neue Zuordnung kann die Person den zweiten Faktor nicht nutzen.');
    if (ok) {
      this.confirmingRemove.set(false);
    }
  }
}
