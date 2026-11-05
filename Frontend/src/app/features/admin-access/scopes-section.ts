import { Component, computed, inject, signal } from '@angular/core';
import { AuthService } from '../../core/auth/auth.service';
import { StorePicker } from '../report-issue/store-picker';
import { AdminAccessApi } from './admin-access.api';
import { StoreGroup, StoreOption } from './admin-access-models';
import { ADMIN_UI, ActionMessages, ActionState } from './admin-ui';

/**
 * "Filial-Gruppen" (scopes): a named set of stores, for example a district. A role assignment can be limited to such groups. The name
 * cannot be changed afterwards (the service only changes the stores). Changing the stores changes the reach of everyone who has the group.
 */
@Component({
  selector: 'app-scopes-section',
  imports: [...ADMIN_UI, ActionMessages, StorePicker],
  templateUrl: './scopes-section.html',
})
export class ScopesSection {
  private readonly api = inject(AdminAccessApi);
  private readonly auth = inject(AuthService);

  protected readonly act = new ActionState();
  protected readonly scopes = signal<StoreGroup[]>([]);
  protected readonly stores = signal<StoreOption[]>([]);
  protected readonly loaded = signal(false);
  protected readonly loadError = signal<string | null>(null);
  protected readonly storesError = signal(false);

  protected readonly canCreate = computed(() => this.auth.privileges().has('access:scope:create'));
  protected readonly canUpdate = computed(() => this.auth.privileges().has('access:scope:update'));
  protected readonly canDelete = computed(() => this.auth.privileges().has('access:scope:delete'));

  protected readonly editingId = signal<string | null>(null);
  protected readonly draft = signal<StoreOption[]>([]);
  /** Stores of the group that are not in the list of the portal (for example closed); they stay as they are. */
  protected readonly keptIds = signal<string[]>([]);
  protected readonly confirmingDeleteId = signal<string | null>(null);

  protected readonly creating = signal(false);
  protected readonly newName = signal('');
  protected readonly newStores = signal<StoreOption[]>([]);
  protected readonly nameError = signal<string | null>(null);

  protected readonly editing = computed(() => this.scopes().find((s) => s.id === this.editingId()) ?? null);
  protected readonly changed = computed(() => {
    const scope = this.editing();
    return scope ? this.draftIds().sort().join('|') !== [...scope.storeIds].sort().join('|') : false;
  });

  constructor() {
    void this.load();
  }

  protected namesOf(scope: StoreGroup): string {
    const names = scope.storeIds.map((id) => this.stores().find((s) => s.id === id)?.name ?? 'unbekannte Filiale');
    return names.length ? names.join(', ') : 'keine Filialen';
  }

  protected async load(): Promise<void> {
    try {
      this.scopes.set(await this.api.scopes());
      this.loadError.set(null);
    } catch {
      this.loadError.set('Die Filial-Gruppen konnten nicht geladen werden.');
    } finally {
      this.loaded.set(true);
    }
    try {
      this.stores.set(await this.api.stores());
      this.storesError.set(false);
    } catch {
      this.storesError.set(true);
    }
  }

  private draftIds(): string[] {
    return [...this.draft().map((s) => s.id), ...this.keptIds()];
  }

  protected edit(scope: StoreGroup): void {
    if (this.editingId() === scope.id) {
      this.closeEditor();
      return;
    }
    this.editingId.set(scope.id);
    this.draft.set(scope.storeIds.map((id) => this.stores().find((s) => s.id === id)).filter((s): s is StoreOption => !!s));
    this.keptIds.set(scope.storeIds.filter((id) => !this.stores().some((s) => s.id === id)));
    this.confirmingDeleteId.set(null);
    this.act.clear();
  }

  protected closeEditor(): void {
    this.editingId.set(null);
    this.draft.set([]);
    this.keptIds.set([]);
  }

  protected async save(): Promise<void> {
    const scope = this.editing();
    if (!scope) {
      return;
    }
    const ok = await this.act.run(async () => {
      await this.api.updateScope(scope.id, this.draftIds());
      this.scopes.set(await this.api.scopes());
    }, `Die Filialen von „${scope.name}“ wurden gespeichert. Es gilt sofort für alle, die diese Gruppe haben.`);
    if (ok) {
      this.closeEditor();
    }
  }

  protected askDelete(scope: StoreGroup): void {
    this.act.clear();
    this.confirmingDeleteId.set(scope.id);
  }

  protected cancelDelete(): void {
    this.confirmingDeleteId.set(null);
  }

  protected async remove(scope: StoreGroup): Promise<void> {
    const ok = await this.act.run(async () => {
      await this.api.deleteScope(scope.id);
      this.scopes.set(await this.api.scopes());
    }, `Die Filial-Gruppe „${scope.name}“ wurde gelöscht.`);
    if (ok) {
      this.confirmingDeleteId.set(null);
      this.closeEditor();
    }
  }

  protected openCreate(): void {
    this.creating.set(true);
    this.act.clear();
  }

  protected closeCreate(): void {
    this.creating.set(false);
    this.newName.set('');
    this.newStores.set([]);
    this.nameError.set(null);
  }

  protected onName(event: Event): void {
    this.newName.set((event.target as HTMLInputElement).value);
    this.nameError.set(null);
  }

  protected async create(): Promise<void> {
    const name = this.newName().trim();
    if (!name) {
      this.nameError.set('Bitte einen Namen für die Gruppe eingeben.');
      return;
    }
    if (this.scopes().some((s) => s.name.toLowerCase() === name.toLowerCase())) {
      this.nameError.set('Eine Gruppe mit diesem Namen gibt es schon.');
      return;
    }
    const ok = await this.act.run(async () => {
      await this.api.createScope(name, this.newStores().map((s) => s.id));
      this.scopes.set(await this.api.scopes());
    }, `Die Filial-Gruppe „${name}“ wurde angelegt.`);
    if (ok) {
      this.closeCreate();
    }
  }
}
