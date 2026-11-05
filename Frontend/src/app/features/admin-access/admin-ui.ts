import { Component, input, signal } from '@angular/core';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { PAGE_UI } from '../../shared/ui/material';
import { accessProblemMessage } from './admin-access-models';

/** The building blocks of the administration pages: the shared page UI and checkboxes. */
export const ADMIN_UI = [...PAGE_UI, MatCheckboxModule] as const;

/** Busy flag, error and success text of one section; `run` turns a failed call into a plain German message. */
export class ActionState {
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);
  readonly status = signal<string | null>(null);

  /** True if the action worked. */
  async run(action: () => Promise<void>, done: string | null): Promise<boolean> {
    this.busy.set(true);
    this.error.set(null);
    this.status.set(null);
    try {
      await action();
      this.status.set(done);
      return true;
    } catch (error) {
      this.error.set(accessProblemMessage(error));
      return false;
    } finally {
      this.busy.set(false);
    }
  }

  clear(): void {
    this.error.set(null);
    this.status.set(null);
  }
}

/** The error and success lines of a section. */
@Component({
  selector: 'app-action-messages',
  template: `
    @if (state().error(); as message) {
      <p class="mb-3 rounded-xl border border-error px-4 py-2.5 text-error" role="alert" data-testid="error">{{ message }}</p>
    }
    @if (state().status(); as message) {
      <p class="mb-3 rounded-xl bg-primary-container px-4 py-2.5 text-on-primary-container" role="status" data-testid="status">{{ message }}</p>
    }
  `,
})
export class ActionMessages {
  readonly state = input.required<ActionState>();
}
