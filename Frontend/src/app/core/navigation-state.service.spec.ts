import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { NavigationState, PageReloader } from './navigation-state.service';

@Component({ template: 'ok' })
class Dummy {}

describe('NavigationState', () => {
  let reloader: { reloadOnce: ReturnType<typeof vi.fn> };
  let router: Router;
  let state: NavigationState;

  function setup(routes: Parameters<typeof provideRouter>[0]): void {
    reloader = { reloadOnce: vi.fn().mockReturnValue(false) };
    TestBed.configureTestingModule({ providers: [provideRouter(routes), { provide: PageReloader, useValue: reloader }] });
    router = TestBed.inject(Router);
    state = TestBed.inject(NavigationState);
  }

  const settle = () => new Promise((r) => setTimeout(r));

  it('has no error and is not loading while nothing happens', () => {
    setup([]);

    expect(state.loading()).toBe(false);
    expect(state.error()).toBeNull();
  });

  it('does not flash a loading bar for a quick page', async () => {
    setup([{ path: 'quick', component: Dummy }]);

    await router.navigateByUrl('/quick');

    expect(state.loading()).toBe(false);
    expect(state.error()).toBeNull();
  });

  it('shows the loading bar when a page takes long to load, and removes it afterwards', async () => {
    setup([{ path: 'slow', loadComponent: () => new Promise<typeof Dummy>((resolve) => setTimeout(() => resolve(Dummy), 450)) }]);

    const done = router.navigateByUrl('/slow');
    await new Promise((r) => setTimeout(r, 300));
    expect(state.loading()).toBe(true);

    await done;
    expect(state.loading()).toBe(false);
  });

  it('tells the person when a page cannot be loaded and keeps the reason', async () => {
    setup([{ path: 'broken', loadComponent: () => Promise.reject(new Error('Something broke')) }]);

    await router.navigateByUrl('/broken').catch(() => undefined);
    await settle();

    expect(state.error()).toContain('konnte nicht geladen werden');
    expect(state.errorDetail()).toContain('Something broke');
    expect(state.loading()).toBe(false);
    expect(reloader.reloadOnce).not.toHaveBeenCalled();
  });

  it('reloads the page once if a part of the app cannot be fetched (stale build, broken connection)', async () => {
    setup([{ path: 'chunk', loadComponent: () => Promise.reject(new Error('Failed to fetch dynamically imported module: http://x/chunk.js')) }]);

    await router.navigateByUrl('/chunk').catch(() => undefined);
    await settle();

    expect(reloader.reloadOnce).toHaveBeenCalledWith('/chunk');
    expect(state.error()).toContain('konnte nicht geladen werden');
  });

  it('forgets the error with the next navigation', async () => {
    setup([
      { path: 'broken', loadComponent: () => Promise.reject(new Error('Something broke')) },
      { path: 'fine', component: Dummy },
    ]);
    await router.navigateByUrl('/broken').catch(() => undefined);
    await settle();
    expect(state.error()).not.toBeNull();

    await router.navigateByUrl('/fine');

    expect(state.error()).toBeNull();
    expect(state.errorDetail()).toBeNull();
  });
});

describe('PageReloader', () => {
  beforeEach(() => sessionStorage.clear());

  it('reloads the address once and then leaves it alone, so a real problem does not loop', () => {
    const assign = vi.fn();
    const reloader = new PageReloader(assign);

    expect(reloader.reloadOnce('/my-vacation')).toBe(true);
    expect(assign).toHaveBeenCalledWith('/my-vacation');

    expect(reloader.reloadOnce('/my-vacation')).toBe(false);
    expect(assign).toHaveBeenCalledTimes(1);
  });

  it('may reload another address once more', () => {
    const assign = vi.fn();
    const reloader = new PageReloader(assign);

    reloader.reloadOnce('/a');
    expect(reloader.reloadOnce('/b')).toBe(true);
  });
});
