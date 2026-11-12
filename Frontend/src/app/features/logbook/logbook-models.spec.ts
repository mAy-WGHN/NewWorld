import { HttpErrorResponse } from '@angular/common/http';
import {
  LogbookTemplate,
  currentWeek,
  draftsFrom,
  logbookError,
  missingRequired,
  mondayOf,
  RATING_SECTION_ID,
  shiftWeek,
  shownAnswers,
  stamp,
  stateOrder,
  toRequest,
  weekLabel,
  weekNumber,
} from './logbook-models';

const template: LogbookTemplate = {
  version: 3,
  publishedAt: null,
  sections: [
    { id: 's1', title: 'Wie war die Woche?', hint: null, kind: 'singleChoice', required: true, options: [{ id: 'o1', label: 'Gut' }, { id: 'o2', label: 'Schlecht' }] },
    { id: 's2', title: 'Was war los?', hint: null, kind: 'multiChoice', required: false, options: [{ id: 'o3', label: 'Krankheit' }, { id: 'o4', label: 'Lieferung' }] },
    { id: 's3', title: 'Sonstiges', hint: 'Frei', kind: 'freeText', required: true, options: [] },
  ],
};

describe('weeks', () => {
  it('names the Monday of any day of the week, also on a Sunday', () => {
    expect(mondayOf('2026-10-19')).toBe('2026-10-19');
    expect(mondayOf('2026-10-22')).toBe('2026-10-19');
    expect(mondayOf('2026-10-25')).toBe('2026-10-19');
    expect(mondayOf('2026-10-26')).toBe('2026-10-26');
  });

  it('moves by whole weeks over the change of the clock and the turn of the year', () => {
    expect(shiftWeek('2026-10-26', -1)).toBe('2026-10-19'); // the clock was set back on 2026-10-25
    expect(shiftWeek('2026-10-19', 1)).toBe('2026-10-26');
    expect(shiftWeek('2026-12-28', 1)).toBe('2027-01-04');
    expect(shiftWeek('2028-02-21', 1)).toBe('2028-02-28'); // leap year
    expect(shiftWeek('2028-02-28', 1)).toBe('2028-03-06');
  });

  it('counts the calendar weeks the ISO way, including week 53 and the first week of a year that begins in December', () => {
    expect(weekNumber('2026-10-19')).toBe(43);
    expect(weekNumber('2026-12-28')).toBe(53); // 2026 has 53 weeks
    expect(weekNumber('2027-01-04')).toBe(1);
    expect(weekNumber('2025-12-29')).toBe(1); // belongs to 2026
    expect(weekNumber('2026-01-05')).toBe(2);
  });

  it('writes the week for people', () => {
    expect(weekLabel('2026-10-19')).toBe('KW 43 · 19.10. bis 25.10.2026');
    expect(weekLabel('2026-12-28')).toBe('KW 53 · 28.12. bis 03.01.2027');
  });

  it('takes the current week from a given day', () => {
    expect(currentWeek(new Date(2026, 9, 25, 23, 30))).toBe('2026-10-19');
    expect(currentWeek(new Date(2026, 9, 26, 0, 10))).toBe('2026-10-26');
  });

  it('writes a time stamp the German way, and nothing for nothing', () => {
    expect(stamp(new Date(2026, 9, 23, 14, 5).toISOString())).toBe('23.10.2026, 14:05 Uhr');
    expect(stamp(null)).toBe('');
  });
});

describe('states', () => {
  it('sorts what needs a look first: followed up, new, in progress, done', () => {
    const order = (['done', 'inProgress', 'new', 'followedUp'] as const).slice().sort((a, b) => stateOrder(a) - stateOrder(b));
    expect(order).toEqual(['followedUp', 'new', 'inProgress', 'done']);
  });
});

