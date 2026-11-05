import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { AuthService } from '../../core/auth/auth.service';
import { AdminAccessApi } from './admin-access.api';
import {
  KNOWN_TOPICS,
  Person,
  StoreGroup,
  StoreOption,
  TARGET_KINDS,
  TargetKind,
  TopicResponsibility,
  dayAfter,
  dayStart,
  isGuid,
  isValidTopic,
  personLabel,
  storeLabel,
  validatePeriod,
} from './admin-access-models';
import { ADMIN_UI, ActionMessages, ActionState } from './admin-ui';
import { PersonPicker } from './person-picker';

interface CreatedResponsibility {
  id: string;
  topic: string;
  target: string;
}

/**
 * "Zuständigkeiten": who is responsible for a topic (for example "Urlaub genehmigen") for a person, a store or a group of stores.
 * The list shows who is responsible for the loaded person, per topic. Removing needs the ID of the responsibility.
 * TODO(Backend): the list has no IDs and no list of all responsibilities exists; therefore only what was created in this session can be removed
 * with a click, everything else by ID.
 */
@Component({
  selector: 'app-responsibilities-panel',
  imports: [...ADMIN_UI, ActionMessages, PersonPicker],
  templateUrl: './responsibilities-panel.html',
})
export class ResponsibilitiesPanel {
  private readonly api = inject(AdminAccessApi);
  private readonly auth = inject(AuthService);

  readonly personId = input.required<string>();

  protected readonly act = new ActionState();
  protected readonly items = signal<TopicResponsibility[]>([]);
  protected readonly stores = signal<StoreOption[]>([]);
  protected readonly scopes = signal<StoreGroup[]>([]);
  protected readonly loaded = signal(false);
  protected readonly loadError = signal<string | null>(null);
  protected readonly created = signal<CreatedResponsibility[]>([]);
  /** Names of the people that are shown by id (responsible persons) or were chosen here. */
  protected readonly names = signal<Record<string, string>>({});

  protected readonly canCreate = computed(() => this.auth.privileges().has('access:responsibility:create'));
  protected readonly canDelete = computed(() => this.auth.privileges().has('access:responsibility:delete'));

  protected readonly topic = signal('');
  protected readonly responsibleId = signal('');
  protected readonly kind = signal<TargetKind>('employee');
  protected readonly targetId = signal('');
  protected readonly from = signal('');
  protected readonly to = signal('');
  protected readonly formError = signal<string | null>(null);

  protected readonly removeId = signal('');
  protected readonly removeError = signal<string | null>(null);
  protected readonly confirmingDeleteId = signal<string | null>(null);

  protected readonly topics = KNOWN_TOPICS;
  protected readonly kinds = TARGET_KINDS;
  protected readonly storeLabel = storeLabel;

  constructor() {
    effect(() => {
      const id = this.personId();
      untracked(() => {
        this.targetId.set(id);
        this.kind.set('employee');
        void this.load(id);
      });
    });
  }

  protected topicLabel(topic: string): string {
    return KNOWN_TOPICS.find((t) => t.topic === topic)?.label ?? topic;
  }

  private async load(id: string): Promise<void> {
    this.loaded.set(false);
    this.act.clear();
    try {
      this.items.set(await this.api.responsibilities(id));
      this.loadError.set(null);
      void this.loadNames(this.items().flatMap((i) => i.responsibleEmployeeIds));
    } catch (error) {
      this.items.set([]);
      this.loadError.set(
        error instanceof HttpErrorResponse && error.status === 403
          ? 'Du darfst die Zuständigkeiten für diese Person nicht sehen.'
          : 'Die Zuständigkeiten konnten nicht geladen werden.',
      );
    } finally {
      this.loaded.set(true);
    }
    const [stores, scopes] = await Promise.allSettled([this.api.stores(), this.api.scopes()]);
    this.stores.set(stores.status === 'fulfilled' ? stores.value : []);
    this.scopes.set(scopes.status === 'fulfilled' ? scopes.value : []);
  }

  /** Looks up the names of the responsible people so that the list shows names instead of ids. */
  private async loadNames(ids: readonly string[]): Promise<void> {
    const missing = [...new Set(ids)].filter((id) => !(id in this.names()));
    if (missing.length === 0) {
      return;
    }
    try {
      const people = await this.api.peopleByIds(missing);
      this.names.update((known) => ({ ...known, ...Object.fromEntries(people.map((p) => [p.employeeId, personLabel(p)])) }));
    } catch {
      // the ids stay as they are
    }
  }

