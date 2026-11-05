import { Component, computed, inject, signal } from '@angular/core';
import { AuthService } from '../../core/auth/auth.service';
import { PAGE_UI } from '../../shared/ui/material';
import { LIST_KINDS, NumberSeries, ReferenceEntry, SeriesForm, describeError, loadFailed, seriesForm, seriesRequest } from './admin-hr-models';
import { AdminHrApi } from './admin-hr.api';

/**
 * "Mitarbeiterkarte" (decision 29): the personnel number series (`employee:manage-number-series`) and the selection lists for the card
 * (`employee:manage-lists`). Entries of a list can only be deactivated, never deleted, and the code is fixed after creation.
 */
@Component({
  selector: 'app-employee-card-section',
  imports: [...PAGE_UI],
  templateUrl: './employee-card-section.html',
})
export class EmployeeCardSection {
  private readonly api = inject(AdminHrApi);
  private readonly auth = inject(AuthService);

  protected readonly canSeries = computed(() => this.auth.privileges().has('employee:manage-number-series'));
  protected readonly canLists = computed(() => this.auth.privileges().has('employee:manage-lists'));
  protected readonly kinds = LIST_KINDS;

  // number series
  protected readonly series = signal<NumberSeries | null>(null);
  protected readonly seriesForm = signal<SeriesForm>({ last: '', start: '', end: '' });
  protected readonly seriesBusy = signal(false);
  protected readonly seriesError = signal<string | null>(null);
  protected readonly seriesStatus = signal<string | null>(null);
  protected readonly seriesSet = computed(() => this.series()?.lastNumber != null);

  // lists
  protected readonly kind = signal<string>(LIST_KINDS[0].kind);
  protected readonly entries = signal<ReferenceEntry[] | null>(null);
  protected readonly listError = signal<string | null>(null);
  protected readonly listStatus = signal<string | null>(null);
  protected readonly listBusy = signal(false);
  protected readonly newCode = signal('');
  protected readonly newName = signal('');
  protected readonly editing = signal<{ id: string; name: string } | null>(null);
  protected readonly kindLabel = computed(() => LIST_KINDS.find((k) => k.kind === this.kind())?.label ?? '');

  constructor() {
    if (this.canSeries()) {
      void this.loadSeries();
    }
    if (this.canLists()) {
      void this.loadList();
    }
  }

  // ---------------------------------------------------------------- number series

  private async loadSeries(): Promise<void> {
    try {
      const s = await this.api.numberSeries();
      this.series.set(s);
      this.seriesForm.set(seriesForm(s));
    } catch {
      this.seriesError.set(loadFailed('Der Nummernkreis'));
    }
  }

  protected seriesText(field: keyof SeriesForm, event: Event): void {
    const value = (event.target as HTMLInputElement).value;
    this.seriesForm.update((f) => ({ ...f, [field]: value }));
    this.seriesStatus.set(null);
  }

  protected async saveSeries(): Promise<void> {
    const request = seriesRequest(this.seriesForm(), this.series() ?? { lastNumber: null });
    if (typeof request === 'string') {
      this.seriesError.set(request);
      return;
    }
    this.seriesBusy.set(true);
    this.seriesError.set(null);
    this.seriesStatus.set(null);
    try {
      const saved = await this.api.saveNumberSeries(request);
      this.series.set(saved);
      this.seriesForm.set(seriesForm(saved));
      this.seriesStatus.set('Der Nummernkreis ist gespeichert.');
    } catch (e) {
      this.seriesError.set(describeError(e));
    } finally {
      this.seriesBusy.set(false);
    }
  }

  // ---------------------------------------------------------------- selection lists

  protected async selectKind(event: Event): Promise<void> {
    this.kind.set((event.target as HTMLSelectElement).value);
    this.editing.set(null);
    this.newCode.set('');
    this.newName.set('');
    await this.loadList();
  }

  private async loadList(): Promise<void> {
    this.entries.set(null);
    this.listError.set(null);
    this.listStatus.set(null);
    const kind = this.kind();
    try {
      const entries = await this.api.listEntries(kind);
      if (kind === this.kind()) {
        this.entries.set(entries);
      }
    } catch {
      if (kind === this.kind()) {
        this.listError.set(loadFailed('Die Einträge der Liste'));
      }
    }
  }

  protected text(target: { set(v: string): void }, event: Event): void {
    target.set((event.target as HTMLInputElement).value);
  }

  protected async add(): Promise<void> {
    const code = this.newCode().trim();
    const name = this.newName().trim();
    if (!code || !name) {
      this.listError.set('Bitte Code und Bezeichnung eintragen.');
      return;
    }
    if (code.length > 30) {
      this.listError.set('Der Code darf höchstens 30 Zeichen lang sein.');
      return;
    }
    await this.run(async () => {
      const entry = await this.api.addEntry(this.kind(), code, name);
      this.entries.update((list) => [...(list ?? []), entry]);
      this.newCode.set('');
      this.newName.set('');
      return `„${name}“ ist hinzugefügt.`;
    });
  }

  protected edit(entry: ReferenceEntry): void {
    this.editing.set({ id: entry.id, name: entry.name });
  }

  protected editName(event: Event): void {
    const name = (event.target as HTMLInputElement).value;
    this.editing.update((e) => (e ? { ...e, name } : e));
  }

  protected async saveName(entry: ReferenceEntry): Promise<void> {
    const name = this.editing()?.name.trim() ?? '';
    if (!name) {
      this.listError.set('Die Bezeichnung darf nicht leer sein.');
      return;
    }
    await this.run(async () => {
      await this.api.updateEntry(this.kind(), entry.id, name, entry.isActive);
      this.replace(entry.id, { name });
      this.editing.set(null);
      return 'Die Bezeichnung ist gespeichert.';
    });
  }

  protected async setActive(entry: ReferenceEntry, isActive: boolean): Promise<void> {
    await this.run(async () => {
      await this.api.updateEntry(this.kind(), entry.id, entry.name, isActive);
      this.replace(entry.id, { isActive });
      return isActive ? `„${entry.name}“ ist wieder aktiv.` : `„${entry.name}“ ist deaktiviert und steht nicht mehr zur Auswahl.`;
    });
  }

  private replace(id: string, change: Partial<ReferenceEntry>): void {
    this.entries.update((list) => list?.map((e) => (e.id === id ? { ...e, ...change } : e)) ?? null);
  }

  private async run(action: () => Promise<string>): Promise<void> {
    this.listBusy.set(true);
    this.listError.set(null);
    this.listStatus.set(null);
    try {
      this.listStatus.set(await action());
    } catch (e) {
      this.listError.set(describeError(e));
    } finally {
      this.listBusy.set(false);
    }
  }
}
