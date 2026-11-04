import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { ConfigService } from '../../core/config';
import { PendingApproval } from './approval-models';

/** Vacation requests the signed-in person has to decide (Workforce). Errors are left to the caller; see `problemTitle`. */
@Injectable({ providedIn: 'root' })
export class ApprovalsApi {
  private readonly http = inject(HttpClient);
  private readonly config = inject(ConfigService);

  pending(): Promise<PendingApproval[]> {
    return firstValueFrom(this.http.get<PendingApproval[]>(this.config.url('workforce', '/api/approvals/pending')));
  }

  approve(absenceId: string): Promise<unknown> {
    return firstValueFrom(this.http.post(this.config.url('workforce', `/api/absences/${absenceId}/approve`), null));
  }

  reject(absenceId: string): Promise<unknown> {
    return firstValueFrom(this.http.post(this.config.url('workforce', `/api/absences/${absenceId}/reject`), null));
  }
}
