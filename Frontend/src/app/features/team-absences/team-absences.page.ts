import { Component, computed, inject, signal } from '@angular/core';
import { AuthService } from '../../core/auth/auth.service';
import { PAGE_UI } from '../../shared/ui/material';
import { problemTitle } from '../absences/absence-models';
import {
  CELL_CLASS,
  CELL_KINDS,
  CELL_LABELS,
  CELL_TEXT,
  CellKind,
  StoreOption,
  StorePerson,
  StoreUnavailability,
  TeamMember,
  UnavailabilityKind,
  UnavailabilityPeriod,
  YearMonth,
  cellsFor,
  enterableKinds,
  entryErrorMessage,
  formatDate,
  isManualEntry,
  membersWithPeople,
  monthDays,
  monthEnd,
  monthLabel,
  monthOf,
  monthStart,
  periodLabel,
  shiftMonth,
  todayIso,
  validateEntry,
} from './team-absences-models';
import { TeamAbsencesApi } from './team-absences.api';

type View = 'team' | 'mine';

/**
 * "Team-Abwesenheiten": who is not available when, as a month grid per store (rows = people, columns = days), and the own timeline. What a
 * viewer sees of a period (the kind or only "nicht verfügbar") is decided by the service; the grid shows exactly what it sends. HR (with
 * `unavailability:create` / `unavailability:delete`) can also enter and delete entries; the service enforces the reach.
 */
@Component({
  selector: 'app-team-absences',
  imports: [...PAGE_UI],
  templateUrl: './team-absences.page.html',
})
export class TeamAbsencesPage {
  private readonly api = inject(TeamAbsencesApi);
  private readonly auth = inject(AuthService);

  protected readonly canCreate = computed(() => this.auth.privileges().has('unavailability:create'));
  protected readonly canDelete = computed(() => this.auth.privileges().has('unavailability:delete'));
  protected readonly canSeeOwn = computed(() => this.auth.privileges().has('absence:read-own'));
  protected readonly manage = computed(() => this.canCreate() || this.canDelete());

  protected readonly view = signal<View>('team');

  /** The stores the person may look at: all of Portal's list if one of the two privileges is unrestricted, otherwise the granted ones. */
  protected readonly stores = signal<StoreOption[]>([]);
  protected readonly storeId = signal<string | null>(null);
  protected readonly month = signal<YearMonth>(monthOf(todayIso()));
  protected readonly rows = signal<StoreUnavailability[]>([]);
  protected readonly loaded = signal(false);
  protected readonly loadError = signal<string | null>(null);

  protected readonly mine = signal<UnavailabilityPeriod[]>([]);
  protected readonly mineLoaded = signal(false);
  protected readonly mineError = signal<string | null>(null);

  protected readonly today = todayIso();
  protected readonly days = computed(() => monthDays(this.month()));
  protected readonly title = computed(() => monthLabel(this.month()));
  /** The active people of the store; all of them get a row, also those without an entry. */
  protected readonly people = signal<StorePerson[]>([]);
  protected readonly members = computed<TeamMember[]>(() => membersWithPeople(this.people(), this.rows()));
  protected readonly grid = computed(() => {
    const days = this.days();
    return this.members().map((m) => ({
      member: m,
      cells: cellsFor(
        this.rows().filter((r) => r.period.employeeId === m.employeeId).map((r) => r.period),
        days,
      ),
    }));
  });
  /** What the legend shows: only what appears in the grid would hide the meaning of a colour, so all kinds are listed. */
  protected readonly legend = CELL_KINDS.map((k) => ({ kind: k, label: CELL_LABELS[k] }));

  // HR: entries.
  protected readonly kinds = signal<UnavailabilityKind[]>([]);
  protected readonly personId = signal<string | null>(null);
  protected readonly kindId = signal('');
  protected readonly from = signal('');
  protected readonly to = signal('');
  protected readonly deleting = signal<string | null>(null);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly status = signal<string | null>(null);

  protected readonly selectedPerson = computed(() => this.members().find((m) => m.employeeId === this.personId()) ?? null);
  /** The entries of the chosen person in the shown month that HR entered by hand (the only ones that can be deleted). */
  protected readonly personEntries = computed(() => {
    const id = this.personId();
    return this.rows().map((r) => r.period).filter((p) => p.employeeId === id && isManualEntry(p));
  });
  protected readonly enterableKinds = computed(() => enterableKinds(this.kinds()));

  protected readonly formatDate = formatDate;
  protected readonly periodLabel = periodLabel;

  constructor() {
    void this.init();
  }

