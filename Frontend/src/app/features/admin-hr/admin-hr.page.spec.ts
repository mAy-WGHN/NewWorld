import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { AuthService } from '../../core/auth/auth.service';
import { PrivilegeSet } from '../../core/auth/privileges';
import { AdminHrApi } from './admin-hr.api';
import { AdminHrPage } from './admin-hr.page';

const grant = (...keys: string[]) => new PrivilegeSet(keys.map((key) => ({ key, unrestricted: true, storeIds: [], employeeIds: [] })));
const never = () => new Promise<never>(() => undefined);

describe('AdminHrPage', () => {
  const privileges = signal(grant());
  let fixture: ComponentFixture<AdminHrPage>;
  let root: HTMLElement;
  let api: Record<string, ReturnType<typeof vi.fn>>;

  async function settle(): Promise<void> {
    for (let i = 0; i < 4; i++) {
      await fixture.whenStable();
      await new Promise((r) => setTimeout(r));
    }
  }

  async function create(...keys: string[]): Promise<void> {
    privileges.set(grant(...keys));
    TestBed.configureTestingModule({ providers: [{ provide: AdminHrApi, useValue: api }, { provide: AuthService, useValue: { privileges } }] });
    fixture = TestBed.createComponent(AdminHrPage);
    root = fixture.nativeElement as HTMLElement;
    await settle();
  }

  const tabs = () => [...root.querySelectorAll('[data-testid="tab"]')].map((t) => t.textContent!.trim());

  beforeEach(() => {
    // the sections load their own data; for this test nothing has to arrive
    api = new Proxy({} as Record<string, ReturnType<typeof vi.fn>>, { get: (target, key: string) => (target[key] ??= vi.fn().mockImplementation(never)) });
  });

  it('is titled "Personal-Einstellungen"', async () => {
    await create('absence:manage-vacation-rules');

    expect(root.querySelector('h1')!.textContent).toContain('Personal-Einstellungen');
  });

  it('shows a tab for every section the person has the privilege for, and no other', async () => {
    await create('absence:manage-vacation-rules', 'unavailability:manage-kinds');

    expect(tabs()).toEqual(['Urlaub', 'Nicht-Verfügbarkeitsarten']);
  });

  it('shows all six tabs with all privileges', async () => {
    await create(
      'absence:manage-vacation-rules', 'workingtime:manage-rules', 'certificate:manage-retention', 'employee:manage-lists', 'unavailability:manage-kinds', 'absence:manage-holiday-opening',
    );

    expect(tabs()).toEqual(['Urlaub', 'Arbeitszeitkonto', 'Krankmeldung und AU', 'Mitarbeiterkarte', 'Nicht-Verfügbarkeitsarten', 'Feiertage']);
  });

  it('shows the AU tab for the access log alone and the card tab for the number series alone', async () => {
    await create('certificate:read-access-log', 'employee:manage-number-series');

    expect(tabs()).toEqual(['Krankmeldung und AU', 'Mitarbeiterkarte']);
  });

  it('opens the first section the person may see, and its data is loaded, the others are not', async () => {
    await create('workingtime:manage-rules', 'absence:manage-holiday-opening');

    expect(root.querySelector('[data-testid="section-working-time"]')).not.toBeNull();
    expect(root.querySelector('[data-testid="section-holidays"]')).toBeNull();
    expect(api['workingTimeRules']).toHaveBeenCalled();
    expect(api['stores']).not.toHaveBeenCalled();
    expect(root.querySelector('[data-testid="tab"][aria-selected="true"]')!.textContent).toContain('Arbeitszeitkonto');
  });

  it('switches the section with a click on a tab', async () => {
    await create('absence:manage-vacation-rules', 'absence:manage-holiday-opening');
    (root.querySelectorAll('[data-testid="tab"]')[1] as HTMLElement).click();
    await settle();

    expect(root.querySelector('[data-testid="section-holidays"]')).not.toBeNull();
    expect(root.querySelector('[data-testid="section-vacation"]')).toBeNull();
    expect(api['stores']).toHaveBeenCalled();
  });

  it('moves between the tabs with the arrow keys', async () => {
    await create('absence:manage-vacation-rules', 'workingtime:manage-rules');
    (root.querySelector('[data-testid="tab"]') as HTMLElement).dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowRight', bubbles: true }));
    await settle();

    expect(root.querySelector('[data-testid="section-working-time"]')).not.toBeNull();
  });

  it('shows no sections and no tabs without any of the privileges', async () => {
    await create('absence:read-own');

    expect(root.querySelector('[data-testid="tab"]')).toBeNull();
    expect(root.querySelector('[data-testid="no-sections"]')).not.toBeNull();
    expect(api['vacationRules']).not.toHaveBeenCalled();
  });
});
