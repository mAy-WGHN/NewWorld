import { Component, inject, input, output } from '@angular/core';
import { PersonSource, WghnPersonPicker } from '../../shared/wghn/wghn-person-picker';
import { AdminAccessApi } from './admin-access.api';
import { Person } from './admin-access-models';

/**
 * Choose a person of the Access service by name or personnel number. The picker itself is the library's `wghn-person-picker`; this brings the
 * people the Access service offers (only those in the reach of the signed-in person).
 */
@Component({
  selector: 'app-person-picker',
  imports: [WghnPersonPicker],
  template: `
    <wghn-person-picker
      [label]="label()"
      [inputId]="inputId()"
      [selectedId]="selectedId()"
      [disabled]="disabled()"
      [debounceMs]="debounceMs()"
      [source]="source"
      (picked)="picked.emit($event ? asPerson($event) : null)"
    />
  `,
})
export class PersonPicker {
  private readonly api = inject(AdminAccessApi);

  readonly label = input.required<string>();
  readonly inputId = input.required<string>();
  readonly selectedId = input<string | null>(null);
  readonly disabled = input(false);
  /** Wait after the last key before asking the service. */
  readonly debounceMs = input(300);
  readonly picked = output<Person | null>();

  protected readonly source: PersonSource = {
    search: (query) => this.api.searchPeople(query),
    byIds: (ids) => this.api.peopleByIds(ids),
  };

  /** What the picker hands back is what the Access service gave. */
  protected asPerson(person: unknown): Person {
    return person as Person;
  }
}
