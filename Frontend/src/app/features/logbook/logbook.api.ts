import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom, map } from 'rxjs';
import { ConfigService } from '../../core/config';
import { SectionInput, StructureInput } from './logbook-admin-models';
import { Evaluation } from './logbook-evaluation-models';
import {
  LogbookAnswer,
  LogbookComment,
  LogbookTemplate,
  LogbookMe,
  Missing,
  MyReports,
  ReminderSettings,
  ReportDetail,
  ReportState,
  ReportSummary,
  ReviewFilter,
  ReviewItem,
  LogbookReport,
  Structure,
  StoreOption,
} from './logbook-models';

interface Envelope<T> {
  success: boolean;
  data: T;
}

/** The Berichtsheft of the portal service (`/api/logbook/...`); answers come wrapped as `{success, data}`. */
@Injectable({ providedIn: 'root' })
export class LogbookApi {
  private readonly http = inject(HttpClient);
  private readonly config = inject(ConfigService);

  private url(path: string): string {
    return this.config.url('portal', `/api/logbook${path}`);
  }

  private get<T>(path: string, params?: HttpParams): Promise<T> {
    return firstValueFrom(this.http.get<Envelope<T>>(this.url(path), { params }).pipe(map((a) => a.data)));
  }

  private send<T>(method: 'POST' | 'PUT', path: string, body?: unknown): Promise<T> {
    return firstValueFrom(this.http.request<Envelope<T>>(method, this.url(path), { body: body ?? {} }).pipe(map((a) => a.data)));
  }

  // -------------------------------------------------------------- store manager

  myReports(week?: string): Promise<MyReports> {
    return this.get('/my-reports', week ? new HttpParams().set('week', week) : undefined);
  }

  template(version: number): Promise<LogbookTemplate> {
    return this.get(`/template/${version}`);
  }

  currentTemplate(): Promise<LogbookTemplate> {
    return this.get('/template');
  }

  save(storeId: string, week: string, answers: Record<string, LogbookAnswer>): Promise<LogbookReport> {
    return this.send('PUT', `/reports/${storeId}/${week}`, { answers });
  }

  submit(storeId: string, week: string): Promise<LogbookReport> {
    return this.send('POST', `/reports/${storeId}/${week}/submit`);
  }

  history(storeId: string, take = 12): Promise<ReportSummary[]> {
    return this.get(`/stores/${storeId}/reports`, new HttpParams().set('take', take));
  }

  followUp(id: string, note: string): Promise<ReportDetail> {
    return this.send('POST', `/reports/${id}/follow-up`, { note });
  }

  // -------------------------------------------------------------- leaders

  list(filter: ReviewFilter): Promise<ReviewItem[]> {
    let params = new HttpParams();
    if (filter.week) {
      params = params.set('week', filter.week);
    }
    if (filter.open) {
      params = params.set('open', true);
    }
    if (filter.state) {
      params = params.set('state', filter.state);
    }
    return this.get('/reports', params);
  }

  detail(id: string): Promise<ReportDetail> {
    return this.get(`/reports/${id}`);
  }

  comment(id: string, kind: 'reply' | 'internal', text: string): Promise<LogbookComment> {
    return this.send('POST', `/reports/${id}/comments`, { kind, text });
  }

  setState(id: string, state: Extract<ReportState, 'inProgress' | 'done'>): Promise<ReportDetail> {
    return this.send('PUT', `/reports/${id}/state`, { state });
  }

  escalate(id: string, note: string | null): Promise<ReportDetail> {
    return this.send('POST', `/reports/${id}/escalate`, { note });
  }

  /** The stores the person reaches as a leader (the hierarchy decides, not the role). */
  me(): Promise<LogbookMe> {
    return this.get('/me');
  }

  missing(week?: string): Promise<Missing> {
    return this.get('/missing', week ? new HttpParams().set('week', week) : undefined);
  }

  remind(storeId: string, week: string): Promise<void> {
    return this.send<unknown>('POST', '/reminders', { storeId, week }).then(() => undefined);
  }

  /** The figures of a calendar year for the stores, regions and districts the person sees. */
  evaluation(year?: number): Promise<Evaluation> {
    return this.get('/evaluation', year ? new HttpParams().set('year', year) : undefined);
  }

  // -------------------------------------------------------------- administration

  structure(): Promise<Structure> {
    return this.get('/structure');
  }

  saveStructure(body: StructureInput): Promise<Structure> {
    return this.send('PUT', '/structure', body);
  }

  saveTemplate(sections: SectionInput[]): Promise<LogbookTemplate> {
    return this.send('PUT', '/template', { sections });
  }

  reminderSettings(): Promise<ReminderSettings> {
    return this.get('/reminder-settings');
  }

  saveReminderSettings(settings: Pick<ReminderSettings, 'enabled' | 'dueDay' | 'dueTime' | 'points' | 'maxReminders'>): Promise<ReminderSettings> {
    return this.send('PUT', '/reminder-settings', settings);
  }

  /** The stores of the portal, for assigning them in the hierarchy. */
  async stores(): Promise<StoreOption[]> {
    const answer = await firstValueFrom(this.http.get<{ data: { items: StoreOption[] } }>(this.config.url('portal', '/api/locations')));
    return answer.data.items.filter((s) => !s.archived);
  }
}
