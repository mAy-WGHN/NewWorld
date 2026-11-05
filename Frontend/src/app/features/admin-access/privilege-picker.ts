import { Component, computed, input, model, signal } from '@angular/core';
import { MatCheckboxChange } from '@angular/material/checkbox';
import { CatalogEntry, grantedBy, groupCatalog, isKnownPattern, wildcardsOf } from './admin-access-models';
import { ADMIN_UI } from './admin-ui';

/**
 * Chooses the privileges of a role from the catalogue: grouped by area, with the plain-German description of each privilege and a search.
 * Wildcards (`*` for everything, `area:*` for a whole area) are offered separately; chosen entries that are not in the catalogue (they have
 * no effect and the service would refuse them) are listed so they can be removed.
 */
@Component({
  selector: 'app-privilege-picker',
  imports: [...ADMIN_UI],
  templateUrl: './privilege-picker.html',
})
export class PrivilegePicker {
  readonly catalog = input.required<readonly CatalogEntry[]>();
  readonly selected = model<string[]>([]);
  readonly disabled = input(false);

  protected readonly query = signal('');

  private readonly keys = computed(() => this.catalog().map((e) => e.key));
  protected readonly groups = computed(() => groupCatalog(this.catalog(), this.query()));
  protected readonly wildcards = computed(() => wildcardsOf(this.catalog()));
  protected readonly unknown = computed(() => this.selected().filter((p) => !isKnownPattern(p, this.keys())));

  protected on(pattern: string): boolean {
    return this.selected().includes(pattern);
  }

  protected count(pattern: string): number {
    return grantedBy(pattern, this.keys());
  }

  protected wildcardLabel(pattern: string): string {
    return pattern === '*' ? 'Alle Rechte (Administrator)' : `Alle Rechte im Bereich „${pattern.slice(0, -2)}“`;
  }

  protected toggle(pattern: string, event: MatCheckboxChange): void {
    const checked = event.checked;
    this.selected.update((list) => (checked ? (list.includes(pattern) ? list : [...list, pattern]) : list.filter((p) => p !== pattern)));
  }

  protected remove(pattern: string): void {
    this.selected.update((list) => list.filter((p) => p !== pattern));
  }

  protected onQuery(event: Event): void {
    this.query.set((event.target as HTMLInputElement).value);
  }
}
