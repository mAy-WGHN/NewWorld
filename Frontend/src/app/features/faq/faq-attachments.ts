import { Component, DestroyRef, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { formatSize } from '../sick-note-review/sick-note-review-models';
import { FaqAttachment, attachmentIcon, opensInTab } from './faq-models';
import { FaqApi } from './faq.api';

/** How long the address of an opened file lives (the new tab has loaded it long before). */
const OBJECT_URL_MS = 60_000;

/**
 * The files attached to an entry, for reading: images as small previews, everything else as a button with name and size. The files need the
 * sign-in, so they are fetched as data (with the token in the call, never in an address) and opened from there: PDF and images in a new
 * tab, Office documents as a download.
 */
@Component({
  selector: 'app-faq-attachments',
  imports: [MatButtonModule, MatIconModule],
  templateUrl: './faq-attachments.html',
})
export class FaqAttachments {
  private readonly api = inject(FaqApi);

  readonly attachments = input.required<FaqAttachment[]>();

  protected readonly previews = signal<Record<string, string>>({});
  protected readonly error = signal<string | null>(null);
  protected readonly images = computed(() => this.attachments().filter((a) => a.isImage));
  protected readonly files = computed(() => this.attachments().filter((a) => !a.isImage));

  protected readonly formatSize = formatSize;
  protected readonly icon = attachmentIcon;

  private readonly requested = new Set<string>();
  private readonly urls = new Set<string>();

  constructor() {
    effect(() => {
      const images = this.images();
      untracked(() => {
        for (const image of images) {
          if (!this.requested.has(image.id)) {
            this.requested.add(image.id);
            void this.loadPreview(image);
          }
        }
      });
    });
    inject(DestroyRef).onDestroy(() => {
      for (const url of this.urls) {
        URL.revokeObjectURL(url);
      }
    });
  }

  private async loadPreview(image: FaqAttachment): Promise<void> {
    try {
      const url = URL.createObjectURL(await this.api.downloadAttachment(image));
      this.urls.add(url);
      this.previews.update((known) => ({ ...known, [image.id]: url }));
    } catch {
      // the name stays as the button; opening tells what is wrong
    }
  }

  protected async open(attachment: FaqAttachment): Promise<void> {
    this.error.set(null);
    try {
      const blob = await this.api.downloadAttachment(attachment);
      const url = URL.createObjectURL(blob);
      if (opensInTab(attachment)) {
        window.open(url, '_blank', 'noopener');
      } else {
        const link = document.createElement('a');
        link.href = url;
        link.download = attachment.fileName;
        link.rel = 'noopener';
        link.click();
      }
      setTimeout(() => URL.revokeObjectURL(url), OBJECT_URL_MS);
    } catch {
      this.error.set(`Die Datei "${attachment.fileName}" konnte nicht geladen werden. Bitte erneut versuchen.`);
    }
  }
}
