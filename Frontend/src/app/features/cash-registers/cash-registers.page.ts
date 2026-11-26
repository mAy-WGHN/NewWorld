import { Component, OnDestroy, computed, inject, signal } from '@angular/core';
import { AuthService } from '../../core/auth/auth.service';
import { PAGE_UI } from '../../shared/wghn/material';
import { CashRegisterApi } from './cash-register.api';
import {
  CashRegister,
  CashRegisterEvent,
  CashRegisterList,
  PersonOption,
  ReminderSettings,
  SealFormValue,
  cashRegisterError,
  formatDateTime,
  isValidTime,
  kindLabel,
  sealFormProblem,
} from './cash-register-models';
import { SealFields } from './seal-fields';
import { FilterOption, TABLE_UI, TableState } from '../../shared/wghn/table-state';
import type { WghnColumn } from '../../shared/wghn/wghn-table';

/** The form for "Kasse erfassen" (no `id`) and "Bearbeiten" (with the `id` of the register). */
interface Draft {
  id: string | null;
  storeId: string;
  name: string;
  /** Whether the drawer is sealed: the seal can be set or lifted when recording and editing. */
  sealed: boolean;
  /** Whether it was sealed when the form was opened: a seal that is there already needs no new form. */
  wasSealed: boolean;
  /** The form of a new seal: photo and who attached it. */
  seal: SealFormValue;
}

/** The form of "Siegel erneuern" for one register. */
interface Sealing {
  register: CashRegister;
  seal: SealFormValue;
}

/**
 * "Kassenregister" (Filialbetrieb): the cash registers of the stores with their seal. The seal is a plain state that is set explicitly. **Setting a seal is a
 * form**: a photo of the seal and who attached it (the person who fills in the form is the default), date and time are taken by the service; "Siegel aufheben"
 * (or taking the tick away when editing) marks the drawer as not sealed and needs nothing. The photo of a seal can be looked at on the row and in the
 * history. The screen only guides the person (what the buttons offer follows the privileges), the service decides and limits everything to the stores of
 * the privilege.
 */
@Component({
  selector: 'app-cash-registers',
  imports: [...PAGE_UI, ...TABLE_UI, SealFields],
  templateUrl: './cash-registers.page.html',
})
export class CashRegistersPage implements OnDestroy {
  private readonly api = inject(CashRegisterApi);
  private readonly auth = inject(AuthService);

  protected readonly canRecord = computed(() => this.auth.privileges().has('cashregister:record'));
  protected readonly canDelete = computed(() => this.auth.privileges().has('cashregister:delete'));

  protected readonly canManageReminders = computed(() => this.auth.privileges().has('cashregister:manage-reminders'));
  protected readonly reminder = signal<ReminderSettings | null>(null);
  protected readonly reminderStatus = signal<string | null>(null);

  protected readonly data = signal<CashRegisterList | null>(null);
  protected readonly loading = signal(false);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly status = signal<string | null>(null);

  protected readonly storeFilter = signal('');
  protected readonly draft = signal<Draft | null>(null);
  protected readonly sealing = signal<Sealing | null>(null);
  protected readonly confirmDelete = signal<string | null>(null);
  protected readonly showHistory = signal(false);
  protected readonly history = signal<CashRegisterEvent[] | null>(null);
  protected readonly photoView = signal<{ url: string; title: string } | null>(null);

  protected readonly formatDateTime = formatDateTime;
  protected readonly kindLabel = kindLabel;

  /** The registers with sorting, the status filter, the store filter, the search and paging (the library's table state). */
  protected readonly table = new TableState<CashRegister>(() => this.data()?.items ?? [], {
    columns: {
      store: (r) => r.storeNumber,
      name: (r) => r.name,
      status: (r) => (r.sealed ? 1 : 0),
      recorded: (r) => r.lastRecordedAt,
      by: (r) => r.recordedBy,
    },
    sortKey: 'store',
    search: (r) => `${r.storeNumber} ${r.storeName} ${r.name}`,
    filters: {
      status: (r, value) => (value === 'sealed') === r.sealed,
      store: (r, value) => r.storeId === value,
    },
  });

  protected readonly historyTable = new TableState<CashRegisterEvent>(() => this.history() ?? [], {
    columns: { at: (e) => e.at, store: (e) => e.storeNumber, register: (e) => e.registerName, kind: (e) => e.kind, by: (e) => e.by },
    sortKey: 'at',
    sortDir: 'desc',
  });

