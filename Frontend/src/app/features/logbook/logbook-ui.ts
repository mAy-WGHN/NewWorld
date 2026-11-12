import { signal } from '@angular/core';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatRadioModule } from '@angular/material/radio';
import { MatTabsModule } from '@angular/material/tabs';
import { PAGE_UI } from '../../shared/ui/material';
import { logbookError } from './logbook-models';

/** What the pages of the Berichtsheft need: the shared page UI, checkboxes, radio buttons and tabs. */
export const LOGBOOK_UI = [...PAGE_UI, MatCheckboxModule, MatRadioModule, MatTabsModule] as const;

/** Busy flag, error and success text of one part of a page. */
export class Action {
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);
  readonly status = signal<string | null>(null);

  /** True if the action worked; a failure becomes a plain German message. */
  async run(action: () => Promise<unknown>, done: string | null = null): Promise<boolean> {
    this.busy.set(true);
    this.error.set(null);
    this.status.set(null);
    try {
      await action();
      this.status.set(done);
      return true;
    } catch (error) {
      this.error.set(logbookError(error));
      return false;
    } finally {
      this.busy.set(false);
    }
  }

  reset(): void {
    this.error.set(null);
    this.status.set(null);
  }
}
