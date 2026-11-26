import { Component, input, signal } from '@angular/core';
import { WghnAlert } from './wghn-alert';
import { envelopeProblem } from './problem';

/**
 * Busy flag, error text and success text of one part of a page (a section, a form, a row). `run` does the call and turns a failure into a plain German
 * message; the way a failure is told depends on the service (`describe`: by default the answer of the portal services, the Access pages bring their own).
 * One class for every page, so "what was done" and "what went wrong" behave the same everywhere.
 */
export class ActionState {
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);
  readonly status = signal<string | null>(null);

  constructor(private readonly describe: (error: unknown) => string = envelopeProblem) {}

  /** True if the action worked; `done` is what is said then. */
  async run(action: () => Promise<unknown>, done: string | null = null): Promise<boolean> {
    this.busy.set(true);
    this.error.set(null);
    this.status.set(null);
    try {
      await action();
      this.status.set(done);
      return true;
    } catch (error) {
      this.error.set(this.describe(error));
      return false;
    } finally {
      this.busy.set(false);
    }
  }

  /** Forgets what was said (the error and the success text). */
  clear(): void {
    this.error.set(null);
    this.status.set(null);
  }

  /** The same as `clear` (the Berichtsheft pages call it that). */
  reset(): void {
    this.clear();
  }
}

/** The error and success lines of an `ActionState`, as `wghn-alert`s with the test ids `error` and `status`. */
@Component({
  selector: 'wghn-action-messages',
  imports: [WghnAlert],
  template: `
    @if (state().error(); as message) {
      <wghn-alert class="mb-3" data-testid="error">{{ message }}</wghn-alert>
    }
    @if (state().status(); as message) {
      <wghn-alert kind="status" class="mb-3" data-testid="status">{{ message }}</wghn-alert>
    }
  `,
})
export class WghnActionMessages {
  readonly state = input.required<ActionState>();
}
