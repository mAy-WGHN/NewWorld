import { Component, computed, input, model, signal } from '@angular/core';
import { IconComponent } from '../../shared/icon';
import { Location, filterLocations, locationLabel } from './report-issue-models';

/** Choose one or more stores: search by name or number, the chosen ones stand as chips. */
@Component({
  selector: 'app-store-picker',
  imports: [IconComponent],
  templateUrl: './store-picker.html',
  styleUrl: './store-picker.scss',
})
export class StorePicker {
  readonly locations = input<readonly Location[]>([]);
  readonly selected = model<Location[]>([]);
  /** What is missing, shown below the field. */
  readonly error = input<string | null>(null);
  readonly inputId = input('store-search');

  protected readonly query = signal('');
  protected readonly open = signal(false);
  protected readonly active = signal(0);

  protected readonly matches = computed(() =>
    filterLocations(
      this.locations(),
      this.query(),
      this.selected().map((s) => s.id),
    ),
  );

  protected readonly label = locationLabel;

  protected onInput(event: Event): void {
    this.query.set((event.target as HTMLInputElement).value);
    this.active.set(0);
    this.open.set(true);
  }

  protected onKey(event: KeyboardEvent): void {
    const count = this.matches().length;
    switch (event.key) {
      case 'ArrowDown':
        event.preventDefault();
        this.open.set(true);
        this.active.set(count ? (this.active() + 1) % count : 0);
        break;
      case 'ArrowUp':
        event.preventDefault();
        this.active.set(count ? (this.active() - 1 + count) % count : 0);
        break;
      case 'Enter': {
        event.preventDefault(); // Enter must not send the form
        const chosen = this.matches()[this.active()];
        if (this.open() && chosen) {
          this.add(chosen);
        }
        break;
      }
      case 'Escape':
        this.open.set(false);
        break;
    }
  }

  protected add(location: Location): void {
    this.selected.update((list) => [...list, location]);
    this.query.set('');
    this.active.set(0);
    this.open.set(false);
  }

  protected remove(location: Location): void {
    this.selected.update((list) => list.filter((l) => l.id !== location.id));
  }

  protected close(): void {
    // a click on an option must still arrive, so the list closes a moment later
    setTimeout(() => this.open.set(false), 120);
  }
}
