import { NgTemplateOutlet } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { PAGE_UI } from '../../shared/ui/material';
import { KindForm, UnavailabilityKind, describeError, emptyKindForm, kindToForm, loadFailed } from './admin-hr-models';
import { AdminHrApi } from './admin-hr.api';

/**
 * "Nicht-Verfügbarkeitsarten" (decision 32): the kinds HR can enter for a person (special leave, training, ...). Kinds are only deactivated,
 * never deleted, and the code stays fixed. The system kinds (KRANK, URLAUB, KU) come from the absences and cannot be changed. Needs
 * `unavailability:manage-kinds`.
 */
@Component({
  selector: 'app-unavailability-section',
  imports: [...PAGE_UI, NgTemplateOutlet],
  templateUrl: './unavailability-section.html',
})
export class UnavailabilitySection {
  private readonly api = inject(AdminHrApi);

  protected readonly kinds = signal<UnavailabilityKind[] | null>(null);
  protected readonly loadError = signal<string | null>(null);
  protected readonly error = signal<string | null>(null);
  protected readonly status = signal<string | null>(null);
  protected readonly busy = signal(false);
  protected readonly adding = signal<KindForm>(emptyKindForm());
  protected readonly editing = signal<{ id: string; form: KindForm } | null>(null);

  constructor() {
    void this.load();
  }

  private async load(): Promise<void> {
    try {
      this.kinds.set(await this.api.kinds());
    } catch {
      this.loadError.set(loadFailed('Die Arten'));
    }
  }

  protected field(target: 'adding' | 'editing', name: keyof KindForm, event: Event): void {
    const el = event.target as HTMLInputElement;
    const value = el.type === 'checkbox' ? el.checked : el.value;
    if (target === 'adding') {
      this.adding.update((f) => ({ ...f, [name]: value }));
    } else {
      this.editing.update((e) => (e ? { ...e, form: { ...e.form, [name]: value } } : e));
    }
    this.status.set(null);
  }

  protected edit(kind: UnavailabilityKind): void {
    this.editing.set({ id: kind.id, form: kindToForm(kind) });
  }

  private problem(form: KindForm, withCode: boolean): string | null {
    if (withCode && (!form.code.trim() || form.code.trim().length > 30)) {
      return 'Bitte einen Code mit höchstens 30 Zeichen eintragen.';
    }
    return form.description.trim() ? null : 'Bitte eine Beschreibung eintragen.';
  }

  protected async add(): Promise<void> {
    const form = this.adding();
    const problem = this.problem(form, true);
    if (problem) {
      this.error.set(problem);
      return;
    }
    await this.run(async () => {
      const kind = await this.api.addKind(form);
      this.kinds.update((list) => [...(list ?? []), kind]);
      this.adding.set(emptyKindForm());
      return `„${kind.description}“ ist hinzugefügt.`;
    });
  }

  protected async saveEdit(kind: UnavailabilityKind): Promise<void> {
    const current = this.editing();
    if (!current) {
      return;
    }
    const problem = this.problem(current.form, false);
    if (problem) {
      this.error.set(problem);
      return;
    }
    await this.run(async () => {
      await this.api.updateKind(kind.id, current.form);
      this.replace(kind.id, {
        description: current.form.description.trim(),
        paidTime: current.form.paidTime,
        scheduleSymbol: current.form.scheduleSymbol.trim() || null,
        workCode: current.form.workCode.trim() || null,
      });
      this.editing.set(null);
      return 'Die Änderungen sind gespeichert.';
    });
  }

  protected async setActive(kind: UnavailabilityKind, isActive: boolean): Promise<void> {
    await this.run(async () => {
      await this.api.updateKind(kind.id, kindToForm(kind), isActive);
      this.replace(kind.id, { isActive });
      return isActive ? `„${kind.description}“ ist wieder aktiv.` : `„${kind.description}“ ist deaktiviert und lässt sich nicht mehr eintragen.`;
    });
  }

  private replace(id: string, change: Partial<UnavailabilityKind>): void {
    this.kinds.update((list) => list?.map((k) => (k.id === id ? { ...k, ...change } : k)) ?? null);
  }

  private async run(action: () => Promise<string>): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    this.status.set(null);
    try {
      this.status.set(await action());
    } catch (e) {
      this.error.set(describeError(e));
    } finally {
      this.busy.set(false);
    }
  }
}
