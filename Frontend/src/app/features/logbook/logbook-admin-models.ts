import { Leader, LogbookTemplate, RATING_HINT, RATING_SECTION_ID, RATING_TITLE, SectionKind, Structure, newId } from './logbook-models';

// ---------------------------------------------------------------- what is sent

/** What the administration sends for a section: ids are only there for what already exists (they keep the meaning of answers already given). */
export interface SectionInput {
  id: string | null;
  title: string;
  hint: string | null;
  kind: SectionKind;
  required: boolean;
  options: { id: string | null; label: string }[];
}

export interface StructureInput {
  districts: { id: string; name: string; leaderIds: string[] }[];
  regions: { id: string; districtId: string; name: string; leaderIds: string[] }[];
  stores: { storeId: string; districtId: string; regionId: string | null }[];
}

// ---------------------------------------------------------------- template

let counter = 0;
const nextKey = (): number => ++counter;

export interface EditOption {
  key: number;
  id: string | null;
  label: string;
}

export interface EditSection {
  key: number;
  id: string | null;
  title: string;
  hint: string;
  kind: SectionKind;
  required: boolean;
  options: EditOption[];
}

/** Only these have answers of their own to maintain; the rating has its five fixed ones. */
export const choiceKind = (kind: SectionKind): boolean => kind === 'singleChoice' || kind === 'multiChoice';

/** The rating cannot be removed, and only its title and hint can be changed. */
export const isRating = (section: Pick<EditSection, 'kind'>): boolean => section.kind === 'rating';

/** The sections as they are edited. The rating is always among them: a template that has none (an old one) gets it in front, so saving brings it. */
export function editSections(template: LogbookTemplate): EditSection[] {
  const sections = editSectionsOf(template);
  return sections.some(isRating)
    ? sections
    : [{ key: nextKey(), id: RATING_SECTION_ID, title: RATING_TITLE, hint: RATING_HINT, kind: 'rating', required: true, options: [] }, ...sections];
}

function editSectionsOf(template: LogbookTemplate): EditSection[] {
  return template.sections.map((s) => ({
    key: nextKey(),
    id: s.id,
    title: s.title,
    hint: s.hint ?? '',
    kind: s.kind,
    required: s.required,
    options: s.options.map((o) => ({ key: nextKey(), id: o.id, label: o.label })),
  }));
}

export function newSection(): EditSection {
  return { key: nextKey(), id: null, title: '', hint: '', kind: 'singleChoice', required: false, options: [newOption(), newOption()] };
}

export function newOption(): EditOption {
  return { key: nextKey(), id: null, label: '' };
}

/** The request: texts trimmed, an empty hint is none, a free text section has no options, empty options are left out. */
export function sectionInputs(sections: readonly EditSection[]): SectionInput[] {
  return sections.map((s) => ({
    id: s.id,
    title: s.title.trim(),
    hint: s.hint.trim() || null,
    kind: s.kind,
    required: isRating(s) ? true : s.required,
    options: choiceKind(s.kind) ? s.options.filter((o) => o.label.trim()).map((o) => ({ id: o.id, label: o.label.trim() })) : [],
  }));
}

/** The list with the item at `index` moved by `by` places (up is negative); the list as it was if that is not possible. */
export function moved<T>(list: readonly T[], index: number, by: number): T[] {
  const target = index + by;
  if (index < 0 || index >= list.length || target < 0 || target >= list.length) {
    return [...list];
  }
  const copy = [...list];
  [copy[index], copy[target]] = [copy[target], copy[index]];
  return copy;
}

// ---------------------------------------------------------------- hierarchy

export interface EditDistrict {
  id: string;
  name: string;
  leaders: Leader[];
}

export interface EditRegion {
  id: string;
  districtId: string;
  name: string;
  leaders: Leader[];
}

/** The hierarchy as it is edited; <c>assignment</c> says where each store is: nowhere, in a district (<c>d:id</c>) or in a region (<c>r:id</c>). */
export interface StructureEdit {
  districts: EditDistrict[];
  regions: EditRegion[];
  assignment: Record<string, string>;
}

export const NOT_ASSIGNED = '';

export function structureEdit(structure: Structure): StructureEdit {
  const assignment: Record<string, string> = {};
  for (const store of structure.stores) {
    assignment[store.storeId] = store.regionId ? `r:${store.regionId}` : `d:${store.districtId}`;
  }
  return {
    districts: structure.districts.map((d) => ({ id: d.id, name: d.name, leaders: [...d.leaders] })),
    regions: structure.regions.map((r) => ({ id: r.id, districtId: r.districtId, name: r.name, leaders: [...r.leaders] })),
    assignment,
  };
}

export function newDistrict(): EditDistrict {
  return { id: newId(), name: '', leaders: [] };
}

export function newRegion(districtId: string): EditRegion {
  return { id: newId(), districtId, name: '', leaders: [] };
}

/** Adds a leader unless the person is one already. */
export function withLeader(leaders: readonly Leader[], person: Leader): Leader[] {
  return leaders.some((l) => l.id === person.id) ? [...leaders] : [...leaders, person];
}

/**
 * The request. A store whose district or region no longer exists counts as not assigned, so removing a district or a region never leaves a dangling
 * assignment; a store in a region belongs to the district of the region.
 */
export function structureInput(edit: StructureEdit): StructureInput {
  const districts = new Set(edit.districts.map((d) => d.id));
  const regions = new Map(edit.regions.map((r) => [r.id, r]));
  const stores: StructureInput['stores'] = [];
  for (const [storeId, value] of Object.entries(edit.assignment)) {
    if (value.startsWith('d:') && districts.has(value.slice(2))) {
      stores.push({ storeId, districtId: value.slice(2), regionId: null });
    } else if (value.startsWith('r:')) {
      const region = regions.get(value.slice(2));
      if (region && districts.has(region.districtId)) {
        stores.push({ storeId, districtId: region.districtId, regionId: region.id });
      }
    }
  }
  return {
    districts: edit.districts.map((d) => ({ id: d.id, name: d.name.trim(), leaderIds: d.leaders.map((l) => l.id) })),
    regions: edit.regions.filter((r) => districts.has(r.districtId)).map((r) => ({ id: r.id, districtId: r.districtId, name: r.name.trim(), leaderIds: r.leaders.map((l) => l.id) })),
    stores,
  };
}

/** The label of an assignment for the choice of a store: "Bezirk Süd" or "Region Augsburg (Süd)". */
export function assignmentOptions(edit: StructureEdit): { value: string; label: string }[] {
  const options: { value: string; label: string }[] = [];
  for (const district of edit.districts) {
    options.push({ value: `d:${district.id}`, label: `Bezirk ${district.name || '(ohne Namen)'}` });
    for (const region of edit.regions.filter((r) => r.districtId === district.id)) {
      options.push({ value: `r:${region.id}`, label: `Region ${region.name || '(ohne Namen)'} (${district.name || 'ohne Namen'})` });
    }
  }
  return options;
}
