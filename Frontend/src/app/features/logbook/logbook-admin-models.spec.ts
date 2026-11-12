import {
  EditSection,
  StructureEdit,
  assignmentOptions,
  editSections,
  moved,
  newDistrict,
  newOption,
  newRegion,
  newSection,
  sectionInputs,
  structureEdit,
  structureInput,
  withLeader,
} from './logbook-admin-models';
import { LogbookTemplate, RATING_HINT, RATING_SECTION_ID, RATING_TITLE, Structure } from './logbook-models';

const template: LogbookTemplate = {
  version: 2,
  publishedAt: null,
  sections: [
    { id: 's1', title: 'Stimmung', hint: 'Kurz', kind: 'singleChoice', required: true, options: [{ id: 'o1', label: 'Gut' }, { id: 'o2', label: 'Schlecht' }] },
    { id: 's2', title: 'Sonstiges', hint: null, kind: 'freeText', required: false, options: [] },
    { id: RATING_SECTION_ID, title: 'Gesamtsituation', hint: null, kind: 'rating', required: true, options: [] },
  ],
};

describe('template editing', () => {
  it('keeps the ids of what exists so answers keep their meaning, and gives new things none', () => {
    const sections = editSections(template);
    const added = newSection();

    expect(sections.map((s) => s.id)).toEqual(['s1', 's2', RATING_SECTION_ID]);
    expect(sections[0].options.map((o) => o.id)).toEqual(['o1', 'o2']);
    expect(sections[1].hint).toBe('');
    expect(added.id).toBeNull();
    expect(added.options).toHaveLength(2);
    expect(new Set([...sections.map((s) => s.key), added.key]).size).toBe(4); // every part has its own key
  });

  it('makes the request: texts trimmed, an empty hint is none, empty answers are left out', () => {
    const sections = editSections(template);
    sections[0].title = '  Stimmung der Woche ';
    sections[0].hint = '   ';
    sections[0].options = [...sections[0].options, { ...newOption(), label: '   ' }, { ...newOption(), label: ' Mittel ' }];

    expect(sectionInputs(sections)[0]).toEqual({
      id: 's1',
      title: 'Stimmung der Woche',
      hint: null,
      kind: 'singleChoice',
      required: true,
      options: [{ id: 'o1', label: 'Gut' }, { id: 'o2', label: 'Schlecht' }, { id: null, label: 'Mittel' }],
    });
  });

  it('sends no answers for a free text section even if it had some before', () => {
    const sections: EditSection[] = editSections(template);
    sections[0] = { ...sections[0], kind: 'freeText' };

    expect(sectionInputs(sections)[0].options).toEqual([]);
  });

  it('puts the rating in front of an old template that has none and leaves one that has it where it is', () => {
    const old = editSections({ ...template, sections: template.sections.slice(0, 2) });

    expect(old.map((s) => s.id)).toEqual([RATING_SECTION_ID, 's1', 's2']);
    expect(old[0]).toMatchObject({ kind: 'rating', required: true, title: RATING_TITLE, hint: RATING_HINT, options: [] });
    expect(editSections(template).map((s) => s.id)).toEqual(['s1', 's2', RATING_SECTION_ID]);
  });

  it('always sends the rating as required with no answers of its own, whatever was edited', () => {
    const sections = editSections(template);
    sections[2] = { ...sections[2], required: false, options: [newOption()] };

    expect(sectionInputs(sections)[2]).toEqual({ id: RATING_SECTION_ID, title: 'Gesamtsituation', hint: null, kind: 'rating', required: true, options: [] });
  });

  it('moves an item by one place and leaves the list alone at the ends', () => {
    expect(moved(['a', 'b', 'c'], 1, -1)).toEqual(['b', 'a', 'c']);
    expect(moved(['a', 'b', 'c'], 1, 1)).toEqual(['a', 'c', 'b']);
    expect(moved(['a', 'b', 'c'], 0, -1)).toEqual(['a', 'b', 'c']);
    expect(moved(['a', 'b', 'c'], 2, 1)).toEqual(['a', 'b', 'c']);
    expect(moved(['a', 'b', 'c'], 5, -1)).toEqual(['a', 'b', 'c']);
    const original = ['a', 'b'];
    expect(moved(original, 0, 1)).not.toBe(original);
  });
});

