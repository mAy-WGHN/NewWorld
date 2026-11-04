import { Injectable, signal } from '@angular/core';

const KEY = 'nw.favorites';

/** The areas a person pinned. Kept in the browser only (a convenience per device, not shared). */
@Injectable({ providedIn: 'root' })
export class FavoritesService {
  readonly paths = signal<string[]>(this.read());

  has(path: string): boolean {
    return this.paths().includes(path);
  }

  toggle(path: string): void {
    this.paths.update((list) => (list.includes(path) ? list.filter((p) => p !== path) : [...list, path]));
    this.write(this.paths());
  }

  private read(): string[] {
    try {
      const value: unknown = JSON.parse(localStorage.getItem(KEY) ?? '[]');
      return Array.isArray(value) ? value.filter((v): v is string => typeof v === 'string') : [];
    } catch {
      return [];
    }
  }

  private write(list: string[]): void {
    try {
      localStorage.setItem(KEY, JSON.stringify(list));
    } catch {
      // storage blocked: the choice then lasts until the page is closed
    }
  }
}
