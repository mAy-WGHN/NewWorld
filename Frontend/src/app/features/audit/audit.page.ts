import { HttpClient, HttpParams } from '@angular/common/http';
import { Component, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { ConfigService } from '../../core/config';
import { PAGE_UI } from '../../shared/ui/material';
import { formatDateTime } from '../notifications/notification-models';

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

export const AUDIT_SERVICES = ['access', 'workforce', 'portal', 'notifications'] as const;
type AuditService = (typeof AUDIT_SERVICES)[number];

const ACTION_LABEL: Record<AuditEntry['action'], string> = { Create: 'Angelegt', Update: 'Geändert', Delete: 'Gelöscht' };

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
  imports: [...PAGE_UI],
  template: `
    <app-page-head title="Änderungsprotokoll" />
    <p class="mb-4">Wer wann was angelegt, geändert oder gelöscht hat.</p>

    <div class="mb-4 flex flex-wrap items-end gap-3">
      <label class="flex flex-col text-sm">Personalnummer
        <input class="rounded border px-2 py-1" #nr (keyup.enter)="load(nr.value)" />
      </label>
      <button mat-flat-button type="button" (click)="load(nr.value)" [disabled]="busy()">Anzeigen</button>
    </div>

    @if (error()) {
      <p class="mb-3" role="alert">{{ error() }}</p>
    }

    <div class="overflow-x-auto">
      <table class="w-full text-left text-sm">
        <thead>
          <tr>
            <th class="p-2">Zeitpunkt</th>
            <th class="p-2">Person</th>
            <th class="p-2">Aktion</th>
            <th class="p-2">Bereich</th>
            <th class="p-2">Objekt</th>
            @if (hasValues()) {
              <th class="p-2">Werte</th>
            }
          </tr>
        </thead>
        <tbody>
          @for (e of entries(); track e.id) {
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
  `,
})
export class AuditPage {
  private readonly http = inject(HttpClient);
  private readonly config = inject(ConfigService);

  protected readonly entries = signal<AuditEntry[]>([]);
  protected readonly busy = signal(false);
  protected readonly loaded = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly hasValues = computed(() => this.entries().some((e) => e.changes !== null));

  protected readonly time = (e: AuditEntry) => formatDateTime(e.occurredAt);
  protected readonly actor = actorLabel;
  protected readonly action = (e: AuditEntry) => ACTION_LABEL[e.action] ?? e.action;

  constructor() {
    void this.load('');
  }

  protected async load(personnelNumber: string): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    const results = await Promise.all(AUDIT_SERVICES.map((s) => this.fetch(s, personnelNumber.trim())));
    this.entries.set(mergeByTime(results.filter((r): r is AuditEntry[] => r !== null), 500));
    if (results.every((r) => r === null)) {
      this.error.set('Das Protokoll konnte nicht geladen werden.');
    }
    this.loaded.set(true);
    this.busy.set(false);
  }

  /** `null` = this service gave no answer (no right, or not reachable). */
  private async fetch(service: AuditService, personnelNumber: string): Promise<AuditEntry[] | null> {
    let params = new HttpParams().set('limit', '500');
    if (personnelNumber) {
      params = params.set('personnelNumber', personnelNumber);
    }
    try {
      return await firstValueFrom(this.http.get<AuditEntry[]>(this.config.url(service, '/api/audit'), { params }));
    } catch {
      return null;
    }
  }
}
