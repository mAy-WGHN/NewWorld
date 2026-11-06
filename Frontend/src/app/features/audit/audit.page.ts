import { HttpClient, HttpParams } from '@angular/common/http';
import { Component, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { ConfigService } from '../../core/config';
import { PAGE_UI } from '../../shared/ui/material';
import { formatDateTime } from '../notifications/notification-models';
import { FilterOption, TABLE_UI, TableState } from '../../shared/ui/table';

/** One line of the audit log of a service (`GET /api/audit`); `changes` is only there for people who may read the values. */
export interface AuditEntry {
  id: string;
  occurredAt: string;
  service: string;
  actorKind: string;
  actorPersonnelNumber: string;
  actorName: string;
  action: 'Create' | 'Update' | 'Delete';
  entityType: string;
  entityId: string;
  changes: string | null;
}

/** The lines one service gave so far, newest first, and whether it may have older ones. */
export interface ServiceLog {
  entries: AuditEntry[];
  hasMore: boolean;
}

export const allLoaded = (logs: Partial<Record<string, ServiceLog | null>>): AuditEntry[][] =>
  Object.values(logs).filter((l): l is ServiceLog => !!l).map((l) => l.entries);

export const AUDIT_SERVICES = ['access', 'workforce', 'portal', 'notifications'] as const;
type AuditService = (typeof AUDIT_SERVICES)[number];

const ACTION_LABEL: Record<AuditEntry['action'], string> = { Create: 'Angelegt', Update: 'Geändert', Delete: 'Gelöscht' };

/** The newest entries shown (and asked of each service, so the merged newest are right). */
export const AUDIT_LIMIT = 50;

/** Newest first; the lines of all services are merged by time. */
export function mergeByTime(lists: AuditEntry[][], limit: number): AuditEntry[] {
  return lists
    .flat()
    .sort((a, b) => (a.occurredAt < b.occurredAt ? 1 : a.occurredAt > b.occurredAt ? -1 : 0))
    .slice(0, limit);
}

export function actorLabel(entry: AuditEntry): string {
  if (entry.actorKind === 'Person') {
    const number = entry.actorPersonnelNumber ? ` (${entry.actorPersonnelNumber})` : '';
    return `${entry.actorName || 'Unbekannt'}${number}`;
  }
  return entry.actorKind === 'Service' ? `Dienst ${entry.actorName}` : 'Anonym';
}

/**
 * "Änderungsprotokoll": who created, changed or deleted what and when, merged from the logs of the services. Each service answers only for the
 * people who may read its log; the others are skipped without a message (the page shows what it can get).
 */
@Component({
  selector: 'app-audit',
  imports: [...PAGE_UI, ...TABLE_UI],
  template: `
    <app-page-head title="Änderungsprotokoll" />
    <p class="mb-4">Wer wann was angelegt, geändert oder gelöscht hat.</p>

    <div class="mb-3 flex flex-col gap-2.5">
      <div class="flex flex-wrap items-end gap-3">
        <mat-form-field appearance="outline" subscriptSizing="dynamic">
          <mat-label>Personalnummer (vom Dienst laden)</mat-label>
          <input matInput #nr (keyup.enter)="load(nr.value)" />
        </mat-form-field>
        <button mat-flat-button type="button" (click)="load(nr.value)" [disabled]="busy()">Laden</button>
        <app-table-search [table]="table" label="Suche in den geladenen Einträgen" />
      </div>
      <app-quick-filter [table]="table" name="action" label="Aktion" [options]="actionOptions" />
      <app-quick-filter [table]="table" name="service" label="Bereich" [options]="serviceOptions" />
      <app-quick-filter [table]="table" name="period" label="Zeitraum" [options]="periodOptions" />
    </div>

    @if (error()) {
      <p class="mb-3" role="alert">{{ error() }}</p>
    }

    <div class="overflow-x-auto">
      <table [appSortTable]="table" class="w-full text-left text-sm">
        <thead>
          <tr>
            <th scope="col" class="p-2" mat-sort-header="time">Zeitpunkt</th>
            <th scope="col" class="p-2" mat-sort-header="actor">Person</th>
            <th scope="col" class="p-2" mat-sort-header="action">Aktion</th>
            <th scope="col" class="p-2" mat-sort-header="service">Bereich</th>
            <th scope="col" class="p-2" mat-sort-header="entity">Objekt</th>
            @if (hasValues()) {
              <th scope="col" class="p-2">Werte</th>
            }
          </tr>
        </thead>
        <tbody>
          @for (e of table.rows(); track e.id) {
            <tr class="border-t align-top">
              <td class="p-2 whitespace-nowrap">{{ time(e) }}</td>
              <td class="p-2">{{ actor(e) }}</td>
              <td class="p-2">{{ action(e) }}</td>
              <td class="p-2">{{ e.service }}</td>
              <td class="p-2">{{ e.entityType }}<br /><span class="text-xs opacity-70">{{ e.entityId }}</span></td>
              @if (hasValues()) {
                <td class="p-2 max-w-md break-words font-mono text-xs">{{ e.changes ?? '' }}</td>
              }
            </tr>
          } @empty {
            <tr><td class="p-2" colspan="6">{{ loaded() ? 'Keine Einträge.' : 'Noch nicht geladen.' }}</td></tr>
          }
        </tbody>
      </table>
    </div>
    <app-pager [table]="table" />
    @if (canLoadMore()) {
      <div class="mt-3">
        <button mat-stroked-button type="button" data-action="load-more" (click)="loadMore()" [disabled]="busy()">Ältere Einträge laden</button>
      </div>
    }
  `,
})
export class AuditPage {
  private readonly http = inject(HttpClient);
  private readonly config = inject(ConfigService);

  /** What each service gave so far (newest first); `null` = no answer (no right, or not reachable). */
  private readonly logs = signal<Partial<Record<AuditService, ServiceLog | null>>>({});
  /** How many of the newest lines are shown; grows by one page with "Ältere Einträge laden". */
  private readonly shown = signal(AUDIT_LIMIT);
  private personnelNumber = '';
  protected readonly entries = computed(() => mergeByTime(allLoaded(this.logs()), this.shown()));
  protected readonly canLoadMore = computed(() => {
    const logs = Object.values(this.logs()).filter((l): l is ServiceLog => !!l);
    return logs.some((l) => l.hasMore) || logs.reduce((n, l) => n + l.entries.length, 0) > this.shown();
  });
  protected readonly busy = signal(false);
  protected readonly loaded = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly hasValues = computed(() => this.entries().some((e) => e.changes !== null));

  protected readonly time = (e: AuditEntry) => formatDateTime(e.occurredAt);
  protected readonly actor = actorLabel;
  protected readonly action = (e: AuditEntry) => ACTION_LABEL[e.action] ?? e.action;

  protected readonly actionOptions: FilterOption[] = [{ value: '', label: 'Alle' }, ...Object.entries(ACTION_LABEL).map(([value, label]) => ({ value, label }))];
  protected readonly serviceOptions: FilterOption[] = [{ value: '', label: 'Alle' }, ...AUDIT_SERVICES.map((s) => ({ value: s, label: s }))];
  protected readonly periodOptions: FilterOption[] = [
    { value: '', label: 'Alle' },
    { value: '1', label: 'Heute' },
    { value: '7', label: '7 Tage' },
    { value: '30', label: '30 Tage' },
  ];

  protected readonly table = new TableState<AuditEntry>(() => this.entries(), {
    columns: {
      time: (e) => e.occurredAt,
      actor: (e) => actorLabel(e),
      action: (e) => ACTION_LABEL[e.action] ?? e.action,
      service: (e) => e.service,
      entity: (e) => e.entityType,
    },
    sortKey: 'time',
    sortDir: 'desc',
    search: (e) => `${actorLabel(e)} ${e.entityType} ${e.entityId} ${e.changes ?? ''}`,
    filters: {
      action: (e, v) => e.action === v,
      service: (e, v) => e.service === v,
      period: (e, v) => periodStart(Number(v)) <= Date.parse(e.occurredAt),
    },
  });

  constructor() {
    void this.load('');
  }

  protected async load(personnelNumber: string): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    this.personnelNumber = personnelNumber.trim();
    this.shown.set(AUDIT_LIMIT);
    const results = await Promise.all(AUDIT_SERVICES.map((s) => this.fetch(s, null)));
    this.logs.set(Object.fromEntries(AUDIT_SERVICES.map((s, i) => [s, results[i]])));
    if (results.every((r) => r === null)) {
      this.error.set('Das Protokoll konnte nicht geladen werden.');
    }
    this.loaded.set(true);
    this.busy.set(false);
  }

  /**
   * One more page of the newest lines: every service that may have more gives its next page (after its last line seen), so that each holds at
   * least as many lines as are shown and the merged list is still the newest in all services.
   */
  protected async loadMore(): Promise<void> {
    this.busy.set(true);
    this.shown.update((n) => n + AUDIT_LIMIT);
    const current = this.logs();
    const next = await Promise.all(
      AUDIT_SERVICES.map(async (s) => {
        const log = current[s];
        if (!log || !log.hasMore || log.entries.length >= this.shown()) {
          return log ?? null;
        }
        const page = await this.fetch(s, log.entries[log.entries.length - 1]);
        return page ? { entries: [...log.entries, ...page.entries], hasMore: page.hasMore } : { ...log, hasMore: false };
      }),
    );
    this.logs.set(Object.fromEntries(AUDIT_SERVICES.map((s, i) => [s, next[i]])));
    this.busy.set(false);
  }

  /** The first page of a service, or the page after `after` (the last line seen). `null` = this service gave no answer. */
  private async fetch(service: AuditService, after: AuditEntry | null): Promise<ServiceLog | null> {
    let params = new HttpParams().set('limit', String(AUDIT_LIMIT));
    if (this.personnelNumber) {
      params = params.set('personnelNumber', this.personnelNumber);
    }
    if (after) {
      params = params.set('before', after.occurredAt).set('beforeId', after.id);
    }
    try {
      const entries = await firstValueFrom(this.http.get<AuditEntry[]>(this.config.url(service, '/api/audit'), { params }));
      return { entries, hasMore: entries.length >= AUDIT_LIMIT };
    } catch {
      return null;
    }
  }
}

/** Start of the day `days - 1` days ago (local time): 1 = today. */
export function periodStart(days: number): number {
  const d = new Date();
  d.setHours(0, 0, 0, 0);
  d.setDate(d.getDate() - (days - 1));
  return d.getTime();
}
