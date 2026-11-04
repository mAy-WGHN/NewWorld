import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatChipsModule } from '@angular/material/chips';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { AttentionBlock } from './attention-block';
import { PageHead } from './page-head';
import { RowCard } from './row-card';

/** What almost every page needs: buttons, cards, chips, icons, form fields, and our own page building blocks. */
export const PAGE_UI = [MatButtonModule, MatCardModule, MatChipsModule, MatFormFieldModule, MatIconModule, MatInputModule, PageHead, AttentionBlock, RowCard] as const;
