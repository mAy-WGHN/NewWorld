import { HttpErrorResponse } from '@angular/common/http';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { VacationRules } from './admin-hr-models';
import { AdminHrApi } from './admin-hr.api';
import { VacationSection } from './vacation-section';

const defaults: VacationRules = {
  extraDaysExpiryMonth: null, extraDaysExpiryDay: null, waitingPeriodMonths: 0, waitingPeriodMode: 'Accrued', proRataRounding: 'Up', proRataRoundingStep: 0.5,
  isDefault: true, changedAt: null, changedBy: null,
};

describe('VacationSection', () => {
  let api: Record<'vacationRules' | 'saveVacationRules', ReturnType<typeof vi.fn>>;
  let fixture: ComponentFixture<VacationSection>;
  let root: HTMLElement;

  async function settle(): Promise<void> {
    for (let i = 0; i < 4; i++) {
      await fixture.whenStable();
      await new Promise((r) => setTimeout(r));
    }
  }

  async function create(): Promise<void> {
    TestBed.configureTestingModule({ providers: [{ provide: AdminHrApi, useValue: api }] });
    fixture = TestBed.createComponent(VacationSection);
    root = fixture.nativeElement as HTMLElement;
    await settle();
  }

  const q = <T extends Element = HTMLElement>(selector: string): T | null => root.querySelector<T>(selector);

  async function choose(selector: string, value: string): Promise<void> {
    const el = q<HTMLSelectElement>(selector)!;
    el.value = value;
    el.dispatchEvent(new Event('change', { bubbles: true }));
    await settle();
  }

  async function type(selector: string, value: string): Promise<void> {
    const el = q<HTMLInputElement>(selector)!;
    el.value = value;
    el.dispatchEvent(new Event('input', { bubbles: true }));
    await settle();
  }

  async function save(): Promise<void> {
    q<HTMLButtonElement>('[data-action="save"]')!.click();
    await settle();
  }

  beforeEach(() => {
    api = {
      vacationRules: vi.fn().mockResolvedValue(defaults),
      saveVacationRules: vi.fn().mockImplementation(async (r) => ({ ...defaults, ...r, isDefault: false, changedAt: '2026-11-05T09:30:00+00:00' })),
    };
  });

  it('shows the defaults: nothing expires, no waiting period, rounding up to half days', async () => {
    await create();

    expect((q('[data-testid="expiry-never"]') as HTMLInputElement).checked).toBe(true);
    expect(q('[data-testid="expiry-month"]')).toBeNull();
    expect((q('[data-testid="waiting-months"]') as HTMLInputElement).value).toBe('0');
    expect((q('[data-testid="rounding"]') as HTMLSelectElement).value).toBe('Up');
    expect((q('[data-testid="rounding-step"]') as HTMLSelectElement).value).toBe('0.5');
    expect(q('[data-testid="audit"]')!.textContent).toContain('Standardwerte');
  });

  it('shows the stored expiry date', async () => {
    api['vacationRules'].mockResolvedValue({ ...defaults, extraDaysExpiryMonth: 3, extraDaysExpiryDay: 31, isDefault: false, changedAt: '2026-11-05T09:30:00+00:00' });
    await create();

    expect((q('[data-testid="expiry-date"]') as HTMLInputElement).checked).toBe(true);
    expect((q('[data-testid="expiry-day"]') as HTMLInputElement).value).toBe('31');
    expect((q('[data-testid="expiry-month"]') as HTMLSelectElement).value).toBe('3');
    expect(q('[data-testid="audit"]')!.textContent).toContain('Zuletzt geändert');
  });

  it('saves the expiry date, waiting period with mode, and rounding as typed', async () => {
    await create();

    q<HTMLInputElement>('[data-testid="expiry-date"]')!.click();
    await settle();
    await type('[data-testid="expiry-day"]', '31');
    await choose('[data-testid="expiry-month"]', '3');
    await type('[data-testid="waiting-months"]', '6');
    await choose('[data-testid="waiting-mode"]', 'Blocked');
    await choose('[data-testid="rounding"]', 'Nearest');
    await choose('[data-testid="rounding-step"]', '1');
    await save();

    expect(api['saveVacationRules']).toHaveBeenCalledWith({
      extraDaysExpiryMonth: 3, extraDaysExpiryDay: 31, waitingPeriodMonths: 6, waitingPeriodMode: 'Blocked', proRataRounding: 'Nearest', proRataRoundingStep: 1,
    });
    expect(q('[data-testid="status"]')!.textContent).toContain('gespeichert');
    expect(q('[data-testid="audit"]')!.textContent).toContain('Zuletzt geändert');
  });

  it('explains the chosen waiting mode and rounding', async () => {
    await create();
    await choose('[data-testid="waiting-mode"]', 'Blocked');
    await choose('[data-testid="rounding"]', 'None');

    expect(q('[data-testid="mode-hint"]')!.textContent).toContain('keinen Urlaub');
    expect(q('[data-testid="rounding-hint"]')!.textContent).toContain('unverändert');
  });

  it('switching back to "never" sends no expiry date', async () => {
    api['vacationRules'].mockResolvedValue({ ...defaults, extraDaysExpiryMonth: 3, extraDaysExpiryDay: 31, isDefault: false });
    await create();

    q<HTMLInputElement>('[data-testid="expiry-never"]')!.click();
    await settle();
    await save();

    expect(api['saveVacationRules']).toHaveBeenCalledWith(expect.objectContaining({ extraDaysExpiryMonth: null, extraDaysExpiryDay: null }));
  });

  it('does not send an invalid date or waiting period and says why', async () => {
    await create();
    q<HTMLInputElement>('[data-testid="expiry-date"]')!.click();
    await settle();
    await type('[data-testid="expiry-day"]', '31');
    await choose('[data-testid="expiry-month"]', '2');
    await save();

    expect(api['saveVacationRules']).not.toHaveBeenCalled();
    expect(q('[data-testid="error"]')!.textContent).toContain('im gewählten Monat nicht');

    await choose('[data-testid="expiry-month"]', '3');
    await type('[data-testid="waiting-months"]', '30');
    await save();
    expect(api['saveVacationRules']).not.toHaveBeenCalled();
    expect(q('[data-testid="error"]')!.textContent).toContain('0 bis 24');
  });

  it('shows the error of the service in plain language', async () => {
    api['saveVacationRules'].mockRejectedValue(new HttpErrorResponse({ status: 422, error: { title: 'InvalidVacationRules' } }));
    await create();
    await save();

    expect(q('[role="alert"]')!.textContent).toContain('Urlaubsregeln sind nicht gültig');
    expect(q('[role="alert"]')!.textContent).not.toContain('InvalidVacationRules');
  });

  it('says so when the rules cannot be loaded and offers no form', async () => {
    api['vacationRules'].mockRejectedValue(new HttpErrorResponse({ status: 503 }));
    await create();

    expect(q('[role="alert"]')!.textContent).toContain('Laden hat nicht geklappt');
    expect(q('form')).toBeNull();
  });
});
