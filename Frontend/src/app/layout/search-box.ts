import { Component, computed, inject, signal } from '@angular/core';
import { MatAutocompleteModule, MatAutocompleteSelectedEvent } from '@angular/material/autocomplete';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { Router } from '@angular/router';
import { AuthService } from '../core/auth/auth.service';
import { NavItem, visibleItems } from '../core/nav/nav-items';
import { searchAreas } from '../core/nav/search';

/** "Was möchtest du erledigen?": finds the areas the person may open, by name or by what people call them. */
@Component({
  selector: 'app-search-box',
  imports: [MatAutocompleteModule, MatFormFieldModule, MatIconModule, MatInputModule],
  templateUrl: './search-box.html',
})
export class SearchBox {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  protected readonly query = signal('');

  private readonly items = computed(() => visibleItems(this.auth.privileges()));
  protected readonly results = computed(() => searchAreas(this.query(), this.items()).slice(0, 6));

  /** A few examples from the areas this person really has. */
  protected readonly examples = computed(() => {
    const labels = this.items()
      .filter((i) => i.keywords?.length)
      .slice(0, 3)
      .map((i) => i.label);
    return labels.length ? `z. B. ${labels.join(', ')} …` : '';
  });

  /** The field stays empty after a choice (the options are objects, they must not be written into it). */
  protected readonly display = (): string => '';

  protected onInput(event: Event): void {
    this.query.set((event.target as HTMLInputElement).value);
  }

  protected async choose(event: MatAutocompleteSelectedEvent): Promise<void> {
    const item = event.option.value as NavItem;
    this.query.set('');
    await this.router.navigateByUrl('/' + item.path);
  }
}
