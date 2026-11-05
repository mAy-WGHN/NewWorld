import { HttpErrorResponse } from '@angular/common/http';
import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { AuthService } from '../../core/auth/auth.service';
import { PrivilegeSet } from '../../core/auth/privileges';
import { ReferenceEntry } from './admin-hr-models';
import { AdminHrApi } from './admin-hr.api';
import { EmployeeCardSection } from './employee-card-section';

const grant = (...keys: string[]) => new PrivilegeSet(keys.map((key) => ({ key, unrestricted: true, storeIds: [], employeeIds: [] })));
const series = { lastNumber: 15301, startNumber: 1001, endNumber: 99999, remaining: 84698 };
const entry = (id: string, code: string, name: string, isActive = true): ReferenceEntry => ({ id, kind: 'Function', code, name, isActive });

type Methods = 'numberSeries' | 'saveNumberSeries' | 'listEntries' | 'addEntry' | 'updateEntry';

describe('EmployeeCardSection', () => {
  let api: Record<Methods, ReturnType<typeof vi.fn>>;
  const privileges = signal(grant('employee:manage-number-series', 'employee:manage-lists'));
  let fixture: ComponentFixture<EmployeeCardSection>;
  let root: HTMLElement;

  async function settle(): Promise<void> {
    for (let i = 0; i < 4; i++) {
      await fixture.whenStable();
      await new Promise((r) => setTimeout(r));
    }
  }

  async function create(): Promise<void> {
    TestBed.configureTestingModule({ providers: [{ provide: AdminHrApi, useValue: api }, { provide: AuthService, useValue: { privileges } }] });
    fixture = TestBed.createComponent(EmployeeCardSection);
    root = fixture.nativeElement as HTMLElement;
    await settle();
  }

  const q = <T extends Element = HTMLElement>(selector: string): T | null => root.querySelector<T>(selector);
  const rowOf = (id: string) => root.querySelector<HTMLElement>(`[data-testid="entry-row"][data-id="${id}"]`)!;

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
    privileges.set(grant('employee:manage-number-series', 'employee:manage-lists'));
    api = {
      numberSeries: vi.fn().mockResolvedValue(series),
      saveNumberSeries: vi.fn().mockImplementation(async (r) => ({ ...series, lastNumber: r.lastNumber, remaining: 99999 - r.lastNumber })),
      listEntries: vi.fn().mockResolvedValue([entry('f1', 'FL', 'Filialleitung'), entry('f2', 'VK', 'Verkauf', false)]),
      addEntry: vi.fn().mockImplementation(async (_kind, code, name) => entry('f3', code, name)),
      updateEntry: vi.fn().mockResolvedValue(undefined),
    };
  });

  describe('number series', () => {
    it('shows the last number, the range and what is left, and reminds of the go-live check', async () => {
      await create();

      expect(q('[data-testid="series-state"]')!.textContent).toContain('15301');
      expect(q('[data-testid="series-state"]')!.textContent).toContain('84698');
      expect((q('[data-testid="series-last"]') as HTMLInputElement).value).toBe('15301');
      expect(q('[data-testid="golive-hint"]')!.textContent).toContain('Business Central');
    });

    it('says that new people cannot be created while the series is not set', async () => {
      api['numberSeries'].mockResolvedValue({ lastNumber: null });
      await create();

      expect(q('[data-testid="series-state"]')!.textContent).toContain('noch nicht eingestellt');
      expect((q('[data-testid="series-last"]') as HTMLInputElement).value).toBe('');
    });

    it('saves only the last number when the range is left as it is', async () => {
      api['numberSeries'].mockResolvedValue({ lastNumber: null });
      await create();
      await type('[data-testid="series-last"]', '15301');
      await click('[data-action="save-series"]');

      expect(api['saveNumberSeries']).toHaveBeenCalledWith({ lastNumber: 15301 });
      expect(q('[data-testid="series-status"]')!.textContent).toContain('gespeichert');
    });

    it('saves a typed range', async () => {
      await create();
      await type('[data-testid="series-last"]', '20000');
      await type('[data-testid="series-start"]', '20000');
      await type('[data-testid="series-end"]', '30000');
      await click('[data-action="save-series"]');

      expect(api['saveNumberSeries']).toHaveBeenCalledWith({ lastNumber: 20000, startNumber: 20000, endNumber: 30000 });
    });

    it('rejects a number outside the range before asking the service', async () => {
      await create();
      await type('[data-testid="series-last"]', '100000');
      await click('[data-action="save-series"]');

      expect(api['saveNumberSeries']).not.toHaveBeenCalled();
      expect(q('[data-testid="series-error"]')!.textContent).toContain('im Bereich liegen');
    });

    it('shows the error of the service in plain language', async () => {
      api['saveNumberSeries'].mockRejectedValue(new HttpErrorResponse({ status: 422, error: { title: 'Invalid' } }));
      await create();
      await click('[data-action="save-series"]');

      expect(q('[data-testid="series-error"]')!.textContent).toContain('nicht gültig');
    });
  });

  describe('selection lists', () => {
    it('loads the first list (function) and shows its entries with their state', async () => {
      await create();

      expect(api['listEntries']).toHaveBeenCalledWith('Function');
      expect(rowOf('f1').textContent).toContain('Filialleitung');
      expect(rowOf('f1').querySelector('[data-testid="entry-state"]')!.textContent).toContain('Aktiv');
      expect(rowOf('f2').querySelector('[data-testid="entry-state"]')!.textContent).toContain('Deaktiviert');
      expect(rowOf('f1').querySelector('[data-action="delete"]')).toBeNull();
    });

    it('offers all eight lists and loads the chosen one', async () => {
      await create();
      const select = q<HTMLSelectElement>('[data-testid="list-kind"]')!;
      expect([...select.options].map((o) => o.value)).toEqual([
        'Function', 'Role', 'Region', 'Workplace', 'EmploymentRelationship', 'EmploymentCode', 'AuthorizationGroup', 'WageType',
      ]);

      select.value = 'EmploymentRelationship';
      select.dispatchEvent(new Event('change', { bubbles: true }));
      await settle();

      expect(api['listEntries']).toHaveBeenLastCalledWith('EmploymentRelationship');
    });

    it('adds an entry with code and name', async () => {
      await create();
      await type('[data-testid="new-code"]', ' GL ');
      await type('[data-testid="new-name"]', 'Geschäftsleitung');
      await click('[data-action="add-entry"]');

      expect(api['addEntry']).toHaveBeenCalledWith('Function', 'GL', 'Geschäftsleitung');
      expect(rowOf('f3').textContent).toContain('Geschäftsleitung');
      expect((q('[data-testid="new-code"]') as HTMLInputElement).value).toBe('');
    });

    it('does not add an entry without code or name', async () => {
      await create();
      await type('[data-testid="new-name"]', 'Nur Name');
      await click('[data-action="add-entry"]');

      expect(api['addEntry']).not.toHaveBeenCalled();
      expect(q('[data-testid="list-error"]')!.textContent).toContain('Code und Bezeichnung');
    });

    it('tells that a code is already taken', async () => {
      api['addEntry'].mockRejectedValue(new HttpErrorResponse({ status: 409, error: { title: 'DuplicateCode' } }));
      await create();
      await type('[data-testid="new-code"]', 'FL');
      await type('[data-testid="new-name"]', 'Doppelt');
      await click('[data-action="add-entry"]');

      expect(q('[data-testid="list-error"]')!.textContent).toContain('schon vergeben');
    });

    it('renames an entry without changing code or state', async () => {
      await create();
      await click('[data-action="edit"]', rowOf('f1'));
      await type('[data-testid="edit-name"]', 'Filialleiter', rowOf('f1'));
      await click('[data-action="save-name"]', rowOf('f1'));

      expect(api['updateEntry']).toHaveBeenCalledWith('Function', 'f1', 'Filialleiter', true);
      expect(rowOf('f1').textContent).toContain('Filialleiter');
    });

    it('deactivates and reactivates entries', async () => {
      await create();
      await click('[data-action="deactivate"]', rowOf('f1'));
      expect(api['updateEntry']).toHaveBeenCalledWith('Function', 'f1', 'Filialleitung', false);
      expect(rowOf('f1').querySelector('[data-testid="entry-state"]')!.textContent).toContain('Deaktiviert');
      expect(q('[data-testid="list-status"]')!.textContent).toContain('deaktiviert');

      await click('[data-action="activate"]', rowOf('f2'));
      expect(api['updateEntry']).toHaveBeenCalledWith('Function', 'f2', 'Verkauf', true);
    });

    it('says so when a list cannot be loaded', async () => {
      api['listEntries'].mockRejectedValue(new HttpErrorResponse({ status: 503 }));
      await create();

      expect(q('[data-testid="list-error"]')!.textContent).toContain('Laden hat nicht geklappt');
    });
  });

  describe('privileges', () => {
    it('with only the number series privilege shows no lists and does not load them', async () => {
      privileges.set(grant('employee:manage-number-series'));
      await create();

      expect(q('[data-testid="series-last"]')).not.toBeNull();
      expect(q('[data-testid="list-kind"]')).toBeNull();
      expect(api['listEntries']).not.toHaveBeenCalled();
    });

    it('with only the lists privilege shows no number series and does not load it', async () => {
      privileges.set(grant('employee:manage-lists'));
      await create();

      expect(q('[data-testid="series-last"]')).toBeNull();
      expect(api['numberSeries']).not.toHaveBeenCalled();
      expect(q('[data-testid="list-kind"]')).not.toBeNull();
    });
  });
});
