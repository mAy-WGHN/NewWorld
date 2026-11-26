import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatChipsModule } from '@angular/material/chips';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { AttentionBlock } from './attention-block';
import { PageHead } from './page-head';
import { RowCard } from './row-card';
import { WghnActionMessages } from './action-state';
import { WghnAlert } from './wghn-alert';
import { WghnConfirmBar } from './wghn-confirm-bar';
import { WghnLoading } from './wghn-loading';
import { WghnStatCard } from './wghn-stat-card';
import { WGHN_TABLE } from './wghn-table';

/**
 * The library of the portal (`wghn-…`): what almost every page needs. Buttons, cards, chips, icons and form fields of Material, and our own building
 * blocks: page head, row card, attention block, messages (`wghn-alert`), loading text, the question before deleting (`wghn-confirm-bar`), figures
 * (`wghn-stat-card`) and the table (`wghn-table` with `wghnCell`). Pages say what is in them; look and behaviour live here, once.
 * Further blocks are imported where they are used: `wghn-file-drop`, and the table helpers of `table-state` (pager, quick filter, search).
 */
export const PAGE_UI = [
  MatButtonModule,
  MatCardModule,
  MatChipsModule,
  MatFormFieldModule,
  MatIconModule,
  MatInputModule,
  PageHead,
  AttentionBlock,
  RowCard,
  WghnAlert,
  WghnActionMessages,
  WghnConfirmBar,
  WghnLoading,
  WghnStatCard,
  ...WGHN_TABLE,
] as const;
