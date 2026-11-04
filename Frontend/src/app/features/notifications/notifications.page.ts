import { NgTemplateOutlet } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { MatCheckboxChange, MatCheckboxModule } from '@angular/material/checkbox';
import { RouterLink } from '@angular/router';
import { AuthService } from '../../core/auth/auth.service';
import { NotificationsService } from '../../core/notifications.service';
import { PAGE_UI } from '../../shared/ui/material';
import { GENERIC_ERROR, errorMessage, problemTitle } from '../absences/absence-models';
import { NotificationItem, NotificationType, channelLabel, formatDateTime, safeDeepLink, typeLabel } from './notification-models';
import { NotificationsApi, PAGE_SIZE } from './notifications.api';

/**
 * "Mitteilungen": the own notifications, the unread ones first in a yellow block, and below the choice what to receive per kind and channel.
 * "Alle als gelesen markieren" is at the top right.
 */
@Component({
  selector: 'app-notifications',
  imports: [NgTemplateOutlet, RouterLink, MatCheckboxModule, ...PAGE_UI],
  templateUrl: './notifications.page.html',
})
export class NotificationsPage {
  private readonly api = inject(NotificationsApi);
  private readonly auth = inject(AuthService);
  private readonly bell = inject(NotificationsService);

  // What the person may do; the service checks again with every call.
  protected readonly canUpdate = computed(() => this.auth.privileges().has('notification:update-own'));
  protected readonly canReadChoices = computed(() => this.auth.privileges().has('preference:read-own'));
  protected readonly canChangeChoices = computed(() => this.auth.privileges().has('preference:update-own'));

  protected readonly items = signal<NotificationItem[]>([]);
  protected readonly loaded = signal(false);
  protected readonly listError = signal<string | null>(null);
  /** A full page came back, so there may be older ones. */
  protected readonly hasMore = signal(false);

  protected readonly unread = computed(() => this.items().filter((n) => !n.readAt));
  protected readonly read = computed(() => this.items().filter((n) => n.readAt));

  protected readonly types = signal<NotificationType[]>([]);
  /** Counted up to build the check boxes again (see `toggle`). */
  protected readonly rebuild = signal(0);

  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly status = signal<string | null>(null);

  // Plain functions for the template.
  protected readonly typeLabel = typeLabel;
  protected readonly channelLabel = channelLabel;
  protected readonly formatDateTime = formatDateTime;
  protected readonly safeDeepLink = safeDeepLink;

  constructor() {
    void this.load();
    if (this.canReadChoices()) {
      void this.loadChoices();
    }
  }

  protected async markRead(item: NotificationItem): Promise<void> {
    await this.act(() => this.api.markRead(item.id));
  }

  protected async markAllRead(): Promise<void> {
    await this.act(() => this.api.markAllRead(), 'Alle Mitteilungen sind als gelesen markiert.');
  }

  /** Following a link marks the notification as read on the way; the page change does not wait for it. */
  protected opened(item: NotificationItem): void {
    if (!item.readAt && this.canUpdate()) {
      void this.api.markRead(item.id).then(() => this.bell.refresh(), () => undefined);
    }
  }

  protected async more(): Promise<void> {
    const last = this.items().at(-1);
    if (!last) {
      return;
    }
    try {
      const older = await this.api.list(PAGE_SIZE, last.createdAt);
      this.items.update((list) => [...list, ...older]);
      this.hasMore.set(older.length === PAGE_SIZE);
    } catch {
      this.error.set('Ältere Mitteilungen konnten nicht geladen werden.');
    }
  }

  protected async toggle(type: NotificationType, channel: string, event: MatCheckboxChange): Promise<void> {
    const enabled = event.checked;
    this.error.set(null);
    this.setChoice(type.templateKey, channel, enabled);
    try {
      await this.api.setPreference(type.templateKey, channel, enabled);
    } catch (error) {
      this.setChoice(type.templateKey, channel, !enabled);
      this.rebuild.update((n) => n + 1); // the boxes are built again from the data, so none keeps a value the service refused
      this.error.set(problemTitle(error) === 'Mandatory' ? 'Diese Mitteilung ist Pflicht und lässt sich nicht abschalten.' : GENERIC_ERROR);
    }
  }

  private setChoice(key: string, channel: string, enabled: boolean): void {
    this.types.update((list) =>
      list.map((t) => (t.templateKey === key ? { ...t, channels: t.channels.map((c) => (c.channel === channel ? { ...c, enabled } : c)) } : t)),
    );
  }

  /** Runs a change, then loads the list again and tells the bell; shows what the service answered if it refuses. */
  private async act(action: () => Promise<unknown>, success?: string): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    this.status.set(null);
    try {
      await action();
      if (success) {
        this.status.set(success);
      }
      await this.load();
    } catch (error) {
      this.error.set(errorMessage(problemTitle(error)));
    } finally {
      this.busy.set(false);
    }
  }

  private async load(): Promise<void> {
    try {
      const page = await this.api.list(PAGE_SIZE);
      this.items.set(page);
      this.hasMore.set(page.length === PAGE_SIZE);
      this.listError.set(null);
      void this.bell.refresh();
    } catch {
      this.listError.set('Die Mitteilungen konnten nicht geladen werden.');
    } finally {
      this.loaded.set(true);
    }
  }

  private async loadChoices(): Promise<void> {
    try {
      this.types.set(await this.api.preferences());
    } catch {
      this.error.set('Die Einstellungen konnten nicht geladen werden.');
    }
  }
}
