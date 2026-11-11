import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom, map } from 'rxjs';
import { ConfigService } from '../../core/config';
import { AddUnavailabilityInput, StoreOption, StorePerson, StoreUnavailability, UnavailabilityKind, UnavailabilityPeriod } from './team-absences-models';

/** Unavailability in Workforce and the store names of Portal. Errors are left to the caller; see `problemTitle`. */
@Injectable({ providedIn: 'root' })
export class TeamAbsencesApi {
  private readonly http = inject(HttpClient);
  private readonly config = inject(ConfigService);

  private url(path: string): string {
    return this.config.url('workforce', `/api${path}`);
  }

  /** The stores with their names (public list of Portal; the id is the one Workforce uses). */
  stores(): Promise<StoreOption[]> {
    return firstValueFrom(
      this.http
        .get<{ data: { items: { id: string; name: string; archived?: boolean }[] } }>(this.config.url('portal', '/api/locations'))
        .pipe(map((answer) => answer.data.items.filter((s) => !s.archived).map((s) => ({ id: s.id, name: s.name })))),
    );
  }

  /** Who is not available in the store between the two days (including both; the service takes at most 366 days). */
  store(storeId: string, from: string, to: string): Promise<StoreUnavailability[]> {
    const params = new HttpParams().set('from', from).set('to', to);
    return firstValueFrom(this.http.get<StoreUnavailability[]>(this.url(`/stores/${storeId}/unavailabilities`), { params }));
  }

  /** The active people of the store (also those without an entry), for the rows of the grid. */
  people(storeId: string): Promise<StorePerson[]> {
    return firstValueFrom(this.http.get<StorePerson[]>(this.url(`/stores/${storeId}/people`)));
  }

  /** The own timeline; with a period (days, including both; either one may be left out) only periods that touch it, otherwise everything. */
  mine(from?: string, to?: string): Promise<UnavailabilityPeriod[]> {
    let params = new HttpParams();
    if (from) {
      params = params.set('from', from);
    }
    if (to) {
      params = params.set('to', to);
    }
    return firstValueFrom(this.http.get<UnavailabilityPeriod[]>(this.url('/me/unavailabilities'), { params }));
  }

  kinds(): Promise<UnavailabilityKind[]> {
    return firstValueFrom(this.http.get<UnavailabilityKind[]>(this.url('/unavailability-kinds')));
  }

  add(employeeId: string, input: AddUnavailabilityInput): Promise<UnavailabilityPeriod> {
    return firstValueFrom(this.http.post<UnavailabilityPeriod>(this.url(`/employees/${employeeId}/unavailabilities`), input));
  }

  remove(employeeId: string, entryId: string): Promise<unknown> {
    return firstValueFrom(this.http.delete(this.url(`/employees/${employeeId}/unavailabilities/${entryId}`)));
  }
}
