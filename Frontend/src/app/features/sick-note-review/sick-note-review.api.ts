import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { ConfigService } from '../../core/config';
import { CertificateDocument, ELECTRONIC_FILTER, Page, SickNoteOverview } from './sick-note-review-models';

/** The sick notes and certificates (AU) as HR sees them, in Workforce. Errors are left to the caller; see `problemTitle`. */
@Injectable({ providedIn: 'root' })
export class SickNoteReviewApi {
  private readonly http = inject(HttpClient);
  private readonly config = inject(ConfigService);

  private url(path: string): string {
    return this.config.url('workforce', `/api${path}`);
  }

  list(certificateStatus: string | null, page: number, pageSize: number): Promise<Page<SickNoteOverview>> {
    let params = new HttpParams().set('page', page).set('pageSize', pageSize);
    if (certificateStatus === ELECTRONIC_FILTER) {
      params = params.set('electronic', true);
    } else if (certificateStatus) {
      params = params.set('certificateStatus', certificateStatus);
    }
    return firstValueFrom(this.http.get<Page<SickNoteOverview>>(this.url('/sick-notes'), { params }));
  }

  documents(absenceId: string): Promise<CertificateDocument[]> {
    return firstValueFrom(this.http.get<CertificateDocument[]>(this.url(`/absences/${absenceId}/certificates`)));
  }

  verify(absenceId: string, documentId: string): Promise<unknown> {
    return firstValueFrom(this.http.post(this.url(`/absences/${absenceId}/certificates/${documentId}/verify`), null));
  }

  reject(absenceId: string, documentId: string, reason: string): Promise<unknown> {
    return firstValueFrom(this.http.post(this.url(`/absences/${absenceId}/certificates/${documentId}/reject`), { reason }));
  }

  remove(absenceId: string, documentId: string): Promise<unknown> {
    return firstValueFrom(this.http.delete(this.url(`/absences/${absenceId}/certificates/${documentId}`)));
  }

  /** The file itself, for opening. The service logs every opening (who, when; never the content). */
  content(absenceId: string, documentId: string): Promise<Blob> {
    return firstValueFrom(this.http.get(this.url(`/absences/${absenceId}/certificates/${documentId}/content`), { responseType: 'blob' }));
  }
}
