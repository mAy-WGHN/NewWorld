import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom, map } from 'rxjs';
import { ConfigService } from '../../core/config';
import { LocationSeverityStatistic, MonthRange, MonthlyCategoryStatistic, MonthlyStatistic, PersonnelNumberStatistic } from './statistics-models';

/** The portal wraps answers as `{success, data}`; this unwraps them. Errors are left to the caller. */
interface Envelope<T> {
  success: boolean;
  data: T;
}

/** Statistics of the reports within the reach of the person (`statistic:read`); `from`/`to` are months (`yyyy-MM`), both inclusive. */
@Injectable({ providedIn: 'root' })
export class StatisticsApi {
  private readonly http = inject(HttpClient);
  private readonly config = inject(ConfigService);

  monthly(range: MonthRange): Promise<MonthlyStatistic[]> {
    return this.get('monthly', range);
  }

  monthlyByCategory(range: MonthRange): Promise<MonthlyCategoryStatistic[]> {
    return this.get('monthly-by-category', range);
  }

  byLocationSeverity(range: MonthRange): Promise<LocationSeverityStatistic[]> {
    return this.get('by-location-severity', range);
  }

  byPersonnelNumber(range: MonthRange): Promise<PersonnelNumberStatistic[]> {
    return this.get('by-personnel-number', range);
  }

  private get<T>(name: string, range: MonthRange): Promise<T[]> {
    const params: Record<string, string> = {};
    if (range.from) {
      params['from'] = range.from;
    }
    if (range.to) {
      params['to'] = range.to;
    }
    return firstValueFrom(this.http.get<Envelope<T[]>>(this.config.url('portal', `/api/statistics/${name}`), { params }).pipe(map((a) => a.data)));
  }
}
