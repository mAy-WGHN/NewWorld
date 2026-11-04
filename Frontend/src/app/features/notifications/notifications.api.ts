import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { ConfigService } from '../../core/config';
import { NotificationItem, NotificationType } from './notification-models';

export const PAGE_SIZE = 50;

/** The own notifications and the choice what to receive (Notifications). Errors are left to the caller. */
@Injectable({ providedIn: 'root' })
export class NotificationsApi {
  private readonly http = inject(HttpClient);
  private readonly config = inject(ConfigService);

  private url(path: string): string {
    return this.config.url('notifications', `/api/me${path}`);
  }

  /** The newest first; `before` (a time stamp) asks for older ones. */
  list(take: number, before?: string): Promise<NotificationItem[]> {
    let params = new HttpParams().set('take', take);
    if (before) {
      params = params.set('before', before);
    }
    return firstValueFrom(this.http.get<NotificationItem[]>(this.url('/notifications'), { params }));
  }

  async markRead(id: string): Promise<void> {
    await firstValueFrom(this.http.post(this.url(`/notifications/${id}/read`), null));
  }

  async markAllRead(): Promise<void> {
    await firstValueFrom(this.http.post(this.url('/notifications/read-all'), null));
  }

  preferences(): Promise<NotificationType[]> {
    return firstValueFrom(this.http.get<NotificationType[]>(this.url('/notification-preferences')));
  }

  async setPreference(templateKey: string, channel: string, enabled: boolean): Promise<void> {
    await firstValueFrom(this.http.put(this.url(`/notification-preferences/${templateKey}/${channel}`), { enabled }));
  }
}