describe('hierarchy editing', () => {
  const structure: Structure = {
    districts: [{ id: 'd1', name: 'Süd', leaders: [{ id: 'p1', name: 'Revisor' }] }],
    regions: [
      { id: 'r1', districtId: 'd1', name: 'Augsburg', leaders: [{ id: 'p2', name: 'Ingo' }] },
      { id: 'r2', districtId: 'd1', name: 'München', leaders: [] },
    ],
    stores: [
      { storeId: 'a', storeNumber: '001', storeName: 'A', districtId: 'd1', regionId: 'r1' },
      { storeId: 'b', storeNumber: '002', storeName: 'B', districtId: 'd1', regionId: null },
    ],
  };

  it('says for each store whether it is in a region, in a district only, or nowhere', () => {
    const edit = structureEdit(structure);

    expect(edit.assignment).toEqual({ a: 'r:r1', b: 'd:d1' });
  });

  it('makes the request with the district of the region for a store in a region', () => {
    const edit = structureEdit(structure);

    expect(structureInput(edit)).toEqual({
      districts: [{ id: 'd1', name: 'Süd', leaderIds: ['p1'] }],
      regions: [
        { id: 'r1', districtId: 'd1', name: 'Augsburg', leaderIds: ['p2'] },
        { id: 'r2', districtId: 'd1', name: 'München', leaderIds: [] },
      ],
      stores: [
        { storeId: 'a', districtId: 'd1', regionId: 'r1' },
        { storeId: 'b', districtId: 'd1', regionId: null },
      ],
    });
  });

  it('leaves stores out that are assigned nowhere or to something that was removed, so nothing dangles', () => {
    const edit: StructureEdit = {
      districts: [{ id: 'd1', name: ' Süd ', leaders: [] }],
      regions: [
        { id: 'r1', districtId: 'd1', name: 'Augsburg', leaders: [] },
        { id: 'r9', districtId: 'gone', name: 'Verwaist', leaders: [] },
      ],
      assignment: { a: '', b: 'd:gone', c: 'r:r9', d: 'r:unknown', e: 'r:r1', f: 'd:d1', g: 'x' },
    };

    const input = structureInput(edit);

    expect(input.stores.map((s) => s.storeId).sort()).toEqual(['e', 'f']);
    expect(input.regions.map((r) => r.id)).toEqual(['r1']); // the region of a removed district goes, too
    expect(input.districts[0].name).toBe('Süd');
  });

  it('offers the districts and their regions as places for a store', () => {
    const options = assignmentOptions(structureEdit(structure));

    expect(options).toEqual([
      { value: 'd:d1', label: 'Bezirk Süd' },
      { value: 'r:r1', label: 'Region Augsburg (Süd)' },
      { value: 'r:r2', label: 'Region München (Süd)' },
    ]);
  });

  it('names a district or region without a name so it can still be chosen', () => {
    const edit: StructureEdit = { districts: [newDistrict()], regions: [], assignment: {} };
    edit.regions = [newRegion(edit.districts[0].id)];

    expect(assignmentOptions(edit).map((o) => o.label)).toEqual(['Bezirk (ohne Namen)', 'Region (ohne Namen) (ohne Namen)']);
  });

  it('adds a leader only once', () => {
    const leaders = [{ id: 'p1', name: 'Revisor' }];

    expect(withLeader(leaders, { id: 'p1', name: 'Revisor' })).toEqual(leaders);
    expect(withLeader(leaders, { id: 'p2', name: 'Ingo' })).toHaveLength(2);
    expect(leaders).toHaveLength(1); // the list as it was is not changed
  });
});
