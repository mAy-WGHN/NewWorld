import { HttpClient } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { AuthService } from './auth/auth.service';
import { ConfigService } from './config';

/** How many notifications the person has not read yet (the badge at the bell). */
@Injectable({ providedIn: 'root' })
export class NotificationsService {
  private readonly http = inject(HttpClient);
  private readonly config = inject(ConfigService);
  private readonly auth = inject(AuthService);

  /** `null` until known (or without the privilege). */
  readonly unread = signal<number | null>(null);

  async refresh(): Promise<void> {
    if (!this.auth.privileges().has('notification:read-own')) {
      return;
    }
    try {
      const answer = await firstValueFrom(this.http.get<{ count: number }>(this.config.url('notifications', '/api/me/notifications/unread-count')));
      this.unread.set(answer.count);
    } catch {
      // keep the last count; the next refresh tries again
    }
  }

  clear(): void {
    this.unread.set(null);
  }
}