describe('answers', () => {
  it('starts every section empty and takes over what was saved', () => {
    const drafts = draftsFrom(template, { s1: { optionIds: ['o2'], text: null }, s3: { optionIds: [], text: 'Alles ruhig' } });

    expect(drafts['s1']).toEqual({ optionIds: ['o2'], text: '' });
    expect(drafts['s2']).toEqual({ optionIds: [], text: '' });
    expect(drafts['s3']).toEqual({ optionIds: [], text: 'Alles ruhig' });
    expect(Object.keys(draftsFrom(template, undefined))).toEqual(['s1', 's2', 's3']);
  });

  it('sends only what was answered, texts trimmed, choices without a text', () => {
    const drafts = draftsFrom(template, undefined);
    drafts['s1'] = { optionIds: ['o1'], text: 'ignoriert' };
    drafts['s3'] = { optionIds: [], text: '  Hallo  ' };

    expect(toRequest(template, drafts)).toEqual({ s1: { optionIds: ['o1'], text: null }, s3: { optionIds: [], text: 'Hallo' } });
    expect(toRequest(template, draftsFrom(template, undefined))).toEqual({});
  });

  it('treats a text of blanks as not answered', () => {
    const drafts = draftsFrom(template, undefined);
    drafts['s3'] = { optionIds: [], text: '   \n ' };

    expect(toRequest(template, drafts)).toEqual({});
    expect(missingRequired(template, drafts).map((s) => s.id)).toEqual(['s1', 's3']);
  });

  it('names the required sections that are still empty', () => {
    const drafts = draftsFrom(template, undefined);
    expect(missingRequired(template, drafts).map((s) => s.id)).toEqual(['s1', 's3']);

    drafts['s1'] = { optionIds: ['o1'], text: '' };
    drafts['s3'] = { optionIds: [], text: 'x' };
    expect(missingRequired(template, drafts)).toEqual([]);
  });

  it('treats the rating like a choice: one value is an answer, none is missing', () => {
    const rated: LogbookTemplate = {
      version: 1,
      publishedAt: null,
      sections: [{ id: RATING_SECTION_ID, title: 'Gesamtsituation', hint: null, kind: 'rating', required: true, options: [{ id: 'v1', label: '1' }, { id: 'v5', label: '5' }] }],
    };
    const drafts = draftsFrom(rated, undefined);

    expect(missingRequired(rated, drafts).map((s) => s.id)).toEqual([RATING_SECTION_ID]);
    expect(toRequest(rated, drafts)).toEqual({});
    drafts[RATING_SECTION_ID] = { optionIds: ['v5'], text: '' };
    expect(missingRequired(rated, drafts)).toEqual([]);
    expect(toRequest(rated, drafts)).toEqual({ [RATING_SECTION_ID]: { optionIds: ['v5'], text: null } });
    expect(shownAnswers(rated, toRequest(rated, drafts))[0].labels).toEqual(['5']);
  });

  it('shows the answers in the order of the template and says so if an answer was removed from the template', () => {

    const shown = shownAnswers(template, { s3: { optionIds: [], text: ' Hallo ' }, s2: { optionIds: ['o4', 'gone'], text: null } });

    expect(shown.map((s) => s.section.id)).toEqual(['s1', 's2', 's3']);
    expect(shown[0]).toMatchObject({ labels: [], text: null });
    expect(shown[1].labels).toEqual(['Lieferung', '(entfernte Antwort)']);
    expect(shown[2].text).toBe('Hallo');
  });
});

describe('logbookError', () => {
  const failure = (status: number, error: unknown): HttpErrorResponse => new HttpErrorResponse({ status, error });

  it('joins the messages of the fields for a validation error, each one once', () => {
    const error = failure(422, { success: false, message: 'Ungültig', errors: [{ field: 'a', message: 'Eins.' }, { field: 'b', message: 'Zwei.' }, { field: 'c', message: 'Eins.' }] });

    expect(logbookError(error)).toBe('Eins. Zwei.');
  });

  it('takes the message of the service otherwise, and a general text if there is none', () => {
    expect(logbookError(failure(409, { success: false, message: 'Schon abgegeben.', errors: [] }))).toBe('Schon abgegeben.');
    expect(logbookError(failure(422, { success: false, message: 'Nur die Meldung.', errors: [] }))).toBe('Nur die Meldung.');
    expect(logbookError(failure(500, null))).toMatch(/\S/);
    expect(logbookError(new Error('boom'))).toMatch(/\S/);
  });
});
