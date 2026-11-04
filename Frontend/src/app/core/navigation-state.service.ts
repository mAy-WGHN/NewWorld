import { Injectable, inject, signal } from '@angular/core';
import { NavigationCancel, NavigationEnd, NavigationError, NavigationStart, Router } from '@angular/router';

const RELOAD_KEY = 'nw.chunk-reload';

/** True if a part of the app (a lazily loaded page) could not be fetched: an old build in the browser, or a broken connection. */
const isFetchError = (message: string): boolean => /dynamically imported module|Loading chunk|Importing a module script failed/i.test(message);

/** Reloads the page, but each address only once: a real problem must not turn into an endless loop. */
@Injectable({ providedIn: 'root', useFactory: () => new PageReloader() })
export class PageReloader {
  /** `assign` and `reload` are replaceable for tests. */
  constructor(
    private readonly assign: (url: string) => void = (url) => location.assign(url),
    private readonly reload: () => void = () => location.reload(),
  ) {}

  /** Reloads the current page now (the button of the error message). */
  reloadNow(): void {
    this.reload();
  }

  reloadOnce(url: string): boolean {
    try {
      if (sessionStorage.getItem(RELOAD_KEY) === url) {
        return false;
      }
      sessionStorage.setItem(RELOAD_KEY, url);
    } catch {
      return false;
    }
    this.assign(url);
    return true;
  }
}

/**
 * What the router is doing, for the person: a slow page shows a loading bar (after a short delay, so quick pages do not flash), and a page that
 * cannot be loaded says so instead of silently leaving the previous page on the screen.
 */
@Injectable({ providedIn: 'root' })
export class NavigationState {
  private readonly reloader = inject(PageReloader);
  private timer: ReturnType<typeof setTimeout> | undefined;

  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  /** The technical reason, for whoever helps to find the problem. */
  readonly errorDetail = signal<string | null>(null);

  constructor() {
    inject(Router).events.subscribe((event) => {
      if (event instanceof NavigationStart) {
        this.error.set(null);
        this.errorDetail.set(null);
        this.stopTimer();
        this.timer = setTimeout(() => this.loading.set(true), 250);
      } else if (event instanceof NavigationEnd || event instanceof NavigationCancel) {
        this.finish();
      } else if (event instanceof NavigationError) {
        this.finish();
        const message = event.error instanceof Error ? event.error.message : String(event.error);
        this.error.set('Diese Seite konnte nicht geladen werden.');
        this.errorDetail.set(message);
        if (isFetchError(message)) {
          this.reloader.reloadOnce(event.url);
        }
      }
    });
  }

  private finish(): void {
    this.stopTimer();
    this.loading.set(false);
  }

  private stopTimer(): void {
    if (this.timer !== undefined) {
      clearTimeout(this.timer);
      this.timer = undefined;
    }
  }
}
