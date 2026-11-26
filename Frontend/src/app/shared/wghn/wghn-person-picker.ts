import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, effect, inject, input, output, signal, untracked } from '@angular/core';
import { PAGE_UI } from './material';

/** A person to choose: what the picker needs to show (name, number, position, exit date are shown when there). */
export interface WghnPerson {
  employeeId: string;
  name: string;
  personnelNumber?: string | null;
  homeStoreId?: string | null;
  position?: string | null;
  exitDate?: string | null;
}

/** Where the people come from: a search by text and a lookup by ids (to show a name for an id the page already has). */
export interface PersonSource {
  search(query: string): Promise<readonly WghnPerson[]>;
  byIds(ids: readonly string[]): Promise<readonly WghnPerson[]>;
}

export const PERSON_SEARCH_MIN = 2;

const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
export const isGuid = (text: string): boolean => GUID.test(text.trim());

/**
 * Choose a person by name or personnel number (a pasted id works, too). The search starts shortly after typing; what is offered is up to the
 * `source` (the service only offers people the signed-in person may see). The chosen person is shown with name and number and can be changed. The page
 * keeps the choice (`selectedId`): the picker looks the name up if it only gets the id.
 */
@Component({
  selector: 'wghn-person-picker',
  imports: [...PAGE_UI],
  templateUrl: './wghn-person-picker.html',
})
export class WghnPersonPicker {
  readonly source = input.required<PersonSource>();

  readonly label = input.required<string>();
  readonly inputId = input.required<string>();
  readonly selectedId = input<string | null>(null);
  readonly disabled = input(false);
  /** Wait after the last key before asking the service. */
  readonly debounceMs = input(300);
  readonly picked = output<WghnPerson | null>();

  protected readonly query = signal('');
  protected readonly results = signal<WghnPerson[]>([]);
  protected readonly searching = signal(false);
  /** A search for the current text has finished (so "nobody found" can be said). */
  protected readonly searched = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly selected = signal<WghnPerson | null>(null);

  private timer: ReturnType<typeof setTimeout> | undefined;
  private round = 0;

  constructor() {
    effect(() => {
      const id = this.selectedId();
      untracked(() => void this.sync(id));
    });
    inject(DestroyRef).onDestroy(() => clearTimeout(this.timer));
  }

  /** Shows the person for an id that the page gives (looks the name up; shows the id if the person is unknown or out of reach). */
  private async sync(id: string | null): Promise<void> {
    if (!id) {
      this.selected.set(null);
      return;
    }
    if (this.selected()?.employeeId === id) {
      return;
    }
    this.selected.set({ employeeId: id, personnelNumber: null, name: id });
    try {
      const [person] = await this.source().byIds([id]);
      if (person && this.selectedId() === id) {
        this.selected.set(person);
      }
    } catch {
      // the id stays as the name
    }
  }

  protected onInput(event: Event): void {
    const text = (event.target as HTMLInputElement).value;
    this.query.set(text);
    this.error.set(null);
    this.searched.set(false);
    clearTimeout(this.timer);
    const trimmed = text.trim();
    if (trimmed.length < PERSON_SEARCH_MIN && !isGuid(trimmed)) {
      this.results.set([]);
      this.round++;
      this.searching.set(false);
      return;
    }
    this.timer = setTimeout(() => void this.search(trimmed), this.debounceMs());
  }

  private async search(text: string): Promise<void> {
    const round = ++this.round;
    this.searching.set(true);
    try {
      const found = [...(isGuid(text) ? await this.source().byIds([text.toLowerCase()]) : await this.source().search(text))];
      if (round !== this.round) {
        return; // a newer search is on its way
      }
      this.results.set(found);
      this.searched.set(true);
    } catch (error) {
      if (round !== this.round) {
        return;
      }
      this.results.set([]);
      this.error.set(
        error instanceof HttpErrorResponse && error.status === 403
          ? 'Dafür fehlt dir das Recht.'
          : error instanceof HttpErrorResponse && error.status === 422
            ? 'Bitte mindestens zwei Zeichen eingeben.'
            : 'Die Suche hat nicht geklappt. Bitte erneut versuchen.',
      );
    } finally {
      if (round === this.round) {
        this.searching.set(false);
      }
    }
  }

  protected pick(person: WghnPerson): void {
    this.selected.set(person);
    this.query.set('');
    this.results.set([]);
    this.searched.set(false);
    this.picked.emit(person);
  }

  protected clear(): void {
    this.selected.set(null);
    this.picked.emit(null);
  }

  /** Enter takes the person if there is exactly one hit. */
  protected enter(event: Event): void {
    event.preventDefault();
    const found = this.results();
    if (found.length === 1) {
      this.pick(found[0]);
    }
  }

  protected closeList(): void {
    this.results.set([]);
    this.searched.set(false);
  }

  /** Arrow down from the field goes to the first hit; arrows between the hits. */
  protected focusFirst(event: Event): void {
    event.preventDefault();
    const list = (event.target as HTMLElement).closest('wghn-person-picker')?.querySelector<HTMLElement>('[data-testid="person-result"]');
    list?.focus();
  }

  protected move(event: Event, step: 1 | -1): void {
    event.preventDefault();
    const current = event.target as HTMLElement;
    const all = [...(current.closest('ul')?.querySelectorAll<HTMLElement>('[data-testid="person-result"]') ?? [])];
    const next = all[all.indexOf(current) + step];
    next?.focus();
  }
}
