import { HttpErrorResponse } from '@angular/common/http';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { todayIso } from '../../core/dates';
import { addDays } from './working-time-models';
import { WorkingTime } from './working-time-models';
import { WorkingTimeApi } from './working-time.api';
import { WorkingTimePage } from './working-time.page';

const in10 = () => addDays(todayIso(), 10);
const in200 = () => addDays(todayIso(), 200);

const account = (patch: Partial<WorkingTime> = {}): WorkingTime => ({
  balance: 12.5,
  deficit: 0,
  forfeited: 0,
  buckets: [
    { earnedOn: '2026-01-31', remaining: 4, expiresOn: in10() },
    { earnedOn: '2026-06-30', remaining: 8.5, expiresOn: in200() },
  ],
  bookings: [
    { id: 'b2', kind: 'TimeOff', hours: -4, date: '2026-10-20', monthCloseId: null, createdAt: '2026-10-19T08:00:00+00:00' },
    { id: 'b1', kind: 'Overtime', hours: 6.5, date: '2026-09-30', monthCloseId: 'c1', createdAt: '2026-10-01T08:00:00+00:00' },
  ],
  closes: [{ id: 'c1', year: 2026, month: 9, workedHours: 176.5, thresholdHours: 170, overtimeHours: 6.5, closedAt: '2026-10-01T08:00:00+00:00', minusHours: 0, minusHoursNotBooked: 0 }],
  ...patch,
});

describe('WorkingTimePage', () => {
  let load: ReturnType<typeof vi.fn>;
  let fixture: ComponentFixture<WorkingTimePage>;
  let root: HTMLElement;

  async function create(): Promise<void> {
    TestBed.configureTestingModule({ providers: [{ provide: WorkingTimeApi, useValue: { mine: load } }] });
    fixture = TestBed.createComponent(WorkingTimePage);
    root = fixture.nativeElement as HTMLElement;
    for (let i = 0; i < 4; i++) {
      await fixture.whenStable();
      await new Promise((r) => setTimeout(r));
    }
  }

  const q = <T extends Element = HTMLElement>(selector: string): T | null => root.querySelector<T>(selector);

  beforeEach(() => {
    load = vi.fn().mockResolvedValue(account());
  });

  it('shows the balance of the account', async () => {
    await create();

    expect(q('h1')!.textContent).toContain('Arbeitszeitkonto');
    expect(q('[data-testid="balance"]')!.textContent).toContain('12,5 h');
  });

  it('shows a deficit and expired hours only when there are some', async () => {
    await create();
    expect(q('[data-testid="deficit"]')).toBeNull();
    expect(q('[data-testid="forfeited"]')).toBeNull();

    TestBed.resetTestingModule();
    load = vi.fn().mockResolvedValue(account({ deficit: 3, forfeited: 2 }));
    await create();

    expect(q('[data-testid="deficit"]')!.textContent).toContain('3 h');
    expect(q('[data-testid="forfeited"]')!.textContent).toContain('2 h');
  });

  it('marks hours that run out soon in a yellow block at the top', async () => {
    await create();

    const block = q('[data-testid="attention"]')!;
    expect(block.textContent).toContain('Läuft bald ab');
    expect(block.textContent).toContain('4 h');
    expect(block.textContent).not.toContain('8,5');
    expect(root.querySelector('h1')!.compareDocumentPosition(block) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
  });

  it('shows no yellow block when nothing runs out soon', async () => {
    load.mockResolvedValue(account({ buckets: [{ earnedOn: '2026-06-30', remaining: 8.5, expiresOn: in200() }] }));
    await create();

    expect(q('[data-testid="attention"]')).toBeNull();
  });

  it('lists the bookings newest first with kind, day and signed hours', async () => {
    await create();

    const rows = [...root.querySelectorAll('[data-testid="booking"]')].map((r) => r.textContent!.replace(/\s+/g, ' ').trim());
    expect(rows).toHaveLength(2);
    expect(rows[0]).toContain('20.10.2026');
    expect(rows[0]).toContain('Freizeitausgleich');
    expect(rows[0]).toContain('−4 h');
    expect(rows[1]).toContain('Überstunden');
    expect(rows[1]).toContain('+6,5 h');
  });

  it('lists the month closes with worked hours, threshold and overtime', async () => {
    await create();

    const text = q('[data-testid="closes"]')!.textContent!;
    expect(text).toContain('September 2026');
    expect(text).toContain('176,5 h');
    expect(text).toContain('170 h');
    expect(text).toContain('+6,5 h');
  });

  it('says so when there are no bookings', async () => {
    load.mockResolvedValue(account({ bookings: [], closes: [], buckets: [], balance: 0 }));
    await create();

    expect(root.textContent).toContain('Noch keine Buchungen');
  });

  it('tells the person if the account cannot be loaded', async () => {
    load.mockRejectedValue(new HttpErrorResponse({ status: 500 }));
    await create();

    expect(q('[role="alert"]')!.textContent).toContain('geladen');
    expect(q('[data-testid="balance"]')).toBeNull();
  });
});
