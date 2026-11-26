import { ActionState as WghnActionState, WghnActionMessages } from '../../shared/wghn/action-state';

import { MatCheckboxModule } from '@angular/material/checkbox';
import { PAGE_UI } from '../../shared/wghn/material';
import { accessProblemMessage } from './admin-access-models';

/** The building blocks of the administration pages: the shared page UI and checkboxes. */
export const ADMIN_UI = [...PAGE_UI, MatCheckboxModule] as const;

/** Busy flag, error and success text of one section: the library's action state, told the way the Access service tells problems. */
export class ActionState extends WghnActionState {
  constructor() {
    super(accessProblemMessage);
  }
}

/** The error and success lines of a section (the library's `wghn-action-messages`). */
export const ActionMessages = WghnActionMessages;
