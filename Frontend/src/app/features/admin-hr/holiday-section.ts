import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { PAGE_UI } from '../../shared/ui/material';
import { HolidayState, MAX_BULK_STORES, describeError, loadFailed } from './admin-hr-models';
import { AdminHrApi, StoreLocation } from './admin-hr.api';

/** How many stores are asked for their state at the same time (the service only offers it per store). */
export const STATE_CONCURRENCY = 5;

const normalize = (text: string): string => text.normalize('NFD').replace(/\p{M}/gu, '').toLowerCase().trim();

/**
 * "Feiertage" (decisions 23 and 26): public holidays are closing days unless a store is marked as open on holidays (railway-station
 * stores). The state is read per store (`GET /api/stores/{id}/holiday-opening`, five at a time), changed for many stores at once
 * (all or none). Needs `absence:manage-holiday-opening`, scoped to stores; stores outside the reach cannot be selected.
 */
@Component({
  selector: 'app-holiday-section',
  imports: [...PAGE_UI],
  templateUrl: './holiday-section.html',
})
export class HolidaySection {
  private readonly api = inject(AdminHrApi);

  protected readonly stores = signal<StoreLocation[] | null>(null);
  protected readonly loadError = signal<string | null>(null);
  protected readonly states = signal<ReadonlyMap<string, HolidayState>>(new Map());
  protected readonly query = signal('');
  protected readonly selected = signal<ReadonlySet<string>>(new Set());
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly status = signal<string | null>(null);

  protected readonly maxBulk = MAX_BULK_STORES;
  private alive = true;

  protected readonly visible = computed(() => {
    const q = normalize(this.query());
    return (this.stores() ?? []).filter((s) => !q || normalize(s.name).includes(q) || normalize(s.storeCode).includes(q));
  });
  protected readonly loadedCount = computed(() => [...this.states().values()].filter((s) => s !== 'unknown').length);
  protected readonly stateLoading = computed(() => (this.stores()?.length ?? 0) > this.loadedCount());
  /** The shown stores whose state is known and may be changed. */
  protected readonly selectable = computed(() => this.visible().filter((s) => this.canSelect(s.id)));
  protected readonly selectedCount = computed(() => this.selected().size);

  constructor() {
    inject(DestroyRef).onDestroy(() => (this.alive = false));
    void this.load();
  }

  protected stateOf(id: string): HolidayState {
    return this.states().get(id) ?? 'unknown';
  }

  protected canSelect(id: string): boolean {
    const s = this.stateOf(id);
    return s === 'open' || s === 'closed';
  }

  protected stateLabel(id: string): string {
    switch (this.stateOf(id)) {
      case 'open':
        return 'An Feiertagen geöffnet';
      case 'closed':
        return 'An Feiertagen geschlossen';
      case 'blocked':
        return 'Nicht änderbar (außerhalb deiner Reichweite oder noch nicht bekannt)';
      default:
        return 'Wird geladen …';
    }
  }

  private async load(): Promise<void> {
    let stores: StoreLocation[];
    try {
      stores = (await this.api.stores()).filter((s) => !s.archived).sort((a, b) => a.name.localeCompare(b.name, 'de'));
    } catch {
      this.loadError.set(loadFailed('Die Filialen'));
      return;
    }
    this.stores.set(stores);
    const queue = [...stores];
    const worker = async (): Promise<void> => {
      for (let store = queue.shift(); store && this.alive; store = queue.shift()) {
        await this.loadState(store.id);
      }
    };
    await Promise.all(Array.from({ length: Math.min(STATE_CONCURRENCY, stores.length) }, worker));
  }

  private async loadState(id: string): Promise<void> {
    let state: HolidayState;
    try {
      state = (await this.api.holidayOpening(id)).opensOnHolidays ? 'open' : 'closed';
    } catch {
      // 403: outside the reach; 404: the store is not known to Workforce (yet). Either way nothing can be changed here.
      state = 'blocked';
    }
    this.setStates([id], state);
  }

  private setStates(ids: readonly string[], state: HolidayState): void {
    this.states.update((old) => {
      const next = new Map(old);
      ids.forEach((id) => next.set(id, state));
      return next;
    });
  }

  protected setQuery(event: Event): void {
    this.query.set((event.target as HTMLInputElement).value);
  }

  protected toggle(id: string, checked: boolean): void {
    this.selected.update((old) => {
      const next = new Set(old);
      if (checked) {
        next.add(id);
      } else {
        next.delete(id);
      }
      return next;
    });
    this.status.set(null);
  }

  protected selectShown(): void {
    this.selected.update((old) => new Set([...old, ...this.selectable().map((s) => s.id)]));
  }

  protected clearSelection(): void {
    this.selected.set(new Set());
  }

  protected async apply(opensOnHolidays: boolean): Promise<void> {
    const ids = [...this.selected()].filter((id) => this.canSelect(id));
    if (ids.length === 0 || this.busy()) {
      return;
    }
    if (ids.length > MAX_BULK_STORES) {
      this.error.set(`Es lassen sich höchstens ${MAX_BULK_STORES} Filialen auf einmal ändern. Bitte wähle weniger aus.`);
      return;
    }
    this.busy.set(true);
    this.error.set(null);
    this.status.set(null);
    try {
      await this.api.setHolidayOpening(ids, opensOnHolidays);
      this.setStates(ids, opensOnHolidays ? 'open' : 'closed');
      this.selected.set(new Set());
      this.status.set(
        `${ids.length} ${ids.length === 1 ? 'Filiale ist' : 'Filialen sind'} jetzt ${opensOnHolidays ? 'an Feiertagen geöffnet' : 'an Feiertagen geschlossen'} (gilt auch rückwirkend für die Berechnung von Urlaubstagen und Abwesenheiten).`,
      );
    } catch (e) {
      this.error.set(`${describeError(e)} Es wurde keine Filiale geändert.`);
    } finally {
      this.busy.set(false);
    }
  }
}
