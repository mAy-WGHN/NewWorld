import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { ConfigService } from '../../core/config';

/** What the start page needs beyond the own absences: the vacation requests waiting for this person's decision. */
@Injectable({ providedIn: 'root' })
export class HomeApi {
  private readonly http = inject(HttpClient);
  private readonly config = inject(ConfigService);

  async pendingApprovals(): Promise<number> {
    const rows = await firstValueFrom(this.http.get<unknown[]>(this.config.url('workforce', '/api/approvals/pending')));
    return rows.length;
  }
}
