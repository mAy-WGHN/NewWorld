import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { AuthService } from '../../core/auth/auth.service';
import { AdminAccessApi } from './admin-access.api';
import { MainStore, StoreOption, storeLabel, storeName } from './admin-access-models';
import { ADMIN_UI, ActionMessages, ActionState } from './admin-ui';

/**
 * "Hauptfiliale" of one person. BC only suggests it ("Vorgabefiliale"); what is set here wins and is never overwritten by BC. Removing it
 * brings the BC suggestion back. The main store decides the reach of "Hauptfiliale" assignments, responsibilities and public holidays.
 */
@Component({
  selector: 'app-main-store-panel',
  imports: [...ADMIN_UI, ActionMessages],
  templateUrl: './main-store-panel.html',
})
export class MainStorePanel {
  private readonly api = inject(AdminAccessApi);
  private readonly auth = inject(AuthService);

  readonly personId = input.required<string>();

  protected readonly act = new ActionState();
  protected readonly main = signal<MainStore | null>(null);
  protected readonly stores = signal<StoreOption[]>([]);
  protected readonly loaded = signal(false);
  protected readonly loadError = signal<string | null>(null);
  protected readonly choice = signal('');
  protected readonly confirmingClear = signal(false);
  protected readonly formError = signal<string | null>(null);

  protected readonly canRead = computed(() => this.auth.privileges().has('access:person:read'));
  protected readonly canUpdate = computed(() => this.auth.privileges().has('access:main-store:update'));

  protected readonly storeLabel = storeLabel;

  constructor() {
    effect(() => {
      const id = this.personId();
      untracked(() => void this.load(id));
    });
  }

  protected name(id: string | null): string {
    return storeName(id, this.stores());
  }

  private async load(id: string): Promise<void> {
    this.loaded.set(false);
    this.confirmingClear.set(false);
    this.act.clear();
    this.choice.set('');
    this.formError.set(null);
    if (this.canRead()) {
      try {
        this.main.set(await this.api.mainStore(id));
        this.loadError.set(null);
      } catch (error) {
        this.main.set(null);
        this.loadError.set(
          error instanceof HttpErrorResponse && error.status === 404
            ? 'Diese Person ist in Access nicht bekannt.'
            : error instanceof HttpErrorResponse && error.status === 403
              ? 'Du darfst die Hauptfiliale dieser Person nicht sehen.'
              : 'Die Hauptfiliale konnte nicht geladen werden.',
        );
      }
    }
    this.loaded.set(true);
    try {
      this.stores.set(await this.api.stores());
    } catch {
      this.stores.set([]);
    }
  }

  protected onChoice(event: Event): void {
    this.choice.set((event.target as HTMLSelectElement).value);
    this.formError.set(null);
  }

  protected async save(): Promise<void> {
    if (!this.choice()) {
      this.formError.set('Bitte eine Filiale wählen.');
      return;
    }
    const id = this.personId();
    const store = this.choice();
    const ok = await this.act.run(async () => {
      await this.api.setMainStore(id, store);
      if (this.canRead()) {
        this.main.set(await this.api.mainStore(id));
      }
    }, 'Die Hauptfiliale wurde gesetzt. Sie gilt sofort und BC überschreibt sie nicht.');
    if (ok) {
      this.choice.set('');
    }
  }

  protected askClear(): void {
    this.act.clear();
    this.confirmingClear.set(true);
  }

  protected cancelClear(): void {
    this.confirmingClear.set(false);
  }

  protected async clear(): Promise<void> {
    const id = this.personId();
    const ok = await this.act.run(async () => {
      await this.api.clearMainStore(id);
      if (this.canRead()) {
        this.main.set(await this.api.mainStore(id));
      }
    }, 'Die eigene Einstellung wurde entfernt. Es gilt wieder der Vorschlag aus BC.');
    if (ok) {
      this.confirmingClear.set(false);
    }
  }
}
