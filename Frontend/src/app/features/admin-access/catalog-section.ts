import { Component, computed, inject, signal } from '@angular/core';
import { MatCheckboxChange } from '@angular/material/checkbox';
import { AuthService } from '../../core/auth/auth.service';
import { AdminAccessApi } from './admin-access.api';
import { CatalogEntry, validateCustomKey } from './admin-access-models';
import { ADMIN_UI, ActionMessages, ActionState } from './admin-ui';

interface OwnerGroup {
  owner: string;
  title: string;
  entries: CatalogEntry[];
}

const normalize = (text: string): string => text.normalize('NFD').replace(/\p{M}/gu, '').toLowerCase().trim();

/**
 * "Rechte-Katalog": all privileges per application with what each one allows. Only registered privileges take effect. Own privileges
 * (owner "custom") can be created and deleted; those of the applications are read-only.
 */
@Component({
  selector: 'app-catalog-section',
  imports: [...ADMIN_UI, ActionMessages],
  templateUrl: './catalog-section.html',
})
export class CatalogSection {
  private readonly api = inject(AdminAccessApi);
  private readonly auth = inject(AuthService);

  protected readonly act = new ActionState();
  protected readonly entries = signal<CatalogEntry[]>([]);
  protected readonly loaded = signal(false);
  protected readonly loadError = signal<string | null>(null);
  protected readonly query = signal('');

  protected readonly canCreate = computed(() => this.auth.privileges().has('access:custom-privilege:create'));
  protected readonly canDelete = computed(() => this.auth.privileges().has('access:custom-privilege:delete'));

  protected readonly creating = signal(false);
  protected readonly newKey = signal('');
  protected readonly newScopable = signal(false);
  protected readonly newDescription = signal('');
  protected readonly keyError = signal<string | null>(null);
  protected readonly confirmingDeleteKey = signal<string | null>(null);

  protected readonly groups = computed<OwnerGroup[]>(() => {
    const q = normalize(this.query());
    const byOwner = new Map<string, CatalogEntry[]>();
    for (const e of [...this.entries()].sort((a, b) => a.key.localeCompare(b.key))) {
      if (q && ![e.key, e.description, e.owner].some((t) => normalize(t).includes(q))) {
        continue;
      }
      byOwner.set(e.owner, [...(byOwner.get(e.owner) ?? []), e]);
    }
    return [...byOwner.entries()]
      .sort(([a], [b]) => (a === 'custom' ? 1 : b === 'custom' ? -1 : a.localeCompare(b)))
      .map(([owner, entries]) => ({ owner, title: owner === 'custom' ? 'Eigene Rechte' : `Anwendung „${owner}“`, entries }));
  });

  constructor() {
    void this.load();
  }

  protected async load(): Promise<void> {
    try {
      this.entries.set(await this.api.catalog());
      this.loadError.set(null);
    } catch {
      this.loadError.set('Der Katalog konnte nicht geladen werden.');
    } finally {
      this.loaded.set(true);
    }
  }

  protected onQuery(event: Event): void {
    this.query.set((event.target as HTMLInputElement).value);
  }

  protected openCreate(): void {
    this.creating.set(true);
    this.act.clear();
  }

  protected closeCreate(): void {
    this.creating.set(false);
    this.newKey.set('');
    this.newScopable.set(false);
    this.newDescription.set('');
    this.keyError.set(null);
  }

  protected onKey(event: Event): void {
    this.newKey.set((event.target as HTMLInputElement).value);
    this.keyError.set(null);
  }

  protected onDescription(event: Event): void {
    this.newDescription.set((event.target as HTMLTextAreaElement).value);
  }

  protected onScopable(event: MatCheckboxChange): void {
    this.newScopable.set(event.checked);
  }

  protected async create(): Promise<void> {
    const error = validateCustomKey(this.newKey(), this.entries());
    this.keyError.set(error);
    if (error) {
      return;
    }
    const key = this.newKey().trim();
    const ok = await this.act.run(async () => {
      await this.api.createCustomPrivilege(key, this.newScopable(), this.newDescription().trim() || null);
      this.entries.set(await this.api.catalog());
    }, `Das Recht „${key}“ wurde angelegt. Nimm es in eine Rolle auf, damit es wirkt.`);
    if (ok) {
      this.closeCreate();
    }
  }

  protected askDelete(key: string): void {
    this.act.clear();
    this.confirmingDeleteKey.set(key);
  }

  protected cancelDelete(): void {
    this.confirmingDeleteKey.set(null);
  }

  protected async remove(key: string): Promise<void> {
    const ok = await this.act.run(async () => {
      await this.api.deleteCustomPrivilege(key);
      this.entries.set(await this.api.catalog());
    }, `Das Recht „${key}“ wurde gelöscht. Es wirkt nirgends mehr.`);
    if (ok) {
      this.confirmingDeleteKey.set(null);
    }
  }
}
