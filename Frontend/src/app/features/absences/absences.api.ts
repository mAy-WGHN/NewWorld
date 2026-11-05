import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { ConfigService } from '../../core/config';
import { Absence, SickNoteInput, VacationBalance, errorMessage, problemTitle } from './absence-models';

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

  /** The sick note; the AU (file or eAU mark) is part of the form, see `reportSickNote`. */
  reportSick(input: Pick<SickNoteInput, 'kind' | 'from' | 'to' | 'electronic'>): Promise<Absence> {
    return firstValueFrom(
      this.http.post<Absence>(this.url('/absences/sick'), { from: input.from, to: input.to, kind: input.kind, electronicCertificate: input.electronic }),
    );
  }

  /**
   * Reports the sick note and hands in the AU with it if there is a file (it covers the period of the sick note). A failed upload does not
   * undo the sick note: the answer says so and the person hands the AU in later ("AU bearbeiten").
   */
  async reportSickNote(input: SickNoteInput): Promise<{ certificateFailed: boolean; reason?: string }> {
    const absence = await this.reportSick(input);
    if (!input.file || input.electronic) {
      return { certificateFailed: false };
    }
    try {
      await this.uploadCertificate(absence.id, input.file, input.from, input.to);
      return { certificateFailed: false };
    } catch (error) {
      return { certificateFailed: true, reason: errorMessage(problemTitle(error)) };
    }
  }

  /** The AU is an electronic one (eAU, found in DATEV by HR) - or not (any more). */
  setElectronic(id: string, electronic: boolean): Promise<Absence> {
    return firstValueFrom(this.http.put<Absence>(this.url(`/absences/${id}/electronic-certificate`), { electronicCertificate: electronic }));
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
