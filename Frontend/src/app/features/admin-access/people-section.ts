import { Component, computed, inject, signal } from '@angular/core';
import { AuthService } from '../../core/auth/auth.service';
import { Person } from './admin-access-models';
import { ADMIN_UI } from './admin-ui';
import { AssignmentsPanel } from './assignments-panel';
import { MainStorePanel } from './main-store-panel';
import { PersonPicker } from './person-picker';
import { ResponsibilitiesPanel } from './responsibilities-panel';

/**
 * "Personen": roles (assignments), responsibilities and main store of one person, found by name or personnel number.
 * Which panels show follows the privileges; the services check them again, and the search only offers people in the person's reach.
 */
@Component({
  selector: 'app-people-section',
  imports: [...ADMIN_UI, PersonPicker, AssignmentsPanel, ResponsibilitiesPanel, MainStorePanel],
  templateUrl: './people-section.html',
})
export class PeopleSection {
  private readonly auth = inject(AuthService);

  protected readonly personId = signal<string | null>(null);

  protected readonly showAssignments = computed(() => this.auth.privileges().has('access:assignment:read'));
  protected readonly showResponsibilities = computed(() => this.auth.privileges().has('access:responsibility:read'));
  protected readonly showMainStore = computed(() => this.auth.privileges().hasAny(['access:person:read', 'access:main-store:update']));

  protected onPicked(person: Person | null): void {
    this.personId.set(person?.employeeId ?? null);
  }
}
