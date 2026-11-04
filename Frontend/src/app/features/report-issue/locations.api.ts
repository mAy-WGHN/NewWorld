import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom, map } from 'rxjs';
import { ConfigService } from '../../core/config';
import { Location } from './report-issue-models';

/** The open stores of the portal, for the report form (public, like the form itself). */
@Injectable({ providedIn: 'root' })
export class LocationsApi {
  private readonly http = inject(HttpClient);
  private readonly config = inject(ConfigService);

  list(): Promise<Location[]> {
    return firstValueFrom(
      this.http.get<{ data: { items: Location[] } }>(this.config.url('portal', '/api/locations')).pipe(map((answer) => answer.data.items)),
    );
  }
}
