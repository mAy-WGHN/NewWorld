import { HttpErrorResponse } from '@angular/common/http';
import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ActionState, WghnActionMessages } from './action-state';

@Component({
  selector: 'wghn-action-host',
  imports: [WghnActionMessages],
  template: `<wghn-action-messages [state]="state" />`,
})
class Host {
  readonly state = new ActionState();
}

describe('ActionState', () => {
  it('is busy while the call runs, then says what was done', async () => {
    const state = new ActionState();
    let busyDuring = false;

    const ok = await state.run(async () => {
      busyDuring = state.busy();
    }, 'Gespeichert.');

    expect(ok).toBe(true);
    expect(busyDuring).toBe(true);
    expect(state.busy()).toBe(false);
    expect(state.status()).toBe('Gespeichert.');
    expect(state.error()).toBeNull();
  });

  it('tells a failure in plain German (the answer of the portal services by default) and is free again', async () => {
    const state = new ActionState();

    const ok = await state.run(() => Promise.reject(new HttpErrorResponse({ status: 409, error: { success: false, message: 'Schon vorhanden.', errors: [] } })), 'Fertig.');

    expect(ok).toBe(false);
    expect(state.error()).toBe('Schon vorhanden.');
    expect(state.status()).toBeNull();
    expect(state.busy()).toBe(false);
  });

  it('tells a failure the way the page says (the Access pages bring their own texts)', async () => {
    const state = new ActionState(() => 'Dafür fehlt dir das Recht.');

    await state.run(() => Promise.reject(new Error('x')));

    expect(state.error()).toBe('Dafür fehlt dir das Recht.');
  });

  it('forgets the last message when the next call starts, and clear or reset forget it too', async () => {
    const state = new ActionState();
    await state.run(() => Promise.reject(new Error('x')));
    expect(state.error()).not.toBeNull();

    await state.run(async () => undefined, 'Wieder gut.');
    expect(state.error()).toBeNull();
    state.clear();
    expect(state.status()).toBeNull();
    await state.run(async () => undefined, 'Noch einmal.');
    state.reset();
    expect(state.status()).toBeNull();
  });
});

describe('wghn-action-messages', () => {
  it('shows the error and the success text as alerts with the usual test ids', async () => {
    const fixture = TestBed.createComponent(Host);
    const root = fixture.nativeElement as HTMLElement;
    await fixture.whenStable();
    expect(root.querySelector('wghn-alert')).toBeNull();

    fixture.componentInstance.state.error.set('Es ging schief.');
    fixture.componentInstance.state.status.set('Gespeichert.');
    fixture.detectChanges();

    expect(root.querySelector('[data-testid="error"] p')!.getAttribute('role')).toBe('alert');
    expect(root.querySelector('[data-testid="error"]')!.textContent).toContain('Es ging schief.');
    expect(root.querySelector('[data-testid="status"] p')!.getAttribute('role')).toBe('status');
  });
});
