import { NgTemplateOutlet } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { AuthService } from '../../core/auth/auth.service';
import { AdminAccessApi } from './admin-access.api';
import { CatalogEntry, Role, accessProblemMessage, canChangeRole } from './admin-access-models';
import { ADMIN_UI, ActionMessages, ActionState } from './admin-ui';
import {
  CellState,
  Drafts,
  MatrixRow,
  cellState,
  cellTitle,
  changeCount,
  changedRoles,
  coveringPattern,
  draftsOf,
  isToggleable,
  matrixRows,
  toggled,
} from './role-matrix-models';

/**
 * "Matrix": which role has which privilege, all roles side by side. A click adds or takes away a privilege of one role; nothing is saved
 * before "Speichern" (one call per changed role). A privilege that a role only has through a collective privilege (`*`, `bereich:*`) is
 * shown as covered and cannot be clicked: take the collective privilege away (or give single ones instead) to change it.
 * The administrator role is fixed. Without the privilege to change roles the matrix only shows.
 */
@Component({
  selector: 'app-matrix-section',
  imports: [...ADMIN_UI, ActionMessages, NgTemplateOutlet],
  templateUrl: './matrix-section.html',
})
export class MatrixSection {
  private readonly api = inject(AdminAccessApi);
  private readonly auth = inject(AuthService);

  protected readonly act = new ActionState();
  protected readonly roles = signal<Role[]>([]);
  protected readonly catalog = signal<CatalogEntry[]>([]);
  protected readonly drafts = signal<Drafts>({});
  protected readonly loaded = signal(false);
  protected readonly loadError = signal<string | null>(null);
  protected readonly query = signal('');
  protected readonly confirming = signal(false);

  protected readonly canUpdate = computed(() => this.auth.privileges().has('access:role:update'));
  protected readonly rows = computed(() => matrixRows(this.catalog(), this.query()));
  protected readonly changed = computed(() => changedRoles(this.roles(), this.drafts()));
  protected readonly count = computed(() => changeCount(this.roles(), this.drafts()));

  constructor() {
    void this.load();
  }

  protected async load(): Promise<void> {
    try {
      const [roles, catalog] = await Promise.all([this.api.roles(), this.api.catalog()]);
      this.roles.set([...roles].sort((a, b) => Number(b.builtIn) - Number(a.builtIn) || a.name.localeCompare(b.name, 'de')));
      this.catalog.set(catalog);
      this.drafts.set(draftsOf(roles));
      this.loadError.set(null);
    } catch {
      this.loadError.set('Die Rollen und Rechte konnten nicht geladen werden. Für die Matrix braucht es das Recht, den Katalog zu lesen.');
    } finally {
      this.loaded.set(true);
    }
  }

  protected changeable(role: Role): boolean {
    return this.canUpdate() && canChangeRole(role);
  }

  protected state(role: Role, key: string): CellState {
    return cellState(this.drafts()[role.id] ?? role.privileges, key);
  }

  protected toggleable(role: Role, key: string): boolean {
    return isToggleable(this.state(role, key), this.changeable(role));
  }

  protected title(role: Role, row: MatrixRow): string {
    const privileges = this.drafts()[role.id] ?? role.privileges;
    return cellTitle(role, row, this.state(role, row.key), coveringPattern(privileges, row.key), this.changeable(role));
  }

  /** Whether the cell differs from what the service holds (marked in the matrix). */
  protected dirty(role: Role, key: string): boolean {
    return (this.drafts()[role.id] ?? role.privileges).includes(key) !== role.privileges.includes(key);
  }

  /** What was added and taken away in a role's draft (for the question before saving). */
  protected diff(role: Role): { added: string[]; removed: string[] } {
    const now = this.drafts()[role.id] ?? role.privileges;
    return { added: now.filter((p) => !role.privileges.includes(p)), removed: role.privileges.filter((p) => !now.includes(p)) };
  }

  protected toggle(role: Role, key: string): void {
    if (!this.toggleable(role, key)) {
      return;
    }
    this.act.clear();
    this.confirming.set(false);
    this.drafts.update((d) => ({ ...d, [role.id]: toggled(d[role.id] ?? role.privileges, key) }));
  }

  protected onQuery(event: Event): void {
    this.query.set((event.target as HTMLInputElement).value);
  }

  protected discard(): void {
    this.drafts.set(draftsOf(this.roles()));
    this.confirming.set(false);
    this.act.clear();
  }

  protected askSave(): void {
    this.act.clear();
    this.confirming.set(true);
  }

  protected cancelSave(): void {
    this.confirming.set(false);
  }

  /** One call per changed role; stops at the first refusal and says which role it was, the rest stays as a draft. */
  protected async save(): Promise<void> {
    this.confirming.set(false);
    const todo = this.changed();
    let saved = 0;
    this.act.busy.set(true);
    this.act.clear();
    try {
      for (const role of todo) {
        try {
          await this.api.updateRole(role.id, this.drafts()[role.id]);
          saved++;
        } catch (error) {
          this.act.error.set(`Die Rolle "${role.name}" konnte nicht gespeichert werden: ${accessProblemMessage(error)}${saved ? ` (${saved} Rolle(n) vorher gespeichert)` : ''}`);
          return;
        }
      }
      this.act.status.set(todo.length === 1 ? 'Die Rolle wurde gespeichert.' : `${todo.length} Rollen wurden gespeichert.`);
    } finally {
      this.act.busy.set(false);
      if (saved > 0) {
        const keep = this.drafts();
        await this.load();
        // roles that were not saved keep their draft
        this.drafts.update((d) => ({ ...d, ...Object.fromEntries(todo.slice(saved).map((r) => [r.id, keep[r.id]])) }));
      }
    }
  }
}
