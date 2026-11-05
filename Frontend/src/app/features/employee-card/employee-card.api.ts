import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { ConfigService } from '../../core/config';
import { ChangeRequest, ChangeRequestInput, EmployeeCard } from './employee-card-models';

/** The own employee card and the change requests, in Workforce. Errors are left to the caller; see `problemTitle`. */
@Injectable({ providedIn: 'root' })
export class EmployeeCardApi {
  private readonly http = inject(HttpClient);
  private readonly config = inject(ConfigService);

  private url(path: string): string {
    return this.config.url('workforce', `/api/me/employee-card${path}`);
  }

  /** `null` if the person has no card (people that only exist in Business Central): the service answers 404. */
  async card(): Promise<EmployeeCard | null> {
    try {
      return await firstValueFrom(this.http.get<EmployeeCard>(this.url('')));
    } catch (error) {
      if (error instanceof HttpErrorResponse && error.status === 404) {
        return null;
      }
      throw error;
    }
  }

  requests(): Promise<ChangeRequest[]> {
    return firstValueFrom(this.http.get<ChangeRequest[]>(this.url('/change-requests')));
  }

  request(input: ChangeRequestInput): Promise<ChangeRequest> {
    return firstValueFrom(this.http.post<ChangeRequest>(this.url('/change-requests'), input));
  }

  withdraw(requestId: string): Promise<unknown> {
    return firstValueFrom(this.http.delete(this.url(`/change-requests/${requestId}`)));
  }
}
