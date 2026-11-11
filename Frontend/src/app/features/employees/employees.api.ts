import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { ConfigService } from '../../core/config';
import { ChangeRequest, CreatedEmployee, EmployeeCard, EmployeeRequest, EmployeeSummary, ReferenceEntry } from './employee-models';

/** The employee card and the change requests, in Workforce. Errors are left to the caller; see `problemTitle`. */
@Injectable({ providedIn: 'root' })
export class EmployeesApi {
  private readonly http = inject(HttpClient);
  private readonly config = inject(ConfigService);

  private url(path: string): string {
    return this.config.url('workforce', `/api${path}`);
  }

  changeRequests(): Promise<ChangeRequest[]> {
    return firstValueFrom(this.http.get<ChangeRequest[]>(this.url('/employee-change-requests')));
  }

  approve(requestId: string): Promise<unknown> {
    return firstValueFrom(this.http.post(this.url(`/employee-change-requests/${requestId}/approve`), null));
  }

  reject(requestId: string, reason: string | null): Promise<unknown> {
    return firstValueFrom(this.http.post(this.url(`/employee-change-requests/${requestId}/reject`), { reason }));
  }

  list(kind: string): Promise<ReferenceEntry[]> {
    return firstValueFrom(this.http.get<ReferenceEntry[]>(this.url(`/employee-lists/${kind}`)));
  }

  create(request: EmployeeRequest): Promise<CreatedEmployee> {
    return firstValueFrom(this.http.post<CreatedEmployee>(this.url('/employees'), request));
  }

  /** People in reach by words of the name or the personnel number (at least two characters), at most `take` of them. */
  search(query: string, take = 20): Promise<EmployeeSummary[]> {
    return firstValueFrom(this.http.get<EmployeeSummary[]>(this.url('/employees'), { params: { query, take } }));
  }

  card(employeeId: string): Promise<EmployeeCard> {
    return firstValueFrom(this.http.get<EmployeeCard>(this.url(`/employees/${employeeId}/card`)));
  }
}
