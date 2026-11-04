import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { ConfigService } from '../../core/config';
import { Absence, VacationBalance } from './absence-models';

/** The own absences in Workforce (`/api/me/...`). Errors are left to the caller; see `problemTitle`. */
@Injectable({ providedIn: 'root' })
export class AbsencesApi {
  private readonly http = inject(HttpClient);
  private readonly config = inject(ConfigService);

  private url(path: string): string {
    return this.config.url('workforce', `/api/me${path}`);
  }

  list(): Promise<Absence[]> {
    return firstValueFrom(this.http.get<Absence[]>(this.url('/absences')));
  }

  balance(year: number): Promise<VacationBalance> {
    return firstValueFrom(this.http.get<VacationBalance>(this.url('/vacation-balance'), { params: { year } }));
  }

  reportSick(from: string, to: string | null): Promise<Absence> {
    return firstValueFrom(this.http.post<Absence>(this.url('/absences/sick'), { from, to }));
  }

  requestVacation(from: string, to: string): Promise<Absence> {
    return firstValueFrom(this.http.post<Absence>(this.url('/absences/vacation'), { from, to }));
  }

  withdraw(id: string): Promise<Absence> {
    return firstValueFrom(this.http.post<Absence>(this.url(`/absences/${id}/withdraw`), null));
  }

  endSick(id: string, to: string): Promise<Absence> {
    return firstValueFrom(this.http.post<Absence>(this.url(`/absences/${id}/end`), { to }));
  }

  /** The certificate (AU) goes as a form: the file and the period the certificate covers. */
  uploadCertificate(id: string, file: File, certifiedFrom: string, certifiedTo: string): Promise<unknown> {
    const form = new FormData();
    form.set('certifiedFrom', certifiedFrom);
    form.set('certifiedTo', certifiedTo);
    form.set('file', file, file.name);
    return firstValueFrom(this.http.post(this.url(`/absences/${id}/certificates`), form));
  }
}
