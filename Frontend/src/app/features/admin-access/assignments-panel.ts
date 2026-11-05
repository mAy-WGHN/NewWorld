import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { MatCheckboxChange } from '@angular/material/checkbox';
import { AuthService } from '../../core/auth/auth.service';
import { AdminAccessApi } from './admin-access.api';
import { Assignment, Role, StoreGroup, dayAfter, dayStart, periodText, validatePeriod } from './admin-access-models';
import { ADMIN_UI, ActionMessages, ActionState } from './admin-ui';

/**
 * The roles of one person ("Zuweisungen"): which role, where (everywhere or only the chosen groups and, if wanted, the home store) and
 * for how long. Assignments from BC data are shown but only "Manual" ones are made here. The service checks that nobody grants more than
 * they have themselves.
 */
@Component({
  selector: 'app-assignments-panel',
  imports: [...ADMIN_UI, ActionMessages],
  templateUrl: './assignments-panel.html',
})
export class AssignmentsPanel {
  private readonly api = inject(AdminAccessApi);
  private readonly auth = inject(AuthService);

  readonly personId = input.required<string>();

  protected readonly act = new ActionState();
  protected readonly assignments = signal<Assignment[]>([]);
  protected readonly roles = signal<Role[]>([]);
  protected readonly scopes = signal<StoreGroup[]>([]);
  protected readonly loaded = signal(false);
  protected readonly loadError = signal<string | null>(null);
  protected readonly lookupsMissing = signal(false);

  protected readonly canCreate = computed(() => this.auth.privileges().has('access:assignment:create'));
  protected readonly canDelete = computed(() => this.auth.privileges().has('access:assignment:delete'));

  protected readonly roleId = signal('');
  protected readonly reach = signal<'unrestricted' | 'scopes'>('unrestricted');
  protected readonly scopeIds = signal<string[]>([]);
  protected readonly includeHome = signal(false);
  protected readonly from = signal('');
  protected readonly to = signal('');
  protected readonly formError = signal<string | null>(null);
  protected readonly confirmingDeleteId = signal<string | null>(null);

  protected readonly periodText = periodText;

  constructor() {
    effect(() => {
      const id = this.personId();
      untracked(() => void this.load(id));
    });
  }

  protected roleName(id: string): string {
    return this.roles().find((r) => r.id === id)?.name ?? id;
  }

  protected reachText(a: Assignment): string {
    if (a.unrestricted) {
      return 'Überall (uneingeschränkt)';
    }
    const names = a.scopeIds.map((id) => this.scopes().find((s) => s.id === id)?.name ?? id);
    if (a.includeHomeStore) {
      names.push('Hauptfiliale');
    }
    return names.length ? names.join(', ') : 'Keine Filialen';
  }

  protected sourceText(source: string): string {
    return source === 'Manual' ? 'Von Hand vergeben' : source === 'Automatic' ? 'Automatisch' : source;
  }

  private async load(id: string): Promise<void> {
    this.loaded.set(false);
    this.confirmingDeleteId.set(null);
    this.act.clear();
    try {
      this.assignments.set(await this.api.assignments(id));
      this.loadError.set(null);
    } catch (error) {
      this.assignments.set([]);
      this.loadError.set(
        error instanceof HttpErrorResponse && error.status === 403
          ? 'Du darfst die Zuweisungen dieser Person nicht sehen.'
          : 'Die Zuweisungen konnten nicht geladen werden.',
      );
    } finally {
      this.loaded.set(true);
    }
    const [roles, scopes] = await Promise.allSettled([this.api.roles(), this.api.scopes()]);
    this.roles.set(roles.status === 'fulfilled' ? roles.value : []);
    this.scopes.set(scopes.status === 'fulfilled' ? scopes.value : []);
    this.lookupsMissing.set(roles.status === 'rejected' || scopes.status === 'rejected');
  }

  protected onRole(event: Event): void {
    this.roleId.set((event.target as HTMLSelectElement).value);
    this.formError.set(null);
  }

  protected onReach(value: 'unrestricted' | 'scopes'): void {
    this.reach.set(value);
    this.formError.set(null);
  }

  protected toggleScope(id: string, event: MatCheckboxChange): void {
    this.scopeIds.update((list) => (event.checked ? [...list, id] : list.filter((s) => s !== id)));
    this.formError.set(null);
  }

  protected onIncludeHome(event: MatCheckboxChange): void {
    this.includeHome.set(event.checked);
    this.formError.set(null);
  }

  protected onFrom(event: Event): void {
    this.from.set((event.target as HTMLInputElement).value);
    this.formError.set(null);
  }

  protected onTo(event: Event): void {
    this.to.set((event.target as HTMLInputElement).value);
    this.formError.set(null);
  }

  /** What is wrong with the form, or null. */
  protected validate(): string | null {
    if (!this.roleId()) {
      return 'Bitte eine Rolle wählen.';
    }
    if (this.reach() === 'scopes' && this.scopeIds().length === 0 && !this.includeHome()) {
      return 'Bitte mindestens eine Filial-Gruppe oder die Hauptfiliale wählen, oder „Überall“.';
    }
    return validatePeriod(this.from(), this.to());
  }

  protected async add(): Promise<void> {
    const error = this.validate();
    this.formError.set(error);
    if (error) {
      return;
    }
    const unrestricted = this.reach() === 'unrestricted';
    const personId = this.personId();
    const ok = await this.act.run(async () => {
      await this.api.assign(personId, {
        roleId: this.roleId(),
        unrestricted,
        scopeIds: unrestricted ? null : this.scopeIds(),
        includeHomeStore: unrestricted ? false : this.includeHome(),
        validFrom: dayStart(this.from()),
        validTo: dayAfter(this.to()),
      });
      this.assignments.set(await this.api.assignments(personId));
    }, 'Die Rolle wurde zugewiesen. Es gilt sofort.');
    if (ok) {
      this.roleId.set('');
      this.scopeIds.set([]);
      this.includeHome.set(false);
      this.from.set('');
      this.to.set('');
      this.reach.set('unrestricted');
    }
  }

  protected askDelete(id: string): void {
    this.act.clear();
    this.confirmingDeleteId.set(id);
  }

  protected cancelDelete(): void {
    this.confirmingDeleteId.set(null);
  }

  protected async remove(a: Assignment): Promise<void> {
    const personId = this.personId();
    const ok = await this.act.run(async () => {
      await this.api.removeAssignment(a.id);
      this.assignments.set(await this.api.assignments(personId));
    }, 'Die Zuweisung wurde entzogen.');
    if (ok) {
      this.confirmingDeleteId.set(null);
    }
  }
}
