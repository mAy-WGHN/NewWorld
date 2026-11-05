import { Component, computed, inject, output, signal } from '@angular/core';
import { AuthService } from '../../core/auth/auth.service';
import { AdminAccessApi } from './admin-access.api';
import { RoleDefinitionFile, RoleImportItem, RoleImportResult, importErrorText, parseRoleFile } from './admin-access-models';
import { ADMIN_UI, ActionMessages, ActionState } from './admin-ui';

/**
 * Roles as a file: "Exportieren" saves the roles and their privileges of this system as JSON; "Datei vergleichen" shows what a file would change
 * (a diff against the existing roles, nothing is changed yet) and applies only the ticked items. The file holds no people, assignments or store groups,
 * so a file made locally can be imported in production. Roles that are not in the file stay as they are.
 */
@Component({
  selector: 'app-role-transfer',
  imports: [...ADMIN_UI, ActionMessages],
  templateUrl: './role-transfer.html',
})
export class RoleTransfer {
  private readonly api = inject(AdminAccessApi);
  private readonly auth = inject(AuthService);

  /** Told when roles were changed by an import, so the list can reload. */
  readonly imported = output<void>();

  protected readonly act = new ActionState();
  protected readonly canExport = computed(() => this.auth.privileges().has('access:role:read'));
  protected readonly canImport = computed(() => this.auth.privileges().has('access:role:update'));
  protected readonly fileName = signal<string | null>(null);
  protected readonly fileError = signal<string | null>(null);
  private file: RoleDefinitionFile | null = null;
  protected readonly result = signal<RoleImportResult | null>(null);
  protected readonly selected = signal<ReadonlySet<string>>(new Set());

  /** What differs, i.e. what can be applied. */
  protected readonly changes = computed(() => (this.result()?.items ?? []).filter((i) => i.action === 'create' || i.action === 'update'));
  protected readonly unchangedCount = computed(() => (this.result()?.items ?? []).filter((i) => i.action === 'unchanged').length);
  protected readonly errors = computed(() => (this.result()?.items ?? []).filter((i) => i.action === 'error'));

  protected async export(): Promise<void> {
    await this.act.run(async () => {
      const file = await this.api.roleDefinitions();
      const url = URL.createObjectURL(new Blob([JSON.stringify(file, null, 2)], { type: 'application/json' }));
      const link = document.createElement('a');
      link.href = url;
      link.download = 'rollen.json';
      link.click();
      URL.revokeObjectURL(url);
    }, 'Die Rollen wurden als „rollen.json“ gespeichert.');
  }

  protected async choose(event: Event): Promise<void> {
    const input = event.target as HTMLInputElement;
    const chosen = input.files?.[0];
    input.value = '';
    this.reset();
    if (!chosen) {
      return;
    }
    const parsed = parseRoleFile(await chosen.text());
    if ('error' in parsed) {
      this.fileError.set(parsed.error);
      return;
    }
    this.file = parsed.file;
    this.fileName.set(chosen.name);
    const ok = await this.act.run(async () => {
      const result = await this.api.importRoles(parsed.file, false, []);
      this.result.set(result);
      this.selected.set(new Set(result.items.filter((i) => i.action === 'create' || i.action === 'update').map((i) => i.name)));
    }, null);
    if (!ok) {
      this.reset();
    }
  }

  protected toggle(item: RoleImportItem): void {
    this.selected.update((current) => {
      const next = new Set(current);
      if (!next.delete(item.name)) {
        next.add(item.name);
      }
      return next;
    });
  }

  protected isSelected(item: RoleImportItem): boolean {
    return this.selected().has(item.name);
  }

  protected async apply(): Promise<void> {
    const file = this.file;
    if (!file || this.selected().size === 0) {
      return;
    }
    const names = [...this.selected()];
    const ok = await this.act.run(async () => {
      const result = await this.api.importRoles(file, true, names);
      if (!result.applied) {
        this.result.set(result);
        throw new Error('not applied');
      }
      this.imported.emit();
    }, `${names.length} ${names.length === 1 ? 'Änderung wurde' : 'Änderungen wurden'} übernommen. Es gilt sofort für alle, die die Rollen haben.`);
    if (ok) {
      this.reset();
    }
  }

  protected cancel(): void {
    this.reset();
    this.act.clear();
  }

  protected errorText = importErrorText;

  private reset(): void {
    this.file = null;
    this.fileName.set(null);
    this.fileError.set(null);
    this.result.set(null);
    this.selected.set(new Set());
  }
}
