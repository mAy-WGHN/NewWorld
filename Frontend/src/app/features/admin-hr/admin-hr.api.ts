import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom, map } from 'rxjs';
import { ConfigService } from '../../core/config';
import {
  AccessLogEntry,
  CertificateReminders,
  CertificateRetention,
  KindForm,
  MonthlyThreshold,
  NumberSeries,
  Page,
  ReferenceEntry,
  SetNumberSeriesRequest,
  SetVacationRulesRequest,
  SetWorkingTimeRulesRequest,
  StoreHolidayOpening,
  UnavailabilityKind,
  VacationRules,
  WorkingTimeRules,
} from './admin-hr-models';

/** A store of the portal (`GET /api/locations`). */
export interface StoreLocation {
  id: string;
  name: string;
  storeCode: string;
  archived: boolean;
}

/** The settings HR maintains in Workforce, and the stores of the portal. Errors are left to the caller; see `describeError`. */
@Injectable({ providedIn: 'root' })
export class AdminHrApi {
  private readonly http = inject(HttpClient);
  private readonly config = inject(ConfigService);

  private url(path: string): string {
    return this.config.url('workforce', `/api${path}`);
  }

  // --- vacation
  vacationRules(): Promise<VacationRules> {
    return firstValueFrom(this.http.get<VacationRules>(this.url('/vacation-rules')));
  }

  saveVacationRules(request: SetVacationRulesRequest): Promise<VacationRules> {
    return firstValueFrom(this.http.put<VacationRules>(this.url('/vacation-rules'), request));
  }

  // --- working time
  workingTimeRules(): Promise<WorkingTimeRules> {
    return firstValueFrom(this.http.get<WorkingTimeRules>(this.url('/working-time-rules')));
  }

  saveWorkingTimeRules(request: SetWorkingTimeRulesRequest): Promise<unknown> {
    return firstValueFrom(this.http.put(this.url('/working-time-rules'), request));
  }

  thresholds(): Promise<MonthlyThreshold[]> {
    return firstValueFrom(this.http.get<MonthlyThreshold[]>(this.url('/working-time-thresholds')));
  }

  /** `null` removes the threshold of the relationship. */
  saveThreshold(relationshipId: string, hours: number | null): Promise<unknown> {
    return firstValueFrom(this.http.put(this.url(`/working-time-thresholds/${relationshipId}`), { hours }));
  }

  // --- certificates
  retention(): Promise<CertificateRetention> {
    return firstValueFrom(this.http.get<CertificateRetention>(this.url('/certificates/retention')));
  }

  saveRetention(retentionAfterEndDays: number): Promise<CertificateRetention> {
    return firstValueFrom(this.http.put<CertificateRetention>(this.url('/certificates/retention'), { retentionAfterEndDays }));
  }

  reminders(): Promise<CertificateReminders> {
    return firstValueFrom(this.http.get<CertificateReminders>(this.url('/certificates/reminders')));
  }

  saveReminders(request: { firstReminderAfterReturnDays: number; reminderEveryDays: number; maxReminders: number }): Promise<CertificateReminders> {
    return firstValueFrom(this.http.put<CertificateReminders>(this.url('/certificates/reminders'), request));
  }

  accessLog(from: string | null, to: string | null, page: number, pageSize: number): Promise<Page<AccessLogEntry>> {
    let params = new HttpParams().set('page', page).set('pageSize', pageSize);
    if (from) {
      params = params.set('from', from);
    }
    if (to) {
      params = params.set('to', to);
    }
    return firstValueFrom(this.http.get<Page<AccessLogEntry>>(this.url('/certificates/access-log'), { params }));
  }

  // --- employee card
  numberSeries(): Promise<NumberSeries> {
    return firstValueFrom(this.http.get<NumberSeries>(this.url('/personnel-number-series')));
  }

  saveNumberSeries(request: SetNumberSeriesRequest): Promise<NumberSeries> {
    return firstValueFrom(this.http.put<NumberSeries>(this.url('/personnel-number-series'), request));
  }

  /** `kind` is the segment of the path (`Function`, `EmploymentRelationship`, ...). */
  listEntries(kind: string): Promise<ReferenceEntry[]> {
    return firstValueFrom(this.http.get<ReferenceEntry[]>(this.url(`/employee-lists/${kind}`)));
  }

  addEntry(kind: string, code: string, name: string): Promise<ReferenceEntry> {
    return firstValueFrom(this.http.post<ReferenceEntry>(this.url(`/employee-lists/${kind}`), { code, name }));
  }

  /** The code stays as it is; only name and state change. */
  updateEntry(kind: string, id: string, name: string, isActive: boolean): Promise<unknown> {
    return firstValueFrom(this.http.put(this.url(`/employee-lists/${kind}/${id}`), { name, isActive }));
  }

  // --- unavailability kinds
  kinds(): Promise<UnavailabilityKind[]> {
    return firstValueFrom(this.http.get<UnavailabilityKind[]>(this.url('/unavailability-kinds'), { params: { includeInactive: true } }));
  }

  addKind(form: KindForm): Promise<UnavailabilityKind> {
    return firstValueFrom(this.http.post<UnavailabilityKind>(this.url('/unavailability-kinds'), kindBody(form, true)));
  }

  /** Everything but the code; `isActive` left out keeps the state. */
  updateKind(id: string, form: KindForm, isActive?: boolean): Promise<unknown> {
    return firstValueFrom(this.http.put(this.url(`/unavailability-kinds/${id}`), { ...kindBody(form, false), ...(isActive === undefined ? {} : { isActive }) }));
  }

  // --- holiday opening
  holidayOpening(storeId: string): Promise<StoreHolidayOpening> {
    return firstValueFrom(this.http.get<StoreHolidayOpening>(this.url(`/stores/${storeId}/holiday-opening`)));
  }

  /** All or none: one store outside the reach refuses all of them. */
  setHolidayOpening(storeIds: string[], opensOnHolidays: boolean): Promise<unknown> {
    return firstValueFrom(this.http.put(this.url('/stores/holiday-opening'), { storeIds, opensOnHolidays }));
  }

  /** The stores of the portal. */
  stores(): Promise<StoreLocation[]> {
    return firstValueFrom(
      this.http.get<{ data: { items: StoreLocation[] } }>(this.config.url('portal', '/api/locations')).pipe(map((answer) => answer.data.items)),
    );
  }
}

function kindBody(form: KindForm, withCode: boolean): Record<string, unknown> {
  return {
    ...(withCode ? { code: form.code.trim() } : {}),
    description: form.description.trim(),
    paidTime: form.paidTime,
    scheduleSymbol: form.scheduleSymbol.trim() || null,
    workCode: form.workCode.trim() || null,
  };
}
