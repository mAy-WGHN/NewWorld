import { Component, OnDestroy, computed, effect, inject, model, signal, untracked } from '@angular/core';
import { PersonSource, WghnPerson, WghnPersonPicker } from '../../shared/wghn/wghn-person-picker';
import { WghnFileDrop } from '../../shared/wghn/wghn-file-drop';
import { CashRegisterApi } from './cash-register.api';
import { SealFormValue } from './cash-register-models';

/**
 * The fields of the form of a seal: a photo of the seal (the library's drop zone, like the AU of a sick note) and who attached it (the library's person
 * picker). The date and time are not asked, the service takes them. The person defaults to whoever fills in the form (the page sets it), a colleague is
 * found by name. The form is complete when both are there; the page checks that before it sends.
 */
@Component({
  selector: 'app-seal-fields',
  imports: [WghnFileDrop, WghnPersonPicker],
  template: `
    <fieldset class="m-0 flex min-w-0 flex-col gap-3 rounded-xl border border-outline-variant px-4 py-3" data-testid="seal-fields">
      <legend class="px-1 font-medium">Siegel</legend>

      <div class="flex flex-col gap-1">
        <span class="font-medium" id="seal-photo-label">Foto des Siegels *</span>
        <wghn-file-drop inputId="seal-photo" accept="image/png,image/jpeg,image/webp" [file]="value().photo" (chosen)="onPhoto($event)" (cleared)="onPhoto(null)" />
        @if (previewUrl(); as url) {
          <img class="mt-1 max-h-40 w-fit rounded-lg border border-outline-variant" [src]="url" alt="Gewähltes Foto des Siegels" data-testid="seal-photo-preview" />
        }
      </div>

      <wghn-person-picker label="Siegel angebracht von *" inputId="seal-by" [selectedId]="value().sealedBy?.id ?? null" [source]="people" (picked)="onPerson($event)" />

      <p class="m-0 text-sm text-on-surface-variant">Datum und Uhrzeit werden automatisch gespeichert.</p>
    </fieldset>
  `,
})
export class SealFields implements OnDestroy {
  private readonly api = inject(CashRegisterApi);

  readonly value = model.required<SealFormValue>();

  private readonly photoUrl = signal<string | null>(null);
  protected readonly previewUrl = computed(() => this.photoUrl());

  /** The people to choose from: the name search of the portal service; the person already chosen is known by name without asking. */
  protected readonly people: PersonSource = {
    search: async (query) => (await this.api.people(query)).map((p) => ({ employeeId: p.id, name: p.name })),
    byIds: async (ids) => {
      const chosen = this.value().sealedBy;
      return chosen && ids.includes(chosen.id) ? [{ employeeId: chosen.id, name: chosen.name }] : [];
    },
  };

  constructor() {
    // a photo that was chosen before (the page keeps the value) shows again; the address is made here and given back in ngOnDestroy
    effect(() => {
      const photo = this.value().photo;
      // only the chosen photo is watched: reading and setting the address here must not trigger the effect again
      untracked(() => {
        this.release();
        this.photoUrl.set(photo && typeof URL.createObjectURL === 'function' ? URL.createObjectURL(photo) : null);
      });
    });
  }

  ngOnDestroy(): void {
    this.release();
  }

  private release(): void {
    const url = this.photoUrl();
    if (url && typeof URL.revokeObjectURL === 'function') {
      URL.revokeObjectURL(url);
    }
  }

  protected onPhoto(file: File | null): void {
    this.value.update((v) => ({ ...v, photo: file }));
  }

  protected onPerson(person: WghnPerson | null): void {
    this.value.update((v) => ({ ...v, sealedBy: person ? { id: person.employeeId, name: person.name } : null }));
  }
}
