import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { ConfigService } from '../../core/config';
import { NotificationTemplate, TemplateTexts } from './template-models';

/** The texts of the notifications in Notifications (privilege `template:manage`). Errors are left to the caller; see `problemTitle`. */
@Injectable({ providedIn: 'root' })
export class TemplateApi {
  private readonly http = inject(HttpClient);
  private readonly config = inject(ConfigService);

  private url(path = ''): string {
    return this.config.url('notifications', `/api/notification-templates${path}`);
  }

  list(): Promise<NotificationTemplate[]> {
    return firstValueFrom(this.http.get<NotificationTemplate[]>(this.url()));
  }

  save(key: string, texts: TemplateTexts): Promise<unknown> {
    return firstValueFrom(this.http.put(this.url(`/${encodeURIComponent(key)}`), texts));
  }

  /** Back to the texts of the file. */
  reset(key: string): Promise<unknown> {
    return firstValueFrom(this.http.delete(this.url(`/${encodeURIComponent(key)}`)));
  }

  /** How the texts would look; nothing is stored. */
  preview(key: string, texts: TemplateTexts): Promise<TemplateTexts> {
    return firstValueFrom(this.http.post<TemplateTexts>(this.url(`/${encodeURIComponent(key)}/preview`), texts));
  }
}
