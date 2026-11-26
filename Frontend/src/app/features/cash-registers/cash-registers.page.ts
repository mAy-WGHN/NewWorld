import { Component, computed, inject, signal } from '@angular/core';
import { AuthService } from '../../core/auth/auth.service';
import { PAGE_UI } from '../../shared/ui/material';
import { CashRegisterApi } from './cash-register.api';
import {
  CashRegister,
  CashRegisterEvent,
  CashRegisterList,
  STATUS_FILTERS,
  StatusFilter,
  cashRegisterError,
  filterRegisters,
  ReminderSettings,
  formatDateTime,
  isValidTime,
  kindLabel,
} from './cash-register-models';

/** The form for "Kasse erfassen" (no `id`) and "Bearbeiten" (with the `id` of the register). */
interface Draft {
  id: string | null;
  storeId: string;
  name: string;
}

/**
 * "Kassenregister" (Filialbetrieb): the cash registers of the stores with their seal. Recording or editing a register removes its seal, "Siegel erneuern"
 * sets it. "Filialhistorie" shows every step. The screen only guides the person (what the buttons offer follows the privileges), the service decides
 * and limits everything to the stores of the privilege.
 */
@Component({
  selector: 'app-cash-registers',
  imports: [...PAGE_UI],
  templateUrl: './cash-registers.page.html',
})
export class CashRegistersPage {
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

  protected readonly statusFilter = signal<StatusFilter>('all');
  protected readonly storeFilter = signal('');
  protected readonly query = signal('');
  protected readonly draft = signal<Draft | null>(null);
  protected readonly confirmDelete = signal<string | null>(null);
  protected readonly showHistory = signal(false);
  protected readonly history = signal<CashRegisterEvent[] | null>(null);

  protected readonly filters = STATUS_FILTERS;
  protected readonly formatDateTime = formatDateTime;
  protected readonly kindLabel = kindLabel;

  protected readonly rows = computed(() => filterRegisters(this.data()?.items ?? [], this.statusFilter(), this.storeFilter(), this.query()));

  constructor() {
    void this.load();
    if (this.canManageReminders()) {
      void this.loadReminder();
    }
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

  protected startCreate(): void {
    const stores = this.data()?.stores ?? [];
    this.status.set(null);
    this.draft.set({ id: null, storeId: this.storeFilter() || (stores.length === 1 ? stores[0].id : ''), name: '' });
  }

  protected startEdit(register: CashRegister): void {
    this.status.set(null);
    this.draft.set({ id: register.id, storeId: register.storeId, name: register.name });
  }

  protected setDraft(field: 'storeId' | 'name', event: Event): void {
    const value = (event.target as HTMLInputElement | HTMLSelectElement).value;
    this.draft.update((d) => (d ? { ...d, [field]: value } : d));
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
    await this.act(
      async () => {
        if (draft.id) {
          await this.api.update(draft.id, draft.storeId, draft.name.trim());
        } else {
          await this.api.create(draft.storeId, draft.name.trim());
        }
        this.draft.set(null);
      },
      draft.id ? 'Die Kasse wurde bearbeitet und neu erfasst, das Siegel entfällt.' : 'Die Kasse wurde erfasst.',
    );
  }

  protected async renewSeal(register: CashRegister): Promise<void> {
    await this.act(() => this.api.seal(register.id), `Das Siegel von „${register.name}“ wurde erneuert.`);
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
    if (this.showHistory()) {
      await this.loadHistory();
    }
  }

  protected setQuery(event: Event): void {
    this.query.set((event.target as HTMLInputElement).value);
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
