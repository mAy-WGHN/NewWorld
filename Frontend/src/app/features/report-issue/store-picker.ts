import { Component, computed, input, model, signal } from '@angular/core';
import { MatAutocompleteModule, MatAutocompleteSelectedEvent } from '@angular/material/autocomplete';
import { MatChipsModule } from '@angular/material/chips';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { Location, filterLocations, locationLabel } from './report-issue-models';

/** Choose one or more stores: search by name or number, the chosen ones stand as chips. */
@Component({
  selector: 'app-store-picker',
  imports: [MatAutocompleteModule, MatChipsModule, MatFormFieldModule, MatIconModule],
  templateUrl: './store-picker.html',
})
export class StorePicker {
  readonly locations = input<readonly Location[]>([]);
  readonly selected = model<Location[]>([]);
  /** What is missing, shown below the field. */
  readonly error = input<string | null>(null);
  readonly inputId = input('store-search');

  protected readonly query = signal('');

  protected readonly matches = computed(() =>
    filterLocations(
      this.locations(),
      this.query(),
      this.selected().map((s) => s.id),
    ),
  );

  protected readonly label = locationLabel;

  /** The field stays empty after a choice (the options are objects, they must not be written into it). */
  protected readonly display = (): string => '';

  protected onInput(event: Event): void {
    this.query.set((event.target as HTMLInputElement).value);
  }

  protected add(event: MatAutocompleteSelectedEvent): void {
    this.selected.update((list) => [...list, event.option.value as Location]);
    this.query.set('');
  }

  protected remove(location: Location): void {
    this.selected.update((list) => list.filter((l) => l.id !== location.id));
  }
}
