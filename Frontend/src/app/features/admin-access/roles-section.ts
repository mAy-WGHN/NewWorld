import { Component, computed, inject, signal } from '@angular/core';
import { AuthService } from '../../core/auth/auth.service';
import { AdminAccessApi } from './admin-access.api';
import { CatalogEntry, Role, builtInHint, canChangeRole, canDeleteRole, validateRoleName } from './admin-access-models';
import { ADMIN_UI, ActionMessages, ActionState } from './admin-ui';
import { PrivilegePicker } from './privilege-picker';

/**
 * "Rollen": a role is a named set of privileges. People get roles in "Personen". Built-in roles cannot be deleted; the administrator role
 * cannot be changed either. Whoever changes a role needs every privilege of it themselves (the service checks that).
 */
@Component({
  selector: 'app-roles-section',
  imports: [...ADMIN_UI, ActionMessages, PrivilegePicker],
  templateUrl: './roles-section.html',
})
export class RolesSection {
  private readonly api = inject(AdminAccessApi);
  private readonly auth = inject(AuthService);

  protected readonly act = new ActionState();
  protected readonly roles = signal<Role[]>([]);
  protected readonly catalog = signal<CatalogEntry[]>([]);
  protected readonly loaded = signal(false);
  protected readonly loadError = signal<string | null>(null);
  /** Without the catalogue the privileges cannot be chosen. */
  protected readonly catalogMissing = signal(false);

  protected readonly canCreate = computed(() => this.auth.privileges().has('access:role:create'));
  protected readonly canUpdate = computed(() => this.auth.privileges().has('access:role:update'));
  protected readonly canDelete = computed(() => this.auth.privileges().has('access:role:delete'));

  protected readonly editingId = signal<string | null>(null);
  protected readonly draft = signal<string[]>([]);
  protected readonly confirmingDeleteId = signal<string | null>(null);

  protected readonly creating = signal(false);
  protected readonly newName = signal('');
  protected readonly newPrivileges = signal<string[]>([]);
  protected readonly nameError = signal<string | null>(null);

  protected readonly editing = computed(() => this.roles().find((r) => r.id === this.editingId()) ?? null);
  protected readonly changed = computed(() => {
    const role = this.editing();
    const a = [...this.draft()].sort().join('|');
    return role ? a !== [...role.privileges].sort().join('|') : false;
  });

  protected readonly builtInHint = builtInHint;
  protected readonly canChange = canChangeRole;
  protected readonly canRemove = canDeleteRole;

  constructor() {
    void this.load();
  }

  protected async load(): Promise<void> {
    try {
      this.roles.set(await this.api.roles());
      this.loadError.set(null);
    } catch {
      this.loadError.set('Die Rollen konnten nicht geladen werden.');
    } finally {
      this.loaded.set(true);
    }
    try {
      this.catalog.set(await this.api.catalog());
      this.catalogMissing.set(false);
    } catch {
      this.catalogMissing.set(true);
    }
  }

  protected edit(role: Role): void {
    if (this.editingId() === role.id) {
      this.closeEditor();
      return;
    }
    this.editingId.set(role.id);
    this.draft.set([...role.privileges]);
    this.confirmingDeleteId.set(null);
    this.act.clear();
  }

  protected closeEditor(): void {
    this.editingId.set(null);
    this.draft.set([]);
  }

  protected async save(): Promise<void> {
    const role = this.editing();
    if (!role) {
      return;
    }
    const ok = await this.act.run(async () => {
      await this.api.updateRole(role.id, this.draft());
      await this.reloadRoles();
    }, `Die Rolle „${role.name}“ wurde gespeichert. Es gilt sofort für alle, die sie haben.`);
    if (ok) {
      this.closeEditor();
    }
  }

  protected askDelete(role: Role): void {
    this.act.clear();
    this.confirmingDeleteId.set(role.id);
  }

  protected cancelDelete(): void {
    this.confirmingDeleteId.set(null);
  }

  protected async remove(role: Role): Promise<void> {
    const ok = await this.act.run(async () => {
      await this.api.deleteRole(role.id);
      await this.reloadRoles();
    }, `Die Rolle „${role.name}“ wurde gelöscht.`);
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
    this.newPrivileges.set([]);
    this.nameError.set(null);
  }

  protected onName(event: Event): void {
    this.newName.set((event.target as HTMLInputElement).value);
    this.nameError.set(null);
  }

  protected async create(): Promise<void> {
    const error = validateRoleName(this.newName(), this.roles());
    this.nameError.set(error);
    if (error) {
      return;
    }
    const name = this.newName().trim();
    const ok = await this.act.run(async () => {
      await this.api.createRole(name, this.newPrivileges());
      await this.reloadRoles();
    }, `Die Rolle „${name}“ wurde angelegt. Weise sie unter „Personen“ zu.`);
    if (ok) {
      this.closeCreate();
    }
  }

  private async reloadRoles(): Promise<void> {
    this.roles.set(await this.api.roles());
  }
}
