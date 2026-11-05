import { Component, computed, inject, signal } from '@angular/core';
import { AuthService } from '../../core/auth/auth.service';
import { isGuid } from './admin-access-models';
import { ADMIN_UI } from './admin-ui';
import { AssignmentsPanel } from './assignments-panel';
import { MainStorePanel } from './main-store-panel';
import { ResponsibilitiesPanel } from './responsibilities-panel';

/**
 * "Personen": roles (assignments), responsibilities and main store of one person.
 * TODO(Backend): Personensuche. Access and Workforce have no call that lists or searches people, so the person is entered by ID (a GUID).
 * Which panels show follows the privileges; the services check them again.
 */
@Component({
  selector: 'app-people-section',
  imports: [...ADMIN_UI, AssignmentsPanel, ResponsibilitiesPanel, MainStorePanel],
  templateUrl: './people-section.html',
})
export class PeopleSection {
  private readonly auth = inject(AuthService);

  protected readonly input = signal('');
  protected readonly personId = signal<string | null>(null);
  protected readonly error = signal<string | null>(null);

  protected readonly showAssignments = computed(() => this.auth.privileges().has('access:assignment:read'));
  protected readonly showResponsibilities = computed(() => this.auth.privileges().has('access:responsibility:read'));
  protected readonly showMainStore = computed(() => this.auth.privileges().hasAny(['access:person:read', 'access:main-store:update']));

  protected onInput(event: Event): void {
    this.input.set((event.target as HTMLInputElement).value);
    this.error.set(null);
  }

  protected open(): void {
    const id = this.input().trim().toLowerCase();
    if (!isGuid(id)) {
      this.error.set('Bitte eine gültige Personen-ID eingeben (eine GUID, zum Beispiel 3f2504e0-4f89-11d3-9a0c-0305e82c3301).');
      this.personId.set(null);
      return;
    }
    this.error.set(null);
    this.personId.set(id);
  }
}
