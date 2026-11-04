import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { AuthService } from '../core/auth/auth.service';
import { PrivilegeSet } from '../core/auth/privileges';
import { NotificationsService } from '../core/notifications.service';
import { ProfileService } from '../core/profile.service';
import { Shell } from './shell';

const grant = (...keys: string[]) => new PrivilegeSet(keys.map((key) => ({ key, unrestricted: true, storeIds: [], employeeIds: [] })));

describe('Shell', () => {
  let fixture: ComponentFixture<Shell>;
  let root: HTMLElement;
  const privileges = signal(grant('notification:read-own'));
  const displayName = signal<string | null>('Mehmet Demir');
  const initials = signal('MD');
  const personnelNumber = signal<string | null>('15302');
  const unread = signal<number | null>(3);
  let auth: { privileges: typeof privileges; logout: ReturnType<typeof vi.fn> };
  let profile: { load: ReturnType<typeof vi.fn>; clear: ReturnType<typeof vi.fn>; displayName: typeof displayName; initials: typeof initials; personnelNumber: typeof personnelNumber; firstName: ReturnType<typeof signal<string | null>> };
  let notifications: { refresh: ReturnType<typeof vi.fn>; clear: ReturnType<typeof vi.fn>; unread: typeof unread };
  let navigate: ReturnType<typeof vi.spyOn>;

  async function create(): Promise<void> {
    TestBed.configureTestingModule({
      providers: [provideRouter([]), { provide: AuthService, useValue: auth }, { provide: ProfileService, useValue: profile }, { provide: NotificationsService, useValue: notifications }],
    });
    navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    fixture = TestBed.createComponent(Shell);
    root = fixture.nativeElement as HTMLElement;
    await fixture.whenStable();
  }

  beforeEach(() => {
    privileges.set(grant('notification:read-own'));
    displayName.set('Mehmet Demir');
    initials.set('MD');
    personnelNumber.set('15302');
    unread.set(3);
    auth = { privileges, logout: vi.fn().mockResolvedValue(undefined) };
    profile = { load: vi.fn().mockResolvedValue(undefined), clear: vi.fn(), displayName, initials, personnelNumber, firstName: signal(null) };
    notifications = { refresh: vi.fn().mockResolvedValue(undefined), clear: vi.fn(), unread };
  });

  it('shows who is signed in', async () => {
    await create();

    expect(root.querySelector('.who')!.textContent).toContain('Mehmet Demir');
    expect(root.querySelector('.who')!.textContent).toContain('Personalnummer 15302');
    expect(root.querySelector('.avatar')!.textContent).toContain('MD');
  });

  it('says only "Angemeldet" if no name is known', async () => {
    displayName.set(null);
    personnelNumber.set(null);
    initials.set('?');
    await create();

    expect(root.querySelector('.who')!.textContent).toContain('Angemeldet');
  });

  it('loads the profile and the unread count when it opens', async () => {
    await create();

    expect(profile.load).toHaveBeenCalledTimes(1);
    expect(notifications.refresh).toHaveBeenCalledTimes(1);
  });

  it('shows the unread count at the bell', async () => {
    await create();

    expect(root.querySelector('[data-testid="badge"]')!.textContent).toContain('3');
  });

  it('shows no badge when everything is read and caps big numbers', async () => {
    unread.set(0);
    await create();
    expect(root.querySelector('[data-testid="badge"]')).toBeNull();

    unread.set(250);
    fixture.detectChanges();
    expect(root.querySelector('[data-testid="badge"]')!.textContent).toContain('99+');
  });

  it('has no bell and no messages link without the privilege', async () => {
    privileges.set(grant());
    await create();

    expect(root.querySelector('.bell')).toBeNull();
    expect(root.textContent).not.toContain('Mitteilungen');
  });

  it('has the entries of the side bar', async () => {
    await create();

    for (const label of ['Startseite', 'Alle Bereiche', 'Favoriten', 'Mitteilungen', 'Einstellungen', 'Hilfe']) {
      expect(root.querySelector('.side')!.textContent).toContain(label);
    }
  });

  it('signs out, forgets the person and goes to the login page', async () => {
    await create();

    root.querySelector<HTMLButtonElement>('[data-action="logout"]')!.click();
    await fixture.whenStable();
    await new Promise((r) => setTimeout(r));

    expect(auth.logout).toHaveBeenCalled();
    expect(profile.clear).toHaveBeenCalled();
    expect(notifications.clear).toHaveBeenCalled();
    expect(navigate).toHaveBeenCalledWith(['/login']);
  });

  it('opens and closes the menu on small screens', async () => {
    await create();
    const nav = root.querySelector('.side')!;
    expect(nav.classList.contains('open')).toBe(false);

    root.querySelector<HTMLButtonElement>('.menu-toggle')!.click();
    fixture.detectChanges();
    expect(nav.classList.contains('open')).toBe(true);

    root.querySelector<HTMLButtonElement>('.menu-toggle')!.click();
    fixture.detectChanges();
    expect(nav.classList.contains('open')).toBe(false);
  });
});