  protected readonly statusOptions: FilterOption[] = [
    { value: '', label: 'Alle' },
    { value: 'sealed', label: 'Versiegelt' },
    { value: 'notSealed', label: 'Nicht versiegelt' },
  ];

  protected readonly columns: WghnColumn[] = [
    { key: 'store', header: 'Filiale', sortable: true, rowHeader: true },
    { key: 'name', header: 'Kasse', sortable: true },
    { key: 'status', header: 'Status', sortable: true },
    { key: 'recorded', header: 'Zuletzt erfasst', sortable: true, nowrap: true },
    { key: 'by', header: 'Kürzel', sortable: true },
    { key: 'actions', header: 'Aktionen', hideHeader: true },
  ];

  protected readonly historyColumns: WghnColumn[] = [
    { key: 'at', header: 'Zeitpunkt', sortable: true, nowrap: true },
    { key: 'store', header: 'Filiale', sortable: true, class: 'font-semibold' },
    { key: 'register', header: 'Kasse', sortable: true },
    { key: 'kind', header: 'Was' },
    { key: 'by', header: 'Kürzel', sortable: true },
  ];

  protected readonly rowId = (r: CashRegister): string => r.id;

  /** The person who is filling in the form: the default for "who attached the seal". */
  private readonly me = computed<PersonOption | null>(() => {
    const id = this.auth.employeeId();
    const name = this.auth.name();
    return id && name ? { id, name } : null;
  });

  constructor() {
    void this.load();
    if (this.canManageReminders()) {
      void this.loadReminder();
    }
  }

  ngOnDestroy(): void {
    this.closePhoto();
  }

  private async loadReminder(): Promise<void> {
    try {
      this.reminder.set(await this.api.reminderSettings());
    } catch (error) {
      this.error.set(cashRegisterError(error));
    }
  }

  protected setReminder(field: 'enabled' | 'time', event: Event): void {
    const target = event.target as HTMLInputElement;
    this.reminderStatus.set(null);
    this.reminder.update((r) => (r ? { ...r, [field]: field === 'enabled' ? target.checked : target.value } : r));
  }

  protected async saveReminder(): Promise<void> {
    const settings = this.reminder();
    if (!settings) {
      return;
    }
    if (!isValidTime(settings.time)) {
      this.error.set('Bitte eine Uhrzeit im Format HH:mm angeben, z. B. 18:00.');
      return;
    }
    this.error.set(null);
    this.reminderStatus.set(null);
    try {
      this.reminder.set(await this.api.saveReminderSettings(settings));
      this.reminderStatus.set(settings.enabled ? `Die Erinnerung geht täglich ab ${settings.time} Uhr aus.` : 'Die Erinnerung ist ausgeschaltet.');
    } catch (error) {
      this.error.set(cashRegisterError(error));
    }
  }

  protected async load(): Promise<void> {
    this.loading.set(true);
    try {
      this.data.set(await this.api.list());
      this.error.set(null);
    } catch (error) {
      this.error.set(cashRegisterError(error));
    } finally {
      this.loading.set(false);
    }
  }

  private emptySeal(): SealFormValue {
    return { photo: null, sealedBy: this.me() };
  }

  protected startCreate(): void {
    const stores = this.data()?.stores ?? [];
    this.status.set(null);
    this.sealing.set(null);
    this.draft.set({ id: null, storeId: this.storeFilter() || (stores.length === 1 ? stores[0].id : ''), name: '', sealed: false, wasSealed: false, seal: this.emptySeal() });
  }

  protected startEdit(register: CashRegister): void {
    this.status.set(null);
    this.sealing.set(null);
    this.draft.set({ id: register.id, storeId: register.storeId, name: register.name, sealed: register.sealed, wasSealed: register.sealed, seal: this.emptySeal() });
  }

  protected setDraft(field: 'storeId' | 'name', event: Event): void {
    const value = (event.target as HTMLInputElement | HTMLSelectElement).value;
    this.draft.update((d) => (d ? { ...d, [field]: value } : d));
  }

  protected setDraftSealed(event: Event): void {
    const sealed = (event.target as HTMLInputElement).checked;
    this.draft.update((d) => (d ? { ...d, sealed } : d));
  }

  protected setDraftSeal(seal: SealFormValue): void {
    this.draft.update((d) => (d ? { ...d, seal } : d));
  }

  /** A new seal (ticked, and not sealed before) needs the form with photo and person. */
  protected needsSealForm(draft: Draft): boolean {
    return draft.sealed && !draft.wasSealed;
  }

