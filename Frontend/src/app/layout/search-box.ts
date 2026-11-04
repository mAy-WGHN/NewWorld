import { Component, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { AuthService } from '../core/auth/auth.service';
import { NavItem, visibleItems } from '../core/nav/nav-items';
import { searchAreas } from '../core/nav/search';
import { IconComponent } from '../shared/icon';

/** "Was möchtest du erledigen?": finds the areas the person may open, by name or by what people call them. */
@Component({
  selector: 'app-search-box',
  imports: [IconComponent],
  templateUrl: './search-box.html',
  styleUrl: './search-box.scss',
})
export class SearchBox {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  protected readonly query = signal('');
  protected readonly active = signal(0);
  protected readonly open = signal(false);

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

  protected onInput(event: Event): void {
    this.query.set((event.target as HTMLInputElement).value);
    this.active.set(0);
    this.open.set(true);
  }

  protected onKey(event: KeyboardEvent): void {
    const count = this.results().length;
    switch (event.key) {
      case 'ArrowDown':
        event.preventDefault();
        this.active.set(count ? (this.active() + 1) % count : 0);
        break;
      case 'ArrowUp':
        event.preventDefault();
        this.active.set(count ? (this.active() - 1 + count) % count : 0);
        break;
      case 'Enter': {
        const chosen = this.results()[this.active()];
        if (chosen) {
          event.preventDefault();
          void this.go(chosen);
        }
        break;
      }
      case 'Escape':
        this.reset();
        break;
    }
  }

  protected async go(item: NavItem): Promise<void> {
    this.reset();
    await this.router.navigateByUrl('/' + item.path);
  }

  protected close(): void {
    // a click on a result must still arrive, so the list closes a moment later
    setTimeout(() => this.open.set(false), 120);
  }

  private reset(): void {
    this.query.set('');
    this.active.set(0);
    this.open.set(false);
  }
}
