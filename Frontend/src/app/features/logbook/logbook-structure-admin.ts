import { Component, computed, inject, signal } from '@angular/core';
import { PersonPicker } from '../admin-access/person-picker';
import { Person } from '../admin-access/admin-access-models';
import { LogbookApi } from './logbook.api';
import {
  EditDistrict,
  EditRegion,
  NOT_ASSIGNED,
  StructureEdit,
  assignmentOptions,
  newDistrict,
  newRegion,
  structureEdit,
  structureInput,
  withLeader,
} from './logbook-admin-models';
import { Leader, StoreOption, logbookError } from './logbook-models';
import { Action, LOGBOOK_UI } from './logbook-ui';

/**
 * The hierarchy of the Berichtsheft: districts with district leaders, regions with regional leaders, and which store belongs where. It is edited as a
 * whole and saved as a whole; the service checks that leaders and stores exist. Leaders are found by name or personnel number.
 */
@Component({
  selector: 'app-logbook-structure-admin',
  imports: [PersonPicker, ...LOGBOOK_UI],
  templateUrl: './logbook-structure-admin.html',
})
export class LogbookStructureAdmin {
  private readonly api = inject(LogbookApi);

  protected readonly edit = signal<StructureEdit>({ districts: [], regions: [], assignment: {} });
  protected readonly stores = signal<StoreOption[]>([]);
  protected readonly loaded = signal(false);
  protected readonly loadError = signal<string | null>(null);
  protected readonly storeQuery = signal('');
  /** Changes with every added leader so the picker starts empty again. */
  protected readonly pickerRound = signal(0);
  protected readonly save = new Action();

  protected readonly options = computed(() => assignmentOptions(this.edit()));
  protected readonly notAssigned = NOT_ASSIGNED;
  protected readonly visibleStores = computed(() => {
    const text = this.storeQuery().trim().toLowerCase();
    return this.stores().filter((s) => !text || `${s.storeCode} ${s.name}`.toLowerCase().includes(text));
  });
  /** How many stores belong nowhere (they are not reported on). */
  protected readonly unassigned = computed(() => this.stores().filter((s) => !(this.edit().assignment[s.id] ?? NOT_ASSIGNED)).length);

  constructor() {
    void this.load();
  }

  private async load(): Promise<void> {
    try {
      const [structure, stores] = await Promise.all([this.api.structure(), this.api.stores()]);
      this.edit.set(structureEdit(structure));
      this.stores.set(stores);
      this.loaded.set(true);
    } catch (error) {
      this.loadError.set(logbookError(error));
    }
  }

  protected districtRegions(districtId: string): EditRegion[] {
    return this.edit().regions.filter((r) => r.districtId === districtId);
  }

  protected value(event: Event): string {
    return (event.target as HTMLInputElement | HTMLSelectElement).value;
  }

  private update(change: (edit: StructureEdit) => StructureEdit): void {
    this.edit.update(change);
    this.save.reset();
  }

  protected addDistrict(): void {
    this.update((e) => ({ ...e, districts: [...e.districts, newDistrict()] }));
  }

  protected renameDistrict(district: EditDistrict, name: string): void {
    this.update((e) => ({ ...e, districts: e.districts.map((d) => (d.id === district.id ? { ...d, name } : d)) }));
  }

  protected removeDistrict(district: EditDistrict): void {
    this.update((e) => ({
      districts: e.districts.filter((d) => d.id !== district.id),
      regions: e.regions.filter((r) => r.districtId !== district.id),
      assignment: e.assignment,
    }));
  }

  protected addRegion(district: EditDistrict): void {
    this.update((e) => ({ ...e, regions: [...e.regions, newRegion(district.id)] }));
  }

  protected renameRegion(region: EditRegion, name: string): void {
    this.update((e) => ({ ...e, regions: e.regions.map((r) => (r.id === region.id ? { ...r, name } : r)) }));
  }

  protected removeRegion(region: EditRegion): void {
    this.update((e) => ({ ...e, regions: e.regions.filter((r) => r.id !== region.id) }));
  }

  protected addDistrictLeader(district: EditDistrict, person: Person | null): void {
    if (person) {
      this.update((e) => ({ ...e, districts: e.districts.map((d) => (d.id === district.id ? { ...d, leaders: withLeader(d.leaders, toLeader(person)) } : d)) }));
      this.pickerRound.update((n) => n + 1);
    }
  }

  protected removeDistrictLeader(district: EditDistrict, leader: Leader): void {
    this.update((e) => ({ ...e, districts: e.districts.map((d) => (d.id === district.id ? { ...d, leaders: d.leaders.filter((l) => l.id !== leader.id) } : d)) }));
  }

  protected addRegionLeader(region: EditRegion, person: Person | null): void {
    if (person) {
      this.update((e) => ({ ...e, regions: e.regions.map((r) => (r.id === region.id ? { ...r, leaders: withLeader(r.leaders, toLeader(person)) } : r)) }));
      this.pickerRound.update((n) => n + 1);
    }
  }

  protected removeRegionLeader(region: EditRegion, leader: Leader): void {
    this.update((e) => ({ ...e, regions: e.regions.map((r) => (r.id === region.id ? { ...r, leaders: r.leaders.filter((l) => l.id !== leader.id) } : r)) }));
  }

  protected assignmentOf(storeId: string): string {
    return this.edit().assignment[storeId] ?? NOT_ASSIGNED;
  }

  protected assign(storeId: string, value: string): void {
    this.update((e) => ({ ...e, assignment: { ...e.assignment, [storeId]: value } }));
  }

  protected setQuery(event: Event): void {
    this.storeQuery.set(this.value(event));
  }

  protected async submit(): Promise<void> {
    await this.save.run(async () => {
      const saved = await this.api.saveStructure(structureInput(this.edit()));
      this.edit.set(structureEdit(saved));
    }, 'Die Hierarchie ist gespeichert.');
  }
}

function toLeader(person: Person): Leader {
  return { id: person.employeeId, name: person.name };
}
