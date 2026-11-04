import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom, map } from 'rxjs';
import { ConfigService } from '../../core/config';
import { Report, ReportPage } from './report-models';

/** The portal wraps answers as `{success, data}`; this unwraps them. Errors are left to the caller (see `envelopeMessage`). */
interface Envelope<T> {
  success: boolean;
  data: T;
}

export const PAGE_SIZE = 20;

@Injectable({ providedIn: 'root' })
export class ReportsApi {
  private readonly http = inject(HttpClient);
  private readonly config = inject(ConfigService);

  private url(path: string): string {
    return this.config.url('portal', `/api/reports${path}`);
  }

  /** The reports of one state that are not archived; the service sorts the most pressing first. */
  list(status: 'open' | 'done', offset: number, limit = PAGE_SIZE): Promise<ReportPage> {
    return firstValueFrom(
      this.http
        .get<Envelope<ReportPage>>(this.url(''), { params: { status, archived: false, offset, limit } })
        .pipe(map((answer) => answer.data)),
    );
  }

  listArchived(offset: number, limit = PAGE_SIZE): Promise<ReportPage> {
    return firstValueFrom(this.http.get<Envelope<ReportPage>>(this.url('/archived'), { params: { offset, limit } }).pipe(map((answer) => answer.data)));
  }

  setStatus(id: string, status: 'open' | 'done'): Promise<Report> {
    return this.patch(id, { status });
  }

  saveComment(id: string, comment: string): Promise<Report> {
    return this.patch(id, { comment });
  }

  restore(id: string): Promise<Report> {
    return this.patch(id, { archived: false });
  }

  /** Archives (hides) a report; nothing is deleted. */
  async archive(id: string): Promise<void> {
    await firstValueFrom(this.http.delete(this.url(`/${id}`)));
  }

  private patch(id: string, body: Record<string, unknown>): Promise<Report> {
    return firstValueFrom(this.http.patch<Envelope<Report>>(this.url(`/${id}`), body).pipe(map((answer) => answer.data)));
  }
}