  private async init(): Promise<void> {
    try {
      const all = await this.api.stores();
      const stores = this.allowedStores(all);
      this.stores.set(stores);
      this.storeId.set(stores[0]?.id ?? null);
    } catch {
      this.loadError.set('Die Filialen konnten nicht geladen werden.');
      this.loaded.set(true);
      return;
    }
    if (this.canCreate()) {
      try {
        this.kinds.set(await this.api.kinds());
      } catch {
        // The form then offers no kind; the rest of the page works.
      }
    }
    await this.load();
  }

  private allowedStores(all: StoreOption[]): StoreOption[] {
    const privileges = this.auth.privileges();
    const grants = ['absence:read-availability', 'absence:read-reason'].map((k) => privileges.get(k)).filter((g) => !!g);
    if (grants.some((g) => g.unrestricted)) {
      return all;
    }
    const ids = new Set(grants.flatMap((g) => g.storeIds));
    return all.filter((s) => ids.has(s.id));
  }

  protected async chooseStore(event: Event): Promise<void> {
    this.storeId.set((event.target as HTMLSelectElement).value || null);
    this.personId.set(null);
    await this.load();
  }

  protected async step(delta: number): Promise<void> {
    this.month.set(shiftMonth(this.month(), delta));
    await this.load();
  }

  protected async thisMonth(): Promise<void> {
    this.month.set(monthOf(todayIso()));
    await this.load();
  }

  protected async load(): Promise<void> {
    const storeId = this.storeId();
    if (!storeId) {
      this.rows.set([]);
      this.loaded.set(true);
      return;
    }
    const month = this.month();
    try {
      // The people are asked at the same time; without them (the call fails) the grid shows only those who have an entry, as before.
      const [rows, people] = await Promise.all([
        this.api.store(storeId, monthStart(month), monthEnd(month)),
        this.api.people(storeId).catch((): StorePerson[] => []),
      ]);
      // Ignore an answer for a month or store that is no longer shown.
      if (storeId === this.storeId() && month === this.month()) {
        this.rows.set(rows);
        this.people.set(people);
        this.loadError.set(null);
      }
    } catch (e) {
      this.rows.set([]);
      this.loadError.set(
        problemTitle(e) === 'InvalidPeriod' ? entryErrorMessage('InvalidPeriod') : 'Die Abwesenheiten konnten nicht geladen werden.',
      );
    } finally {
      this.loaded.set(true);
    }
  }

  protected async showView(view: View): Promise<void> {
    this.view.set(view);
    if (view === 'mine' && !this.mineLoaded()) {
      await this.loadMine();
    }
  }

  protected async loadMine(): Promise<void> {
    try {
      const items = await this.api.mine();
      this.mine.set([...items].sort((a, b) => b.from.localeCompare(a.from)));
      this.mineError.set(null);
    } catch {
      this.mineError.set('Deine Abwesenheiten konnten nicht geladen werden.');
    } finally {
      this.mineLoaded.set(true);
    }
  }

  /** Colour from the theme, plus a letter in the cell so the colour is never the only signal. */
  protected cellClass(kind: CellKind): string {
    return CELL_CLASS[kind];
  }

  protected cellText(kind: CellKind): string {
    return CELL_TEXT[kind];
  }

  // ---- HR ----

  protected setPerson(event: Event): void {
    this.personId.set((event.target as HTMLSelectElement).value || null);
    this.deleting.set(null);
  }

  protected setKind(event: Event): void {
    this.kindId.set((event.target as HTMLSelectElement).value);
  }

  protected setFrom(event: Event): void {
    this.from.set((event.target as HTMLInputElement).value);
  }

  protected setTo(event: Event): void {
    this.to.set((event.target as HTMLInputElement).value);
  }

  protected async add(): Promise<void> {
    const person = this.personId();
    const input = { kindId: this.kindId(), from: this.from(), to: this.to() };
    const problem = validateEntry(input);
    if (!person) {
      this.error.set('Bitte eine Person wählen.');
      return;
    }
    if (problem) {
      this.error.set(problem);
      return;
    }
    await this.run(async () => {
      await this.api.add(person, input);
      this.from.set('');
      this.to.set('');
      await this.load();
    }, 'Eintrag gespeichert.');
  }

  protected askDelete(entry: UnavailabilityPeriod): void {
    this.error.set(null);
    this.deleting.set(entry.id ?? null);
  }

  protected cancelDelete(): void {
    this.deleting.set(null);
  }

  protected async remove(entry: UnavailabilityPeriod): Promise<void> {
    if (!entry.id || this.deleting() !== entry.id) {
      return;
    }
    await this.run(async () => {
      await this.api.remove(entry.employeeId, entry.id!);
      this.deleting.set(null);
      await this.load();
    }, 'Eintrag gelöscht.');
  }

  private async run(action: () => Promise<void>, done: string): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    this.status.set(null);
    try {
      await action();
      this.status.set(done);
    } catch (e) {
      this.error.set(entryErrorMessage(problemTitle(e)));
    } finally {
      this.busy.set(false);
    }
  }
}
