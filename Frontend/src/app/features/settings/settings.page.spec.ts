import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { AuthService } from '../../core/auth/auth.service';
import { PrivilegeGrant, PrivilegeSet } from '../../core/auth/privileges';
import { ProfileService } from '../../core/profile.service';
import { ThemeService } from '../../core/theme.service';
import { NotificationsApi } from '../notifications/notifications.api';
import { SettingsPage } from './settings.page';

const grants: PrivilegeGrant[] = [
  { key: 'absence:read', unrestricted: false, storeIds: ['a', 'b'], employeeIds: [] },
  { key: 'preference:read-own', unrestricted: true, storeIds: [], employeeIds: [] },
  { key: 'preference:update-own', unrestricted: true, storeIds: [], employeeIds: [] },
];

describe('SettingsPage', () => {
  let fixture: ComponentFixture<SettingsPage>;
  let root: HTMLElement;
  let theme: ThemeService;

  async function settle(): Promise<void> {
    for (let i = 0; i < 4; i++) {
      await fixture.whenStable();
      await new Promise((r) => setTimeout(r));
    }
  }

  async function create(roles: string[] = ['Public User', 'Filialleitung']): Promise<void> {
    localStorage.clear();
    TestBed.configureTestingModule({
      providers: [
        { provide: AuthService, useValue: { privileges: signal(new PrivilegeSet(grants)), roles: signal(roles) } },
        { provide: ProfileService, useValue: { displayName: signal('Mehmet Demir'), personnelNumber: signal('15302'), storeName: signal<string | null>('Augsburg Mitte (101)') } },
        { provide: NotificationsApi, useValue: { preferences: vi.fn().mockResolvedValue([]), setPreference: vi.fn() } },
      ],
    });
    theme = TestBed.inject(ThemeService);
    fixture = TestBed.createComponent(SettingsPage);
    root = fixture.nativeElement as HTMLElement;
    await settle();
  }

  const q = <T extends Element = HTMLElement>(selector: string): T | null => root.querySelector<T>(selector);

  async function openTab(label: string): Promise<void> {
    const tab = [...root.querySelectorAll<HTMLElement>('[role="tab"]')].find((t) => t.textContent?.includes(label))!;
    tab.click();
    await settle();
  }

  it('is titled "Einstellungen" with the four tabs', async () => {
    await create();

    expect(q('h1')!.textContent).toContain('Einstellungen');
    expect([...root.querySelectorAll('[role="tab"]')].map((t) => t.textContent?.trim())).toEqual(['Darstellung', 'Mitteilungen', 'Anmeldung', 'Rollen und Rechte']);
  });

  it('changes the appearance and remembers it', async () => {
    await create();

    q<HTMLElement>('[data-theme="dark"]')!.click();
    await settle();

    expect(theme.mode()).toBe('dark');
    expect(document.documentElement.style.colorScheme).toBe('dark');
  });

  it('offers the choice of notifications', async () => {
    await create();
    await openTab('Mitteilungen');

    expect(q('app-notification-preferences')).not.toBeNull();
  });

  it('says who is signed in and that the PIN is managed in Business Central', async () => {
    await create();
    await openTab('Anmeldung');

    const text = q('[data-testid="login-info"]')!.textContent!;
    expect(text).toContain('Mehmet Demir');
    expect(text).toContain('15302');
    expect(text).toContain('Augsburg Mitte (101)');
    expect(text).toContain('Business Central');
  });

  it('shows the own roles and the privileges with their reach, read only', async () => {
    await create();
    await openTab('Rollen und Rechte');

    expect(q('[data-testid="roles"]')!.textContent).toContain('Filialleitung');
    const text = q('[data-testid="privileges"]')!.textContent!;
    expect(text).toContain('absence');
    expect(text).toContain('read');
    expect(text).toContain('2 Filialen');
    expect(text).toContain('überall');
    expect(q('[data-testid="privileges"] button')).toBeNull();
  });

  it('says so when there are no roles', async () => {
    await create([]);
    await openTab('Rollen und Rechte');

    expect(q('[data-testid="roles"]')!.textContent).toContain('Keine Rolle');
  });
});
