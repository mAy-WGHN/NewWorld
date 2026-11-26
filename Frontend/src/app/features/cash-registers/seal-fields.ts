import { Component, OnDestroy, computed, effect, inject, model, signal, untracked } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { CashRegisterApi } from './cash-register.api';
import { WghnFileDrop } from '../../shared/wghn/wghn-file-drop';
import { PersonOption, SealFormValue } from './cash-register-models';

/**
 * The fields of the form of a seal: a photo of the seal and who attached it. The date and time are not asked, the service takes them. The person defaults
 * to whoever fills in the form (the page sets it), a colleague is found by name. The form is complete when both are there; the page checks that before it sends.
 */
@Component({
  selector: 'app-seal-fields',
  imports: [MatButtonModule, WghnFileDrop],
  template: `
    <fieldset class="m-0 flex min-w-0 flex-col gap-3 rounded-xl border border-outline-variant px-4 py-3" data-testid="seal-fields">
      <legend class="px-1 font-medium">Siegel</legend>

      <div class="flex flex-col gap-1">
        <span class="font-medium" id="seal-photo-label">Foto des Siegels *</span>
        <!-- the same drop zone as for the AU of a sick note: drag the photo onto it or choose it -->
        <wghn-file-drop inputId="seal-photo" accept="image/png,image/jpeg,image/webp" [file]="value().photo" (chosen)="onPhoto($event)" (cleared)="onPhoto(null)" />
        @if (previewUrl(); as url) {
          <img class="mt-1 max-h-40 w-fit rounded-lg border border-outline-variant" [src]="url" alt="Gewähltes Foto des Siegels" data-testid="seal-photo-preview" />
        }
      </div>

      <div class="flex flex-col gap-1">
        <span class="font-medium" id="seal-by-label">Siegel angebracht von *</span>
        @if (value().sealedBy; as person) {
          <div class="flex flex-wrap items-center gap-2" aria-labelledby="seal-by-label">
            <span class="rounded-full bg-surface-container px-3 py-1" data-testid="seal-person">{{ person.name }}</span>
            <button mat-stroked-button type="button" data-testid="seal-person-change" (click)="changePerson()">Andere Person wählen</button>
          </div>
        } @else {
          <input
            class="rounded-xl border border-outline-variant bg-surface px-3 py-2"
            type="search"
            placeholder="Name suchen (mindestens zwei Buchstaben)"
            aria-labelledby="seal-by-label"
            data-testid="seal-person-search"
            [value]="query()"
            (input)="onQuery($event)"
          />
          @if (results().length > 0) {
            <ul class="m-0 flex list-none flex-col gap-1 p-0" data-testid="seal-person-results">
              @for (p of results(); track p.id) {
                <li><button mat-stroked-button type="button" data-testid="seal-person-option" (click)="choose(p)">{{ p.name }}</button></li>
              }
            </ul>
          } @else if (searched()) {
            <p class="m-0 text-sm text-on-surface-variant" data-testid="seal-person-none">Niemand gefunden.</p>
          }
        }
      </div>

      <p class="m-0 text-sm text-on-surface-variant">Datum und Uhrzeit werden automatisch gespeichert.</p>
    </fieldset>
  `,
})
export class SealFields implements OnDestroy {
  private readonly api = inject(CashRegisterApi);

  readonly value = model.required<SealFormValue>();

  protected readonly query = signal('');
  protected readonly results = signal<PersonOption[]>([]);
  protected readonly searched = signal(false);
  private readonly photoUrl = signal<string | null>(null);
  protected readonly previewUrl = computed(() => this.photoUrl());
  private timer: ReturnType<typeof setTimeout> | null = null;
  private lastQuery = 0;

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
    if (this.timer) {
      clearTimeout(this.timer);
    }
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

  protected changePerson(): void {
    this.value.update((v) => ({ ...v, sealedBy: null }));
    this.query.set('');
    this.results.set([]);
    this.searched.set(false);
  }

  protected choose(person: PersonOption): void {
    this.value.update((v) => ({ ...v, sealedBy: person }));
  }

  /** Searches a moment after the last key (one request per pause); an older answer never replaces a newer one. */
  protected onQuery(event: Event): void {
    const text = (event.target as HTMLInputElement).value;
    this.query.set(text);
    if (this.timer) {
      clearTimeout(this.timer);
    }
    if (text.trim().length < 2) {
      this.results.set([]);
      this.searched.set(false);
      return;
    }
    this.timer = setTimeout(() => void this.search(text.trim()), 250);
  }

  private async search(text: string): Promise<void> {
    const id = ++this.lastQuery;
    try {
      const people = await this.api.people(text);
      if (id === this.lastQuery) {
        this.results.set(people);
        this.searched.set(true);
      }
    } catch {
      if (id === this.lastQuery) {
        this.results.set([]);
        this.searched.set(true);
      }
    }
  }
}