  protected nameOf(id: string): string {
    return this.names()[id] ?? id;
  }

  protected onResponsible(person: Person | null): void {
    this.responsibleId.set(person?.employeeId ?? '');
    if (person) {
      this.names.update((known) => ({ ...known, [person.employeeId]: personLabel(person) }));
    }
    this.formError.set(null);
  }

  protected onTargetPerson(person: Person | null): void {
    this.targetId.set(person?.employeeId ?? '');
    if (person) {
      this.names.update((known) => ({ ...known, [person.employeeId]: personLabel(person) }));
    }
    this.formError.set(null);
  }

  protected set(field: 'topic' | 'responsibleId' | 'targetId' | 'from' | 'to' | 'removeId', event: Event): void {
    this[field].set((event.target as HTMLInputElement | HTMLSelectElement).value);
    this.formError.set(null);
    this.removeError.set(null);
  }

  protected onKind(event: Event): void {
    this.kind.set((event.target as HTMLSelectElement).value as TargetKind);
    this.targetId.set(this.kind() === 'employee' ? this.personId() : '');
    this.formError.set(null);
  }

  protected validate(): string | null {
    if (!isValidTopic(this.topic())) {
      return 'Bitte ein Thema angeben (Kleinbuchstaben, Ziffern, Bindestriche und Punkte, zum Beispiel absence.approval).';
    }
    if (!isGuid(this.responsibleId())) {
      return 'Bitte die zuständige Person auswählen.';
    }
    if (!this.targetId() || (this.kind() === 'employee' && !isGuid(this.targetId()))) {
      return this.kind() === 'employee' ? 'Bitte die Person auswählen, für die die Zuständigkeit gilt.' : 'Bitte das Ziel wählen.';
    }
    return validatePeriod(this.from(), this.to());
  }

  protected targetText(): string {
    const id = this.targetId().trim();
    return this.kind() === 'store'
      ? (this.stores().find((s) => s.id === id) ? storeLabel(this.stores().find((s) => s.id === id)!) : id)
      : this.kind() === 'scope'
        ? (this.scopes().find((s) => s.id === id)?.name ?? id)
        : (this.names()[id] ?? id);
  }

  protected async add(): Promise<void> {
    const error = this.validate();
    this.formError.set(error);
    if (error) {
      return;
    }
    const personId = this.personId();
    const topic = this.topic().trim();
    const targetText = this.targetText();
    let newId = '';
    const ok = await this.act.run(async () => {
      newId = (
        await this.api.addResponsibility({
          topic,
          responsibleEmployeeId: this.responsibleId().trim(),
          target: { kind: this.kind(), id: this.targetId().trim() },
          validFrom: dayStart(this.from()),
          validTo: dayAfter(this.to()),
        })
      ).id;
      this.items.set(await this.api.responsibilities(personId));
    }, 'Die Zuständigkeit wurde eingerichtet. Sie gilt sofort.');
    if (ok) {
      this.created.update((list) => [...list, { id: newId, topic, target: targetText }]);
      this.topic.set('');
      this.responsibleId.set('');
      this.from.set('');
      this.to.set('');
    }
  }

  protected askDelete(id: string): void {
    this.act.clear();
    this.removeError.set(null);
    this.confirmingDeleteId.set(id);
  }

  protected askDeleteById(): void {
    if (!isGuid(this.removeId())) {
      this.removeError.set('Bitte die ID der Zuständigkeit angeben (eine GUID).');
      return;
    }
    this.askDelete(this.removeId().trim());
  }

  protected cancelDelete(): void {
    this.confirmingDeleteId.set(null);
  }

  protected async remove(id: string): Promise<void> {
    const personId = this.personId();
    const ok = await this.act.run(async () => {
      await this.api.removeResponsibility(id);
      this.items.set(await this.api.responsibilities(personId));
    }, 'Die Zuständigkeit wurde entfernt.');
    if (ok) {
      this.confirmingDeleteId.set(null);
      this.created.update((list) => list.filter((c) => c.id !== id));
      this.removeId.set('');
    }
  }
}
