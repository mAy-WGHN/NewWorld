import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Absence } from '../absences/absence-models';
import { VacationCalendar } from './vacation-calendar';

const iso = (offset: number): string => {
  const d = new Date();
  d.setDate(d.getDate() + offset);
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
};

const absence = (id: string, status: string, from: string, to: string): Absence => ({
  id,
  employeeId: 'e',
  type: 'Vacation',
  status,
  from,
  to,
  certificateStatus: null,
  certifiedThrough: null,
});

describe('VacationCalendar', () => {
  let fixture: ComponentFixture<VacationCalendar>;
  const root = () => fixture.nativeElement as HTMLElement;

  async function create(absences: Absence[]): Promise<void> {
    fixture = TestBed.createComponent(VacationCalendar);
    fixture.componentRef.setInput('absences', absences);
    await fixture.whenStable();
  }

  it('marks the days of a request with its status and says so in words', async () => {
    const today = iso(0);
    await create([absence('a', 'Approved', today, today)]);
    const cell = root().querySelector(`[data-iso="${today}"]`)!;
    expect(cell.getAttribute('data-shown')).toBe('approved');
    expect(cell.getAttribute('aria-label')).toContain('genehmigt');
    expect(cell.getAttribute('aria-current')).toBe('date');
    expect(root().querySelector('[data-testid="calendar-empty"]')).toBeNull();
  });

  it('marks a holiday with its name, says whether the store is open and tells which days are on show', async () => {
    const today = iso(0);
    fixture = TestBed.createComponent(VacationCalendar);
    fixture.componentRef.setInput('absences', []);
    fixture.componentRef.setInput('holidays', [{ date: today, name: 'Testfeiertag', storeOpens: true }]);
    const ranges: { from: string; to: string }[] = [];
    fixture.componentInstance.shown.subscribe((r) => ranges.push(r));
    await fixture.whenStable();

    const cell = root().querySelector(`[data-iso="${today}"]`)!;
    expect(cell.getAttribute('data-holiday')).toBe('Testfeiertag');
    expect(cell.getAttribute('title')).toContain('Testfeiertag');
    expect(cell.getAttribute('title')).toContain('geöffnet');
    expect(cell.getAttribute('aria-label')).toContain('Feiertag Testfeiertag');
    expect(ranges.length).toBeGreaterThan(0);
    expect(ranges[0].from <= today && today <= ranges[0].to).toBe(true);
  });

  it('says so when the month has no vacation', async () => {
    await create([]);
    expect(root().querySelector('[data-testid="calendar-empty"]')).not.toBeNull();
  });

  it('moves between months and back to today', async () => {
    await create([]);
    const title = () => root().querySelector('#cal-title')!.textContent;
    const first = title();
    (root().querySelector('[data-action="next-month"]') as HTMLButtonElement).click();
    await fixture.whenStable();
    expect(title()).not.toBe(first);
    (root().querySelector('[data-action="today"]') as HTMLButtonElement).click();
    await fixture.whenStable();
    expect(title()).toBe(first);
  });

  describe('picking days', () => {
    let over: Element | null = null;
    const original = document.elementFromPoint;

    beforeEach(() => {
      document.elementFromPoint = () => over;
    });
    afterEach(() => {
      document.elementFromPoint = original;
    });

    const cell = (offset: number) => root().querySelector(`[data-iso="${iso(offset)}"]`) as HTMLElement;
    const grid = () => root().querySelector('[role="grid"]') as HTMLElement;
    const press = (target: HTMLElement, type: string, button = 0) => {
      over = target;
      target.dispatchEvent(new MouseEvent(type, { bubbles: true, button }));
    };
    const release = () => document.dispatchEvent(new MouseEvent('pointerup', { bubbles: true }));

    async function createSelectable(): Promise<{ from: string; to: string }[]> {
      const sent: { from: string; to: string }[] = [];
      fixture = TestBed.createComponent(VacationCalendar);
      fixture.componentRef.setInput('absences', []);
      fixture.componentRef.setInput('selectable', true);
      fixture.componentInstance.requested.subscribe((r) => sent.push(r));
      await fixture.whenStable();
      return sent;
    }

    it('asks for a confirmation after a click on one day and sends the request only then', async () => {
      const sent = await createSelectable();
      press(cell(0), 'pointerdown');
      release();
      await fixture.whenStable();
      expect(root().querySelector('[data-testid="selection"]')?.textContent).toContain('beantragen?');
      expect(sent).toEqual([]);
      (root().querySelector('[data-action="confirm"]') as HTMLButtonElement).click();
      await fixture.whenStable();
      expect(sent).toEqual([{ from: iso(0), to: iso(0) }]);
      expect(root().querySelector('[data-testid="selection"]')).toBeNull();
    });

    it('picks the period between press and release, also when dragged backwards', async () => {
      const sent = await createSelectable();
      const days = Array.from(root().querySelectorAll<HTMLElement>('[data-iso]'));
      const early = days[10];
      const late = days[13];
      press(late, 'pointerdown');
      over = days[11];
      grid().dispatchEvent(new MouseEvent('pointermove', { bubbles: true }));
      over = early;
      grid().dispatchEvent(new MouseEvent('pointermove', { bubbles: true }));
      release();
      await fixture.whenStable();
      expect(root().querySelectorAll('[data-picked="true"]').length).toBe(4);
      (root().querySelector('[data-action="confirm"]') as HTMLButtonElement).click();
      expect(sent).toEqual([{ from: early.dataset['iso'], to: late.dataset['iso'] }]);
    });

    it('drops the pick with Abbrechen and does not send anything', async () => {
      const sent = await createSelectable();
      press(cell(0), 'pointerdown');
      release();
      await fixture.whenStable();
      (root().querySelector('[data-action="discard"]') as HTMLButtonElement).click();
      await fixture.whenStable();
      expect(root().querySelector('[data-testid="selection"]')).toBeNull();
      expect(sent).toEqual([]);
    });

    it('ignores the right mouse button and calendars that cannot request', async () => {
      await createSelectable();
      press(cell(0), 'pointerdown', 2);
      release();
      await fixture.whenStable();
      expect(root().querySelector('[data-testid="selection"]')).toBeNull();

      await create([]);
      press(cell(0), 'pointerdown');
      release();
      await fixture.whenStable();
      expect(root().querySelector('[data-testid="selection"]')).toBeNull();
    });
  });

  describe('keyboard', () => {
    const cellOf = (iso: string) => root().querySelector(`[data-iso="${iso}"]`) as HTMLElement;
    const press = (iso: string, key: string) => cellOf(iso).dispatchEvent(new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true }));

    async function createSelectable(): Promise<{ from: string; to: string }[]> {
      const sent: { from: string; to: string }[] = [];
      fixture = TestBed.createComponent(VacationCalendar);
      fixture.componentRef.setInput('absences', []);
      fixture.componentRef.setInput('selectable', true);
      fixture.componentInstance.requested.subscribe((r) => sent.push(r));
      await fixture.whenStable();
      return sent;
    }

    it('has exactly one day in the tab order and moves the focus with the arrow keys', async () => {
      await createSelectable();
      expect(root().querySelectorAll('[role="gridcell"][tabindex="0"]').length).toBe(1);
      const start = (root().querySelector('[role="gridcell"][tabindex="0"]') as HTMLElement).dataset['iso']!;
      press(start, 'ArrowRight');
      await fixture.whenStable();
      const next = (root().querySelector('[role="gridcell"][tabindex="0"]') as HTMLElement).dataset['iso']!;
      expect(next > start).toBe(true);
      expect(document.activeElement).toBe(cellOf(next));
    });

    it('turns to the next month when the focus leaves the days on show', async () => {
      await createSelectable();
      const title = () => root().querySelector('#cal-title')!.textContent;
      const first = title();
      const start = (root().querySelector('[role="gridcell"][tabindex="0"]') as HTMLElement).dataset['iso']!;
      press(start, 'PageDown'); // a month on can still lie in the grid (the last days of the week)
      await fixture.whenStable();
      const once = (root().querySelector('[role="gridcell"][tabindex="0"]') as HTMLElement).dataset['iso']!;
      press(once, 'PageDown');
      await fixture.whenStable();
      expect(title()).not.toBe(first);
    });

    it('picks a period with Enter, arrow keys and Enter and sends it only after the confirmation', async () => {
      const sent = await createSelectable();
      const days = Array.from(root().querySelectorAll<HTMLElement>('[data-iso]'));
      const start = days[10].dataset['iso']!;
      press(start, 'Enter');
      press(start, 'ArrowRight');
      await fixture.whenStable();
      const end = (root().querySelector('[role="gridcell"][tabindex="0"]') as HTMLElement).dataset['iso']!;
      press(end, 'ArrowRight');
      await fixture.whenStable();
      const last = (root().querySelector('[role="gridcell"][tabindex="0"]') as HTMLElement).dataset['iso']!;
      press(last, 'Enter');
      await fixture.whenStable();
      expect(root().querySelector('[data-testid="selection"]')).not.toBeNull();
      expect(root().querySelectorAll('[data-picked="true"]').length).toBe(3);
      expect(sent).toEqual([]);
      (root().querySelector('[data-action="confirm"]') as HTMLButtonElement).click();
      expect(sent).toEqual([{ from: start, to: last }]);
    });

    it('drops a pick with Esc and does nothing with Enter when the calendar cannot request', async () => {
      await createSelectable();
      const start = (root().querySelector('[role="gridcell"][tabindex="0"]') as HTMLElement).dataset['iso']!;
      press(start, 'Enter');
      await fixture.whenStable();
      expect(root().querySelectorAll('[data-picked="true"]').length).toBe(1);
      document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
      await fixture.whenStable();
      expect(root().querySelectorAll('[data-picked="true"]').length).toBe(0);

      await create([]);
      const other = (root().querySelector('[role="gridcell"][tabindex="0"]') as HTMLElement).dataset['iso']!;
      press(other, 'Enter');
      await fixture.whenStable();
      expect(root().querySelectorAll('[data-picked="true"]').length).toBe(0);
      expect(root().querySelector('[data-testid="selection"]')).toBeNull();
    });
  });
});
