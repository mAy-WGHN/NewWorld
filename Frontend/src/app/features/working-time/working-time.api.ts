import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { ConfigService } from '../../core/config';
import { WorkingTime } from './working-time-models';

@Injectable({ providedIn: 'root' })
export class WorkingTimeApi {
  private readonly http = inject(HttpClient);
  private readonly config = inject(ConfigService);

  mine(): Promise<WorkingTime> {
    return firstValueFrom(this.http.get<WorkingTime>(this.config.url('workforce', '/api/me/working-time')));
  }
}
