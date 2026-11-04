import { Injectable, signal } from '@angular/core';

export type ThemeMode = 'system' | 'light' | 'dark';

const KEY = 'nw.theme';
const SCHEMES: Record<ThemeMode, string> = { system: 'light dark', light: 'light', dark: 'dark' };

/** Light, dark or "like the device". Kept in the browser only (a convenience per device); the Material theme follows `color-scheme`. */
@Injectable({ providedIn: 'root' })
export class ThemeService {
  readonly mode = signal<ThemeMode>(this.read());

  constructor() {
    this.apply(this.mode());
  }

  set(mode: ThemeMode): void {
    this.mode.set(mode);
    this.apply(mode);
    try {
      localStorage.setItem(KEY, mode);
    } catch {
      // storage blocked: the choice then lasts until the page is closed
    }
  }

  private apply(mode: ThemeMode): void {
    document.documentElement.style.colorScheme = SCHEMES[mode];
  }

  private read(): ThemeMode {
    try {
      const value = localStorage.getItem(KEY);
      return value === 'light' || value === 'dark' ? value : 'system';
    } catch {
      return 'system';
    }
  }
}