  protected async save(): Promise<void> {
    const draft = this.draft();
    if (!draft) {
      return;
    }
    if (!draft.storeId || !draft.name.trim()) {
      this.error.set('Bitte Filiale und Namen der Kasse angeben.');
      return;
    }
    const needsForm = this.needsSealForm(draft);
    const problem = needsForm ? sealFormProblem(draft.seal) : null;
    if (problem) {
      this.error.set(problem);
      return;
    }
    await this.act(
      async () => {
        const payload = needsForm ? await this.uploadSeal(draft.seal) : null;
        if (draft.id) {
          await this.api.update(draft.id, draft.storeId, draft.name.trim(), draft.sealed, payload);
        } else {
          await this.api.create(draft.storeId, draft.name.trim(), payload);
        }
        this.draft.set(null);
      },
      draft.id ? 'Die Kasse wurde bearbeitet.' : 'Die Kasse wurde erfasst.',
    );
  }

  /** The photo goes up first; its id and the person who attached the seal are the form that is sent. */
  private async uploadSeal(seal: SealFormValue): Promise<{ photoId: string; sealedBy: string }> {
    const photoId = await this.api.uploadPhoto(seal.photo as File);
    return { photoId, sealedBy: (seal.sealedBy as PersonOption).id };
  }

  /** "Siegel erneuern": the form of a seal for this register. */
  protected startRenew(register: CashRegister): void {
    this.status.set(null);
    this.error.set(null);
    this.draft.set(null);
    this.sealing.set({ register, seal: this.emptySeal() });
  }

  protected setSealingSeal(seal: SealFormValue): void {
    this.sealing.update((s) => (s ? { ...s, seal } : s));
  }

  protected async submitSealing(): Promise<void> {
    const form = this.sealing();
    if (!form) {
      return;
    }
    const problem = sealFormProblem(form.seal);
    if (problem) {
      this.error.set(problem);
      return;
    }
    await this.act(async () => {
      await this.api.seal(form.register.id, await this.uploadSeal(form.seal));
      this.sealing.set(null);
    }, `Das Siegel von „${form.register.name}“ wurde gesetzt.`);
  }

  protected async liftSeal(register: CashRegister): Promise<void> {
    await this.act(() => this.api.unseal(register.id), `Das Siegel von „${register.name}“ wurde aufgehoben, die Kasse gilt als nicht versiegelt.`);
  }

  protected async remove(register: CashRegister): Promise<void> {
    if (this.confirmDelete() !== register.id) {
      this.confirmDelete.set(register.id);
      return;
    }
    await this.act(async () => {
      await this.api.delete(register.id);
      this.confirmDelete.set(null);
    }, `Die Kasse „${register.name}“ wurde gelöscht.`);
  }

  /** Looks at the photo of a seal (loaded with the sign-in and shown from a temporary address). */
  protected async showPhoto(photoId: string, title: string): Promise<void> {
    try {
      const blob = await this.api.photo(photoId);
      this.closePhoto();
      this.photoView.set({ url: URL.createObjectURL(blob), title });
      this.error.set(null);
    } catch (error) {
      this.error.set(cashRegisterError(error));
    }
  }

  protected closePhoto(): void {
    const view = this.photoView();
    if (view) {
      URL.revokeObjectURL(view.url);
      this.photoView.set(null);
    }
  }

  protected async toggleHistory(): Promise<void> {
    const show = !this.showHistory();
    this.showHistory.set(show);
    if (show) {
      await this.loadHistory();
    }
  }

  protected async loadHistory(): Promise<void> {
    try {
      this.history.set(await this.api.history(this.storeFilter() || undefined));
      this.error.set(null);
    } catch (error) {
      this.error.set(cashRegisterError(error));
    }
  }

  protected async setStore(event: Event): Promise<void> {
    this.storeFilter.set((event.target as HTMLSelectElement).value);
    this.table.setFilter('store', this.storeFilter());
    if (this.showHistory()) {
      await this.loadHistory();
    }
  }

  /** Runs a change, reloads the list (and the history if it is open) and says what happened; a failure becomes a plain German message. */
  private async act(change: () => Promise<unknown>, done: string): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    this.status.set(null);
    try {
      await change();
      this.status.set(done);
      await this.load();
      if (this.showHistory()) {
        await this.loadHistory();
      }
    } catch (error) {
      this.error.set(cashRegisterError(error));
    } finally {
      this.busy.set(false);
    }
  }
}
