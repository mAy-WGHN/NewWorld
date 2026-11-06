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
});
