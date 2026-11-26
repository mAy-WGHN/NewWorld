import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatRadioModule } from '@angular/material/radio';
import { MatTabsModule } from '@angular/material/tabs';
import { PAGE_UI } from '../../shared/wghn/material';
import { ActionState } from '../../shared/wghn/action-state';

/** What the pages of the Berichtsheft need: the shared page UI, checkboxes, radio buttons and tabs. */
export const LOGBOOK_UI = [...PAGE_UI, MatCheckboxModule, MatRadioModule, MatTabsModule] as const;

/** Busy flag, error and success text of one part of a page: the library's action state (the problem texts of the portal services are its default). */
export class Action extends ActionState {}
