import { HttpErrorResponse } from '@angular/common/http';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { WorkingTimeRules } from './admin-hr-models';
import { AdminHrApi } from './admin-hr.api';
import { WorkingTimeSection } from './working-time-section';

const defaults: WorkingTimeRules = { payoutThresholdHours: 0, expiryMonths: null, bookMinusHours: false, maxMinusHours: null, isDefault: true, changedAt: null, changedBy: null };
const entry = (id: string, name: string, isActive = true) => ({ id, kind: 'EmploymentRelationship', code: id.toUpperCase(), name, isActive });

type Methods = 'workingTimeRules' | 'saveWorkingTimeRules' | 'thresholds' | 'saveThreshold' | 'listEntries';

describe('WorkingTimeSection', () => {
  let api: Record<Methods, ReturnType<typeof vi.fn>>;
  let fixture: ComponentFixture<WorkingTimeSection>;
  let root: HTMLElement;

  async function settle(): Promise<void> {
    for (let i = 0; i < 4; i++) {
      await fixture.whenStable();
      await new Promise((r) => setTimeout(r));
    }
  }

  async function create(): Promise<void> {
    TestBed.configureTestingModule({ providers: [{ provide: AdminHrApi, useValue: api }] });
    fixture = TestBed.createComponent(WorkingTimeSection);
    root = fixture.nativeElement as HTMLElement;
    await settle();
  }

  const q = <T extends Element = HTMLElement>(selector: string): T | null => root.querySelector<T>(selector);
  const rowOf = (id: string) => root.querySelector<HTMLElement>(`[data-testid="threshold-row"][data-id="${id}"]`)!;

  async function type(selector: string, value: string, scope: ParentNode = root): Promise<void> {
    const el = scope.querySelector<HTMLInputElement>(selector)!;
    el.value = value;
    el.dispatchEvent(new Event('input', { bubbles: true }));
    await settle();
  }

  async function click(selector: string, scope: ParentNode = root): Promise<void> {
    scope.querySelector<HTMLElement>(selector)!.click();
    await settle();
  }

  beforeEach(() => {
    api = {
      workingTimeRules: vi.fn().mockResolvedValue(defaults),
      saveWorkingTimeRules: vi.fn().mockResolvedValue(undefined),
      thresholds: vi.fn().mockResolvedValue([{ relationshipId: 'r1', hours: 160 }]),
      saveThreshold: vi.fn().mockResolvedValue(undefined),
      listEntries: vi.fn().mockResolvedValue([entry('r1', 'Vollzeit'), entry('r2', 'Minijob'), entry('r3', 'Alt', false)]),
    };
  });

  it('shows the defaults: payout always, nothing expires, no minus hours', async () => {
    await create();

    expect((q('[data-testid="payout"]') as HTMLInputElement).value).toBe('0');
    expect((q('[data-testid="expiry-never"]') as HTMLInputElement).checked).toBe(true);
    expect((q('[data-testid="book-minus"]') as HTMLInputElement).checked).toBe(false);
    expect(q('[data-testid="limit-minus"]')).toBeNull();
    expect(q('[data-testid="audit"]')!.textContent).toContain('Standardwerte');
  });

  it('saves the defaults unchanged as they are', async () => {
    await create();
    await click('[data-action="save"]');

    expect(api['saveWorkingTimeRules']).toHaveBeenCalledWith({ payoutThresholdHours: 0, expiryMonths: null, bookMinusHours: false, maxMinusHours: null });
    expect(q('[data-testid="status"]')!.textContent).toContain('gespeichert');
  });

  it('saves payout threshold, expiry, minus hours and the lower limit as typed', async () => {
    await create();

    await type('[data-testid="payout"]', '20,5');
    await click('[data-testid="expiry-months"]');
    await type('[data-testid="expiry-value"]', '18');
    await click('[data-testid="book-minus"]');
    await click('[data-testid="limit-minus"]');
    await type('[data-testid="max-minus"]', '10');
    await click('[data-action="save"]');

    expect(api['saveWorkingTimeRules']).toHaveBeenCalledWith({ payoutThresholdHours: 20.5, expiryMonths: 18, bookMinusHours: true, maxMinusHours: 10 });
  });

  it('does not send invalid values and says why', async () => {
    await create();
    await type('[data-testid="payout"]', '5000');
    await click('[data-action="save"]');

    expect(api['saveWorkingTimeRules']).not.toHaveBeenCalled();
    expect(q('[data-testid="error"]')!.textContent).toContain('0 und 1000 Stunden');
  });

  it('shows the error of the service in plain language', async () => {
    api['saveWorkingTimeRules'].mockRejectedValue(new HttpErrorResponse({ status: 422, error: { title: 'InvalidRules' } }));
    await create();
    await click('[data-action="save"]');

    expect(q('[data-testid="error"]')!.textContent).toContain('Regeln sind nicht gültig');
  });

  it('lists every active relationship by name with its threshold, and the ones with a threshold even if deactivated', async () => {
    api['thresholds'].mockResolvedValue([{ relationshipId: 'r1', hours: 160 }, { relationshipId: 'r3', hours: 80 }]);
    await create();

    const rows = [...root.querySelectorAll('[data-testid="threshold-row"]')];
    expect(rows.map((r) => r.querySelector('strong')!.textContent)).toEqual(['Vollzeit (R1)', 'Minijob (R2)', 'Alt (R3)']);
    expect((rowOf('r1').querySelector('[data-testid="threshold-input"]') as HTMLInputElement).value).toBe('160');
    expect((rowOf('r2').querySelector('[data-testid="threshold-input"]') as HTMLInputElement).value).toBe('');
    expect(q('[data-testid="names-hint"]')).toBeNull();
  });

  it('shows the id when the person may not read the selection list', async () => {
    api['listEntries'].mockRejectedValue(new HttpErrorResponse({ status: 403 }));
    await create();

    expect(rowOf('r1').textContent).toContain('Arbeitsverhältnis r1');
    expect(q('[data-testid="names-hint"]')).not.toBeNull();
    expect(root.querySelectorAll('[data-testid="threshold-row"]')).toHaveLength(1);
  });

  it('saves the monthly threshold of one relationship and removes it when the field is empty', async () => {
    await create();

    await type('[data-testid="threshold-input"]', '172,5', rowOf('r2'));
    await click('[data-action="save-threshold"]', rowOf('r2'));
    expect(api['saveThreshold']).toHaveBeenCalledWith('r2', 172.5);
    expect(q('[data-testid="row-status"]')!.textContent).toContain('Minijob');

    await type('[data-testid="threshold-input"]', '', rowOf('r1'));
    await click('[data-action="save-threshold"]', rowOf('r1'));
    expect(api['saveThreshold']).toHaveBeenCalledWith('r1', null);
    expect(q('[data-testid="row-status"]')!.textContent).toContain('entfernt');
  });

  it('keeps the save button of a threshold off until it changed, and rejects invalid hours', async () => {
    await create();
    expect(rowOf('r1').querySelector<HTMLButtonElement>('[data-action="save-threshold"]')!.disabled).toBe(true);

    await type('[data-testid="threshold-input"]', '800', rowOf('r1'));
    await click('[data-action="save-threshold"]', rowOf('r1'));

    expect(api['saveThreshold']).not.toHaveBeenCalled();
    expect(rowOf('r1').querySelector('[data-testid="row-error"]')!.textContent).toContain('0 und 744');
  });

  it('shows the error of a threshold at its row', async () => {
    api['saveThreshold'].mockRejectedValue(new HttpErrorResponse({ status: 422, error: { title: 'UnknownRelationship' } }));
    await create();
    await type('[data-testid="threshold-input"]', '150', rowOf('r1'));
    await click('[data-action="save-threshold"]', rowOf('r1'));

    expect(rowOf('r1').querySelector('[data-testid="row-error"]')!.textContent).toContain('gibt es nicht');
  });

  it('says so when the rules or the thresholds cannot be loaded', async () => {
    api['workingTimeRules'].mockRejectedValue(new HttpErrorResponse({ status: 503 }));
    api['thresholds'].mockRejectedValue(new HttpErrorResponse({ status: 503 }));
    await create();

    expect(q('[data-testid="error"]')!.textContent).toContain('Laden hat nicht geklappt');
    expect(q('[data-testid="thresholds-error"]')!.textContent).toContain('Laden hat nicht geklappt');
  });
});
