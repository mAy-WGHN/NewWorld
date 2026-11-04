import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { AuthService } from '../core/auth/auth.service';
import { PrivilegeSet } from '../core/auth/privileges';
import { NavigationState, PageReloader } from '../core/navigation-state.service';
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

  it('has the fixed entries of the side bar', async () => {
    await create();

    for (const label of ['Startseite', 'Alle Bereiche', 'Favoriten', 'Einstellungen', 'Hilfe']) {
      expect(root.querySelector('.side')!.textContent).toContain(label);
    }
  });

  const sideLinks = () => [...root.querySelectorAll('.side a')].map((a) => ({ text: a.textContent?.trim() ?? '', href: a.getAttribute('href') }));
  const groupTitles = () => [...root.querySelectorAll('.side .side-title')].map((t) => t.textContent?.trim());

  it('lists the areas of the person directly in the side bar, so they are one click away', async () => {
    privileges.set(grant('absence:read-own', 'absence:create-own', 'workingtime:read-own', 'notification:read-own'));
    await create();

    const links = sideLinks();
    expect(links).toContainEqual({ text: 'Meine Krankmeldungen', href: '/my-sick-notes' });
    expect(links).toContainEqual({ text: 'Mein Urlaub', href: '/my-vacation' });
    expect(links).toContainEqual({ text: 'Meine Abwesenheiten', href: '/absences' });
    expect(links).toContainEqual({ text: 'Arbeitszeitkonto', href: '/working-time' });
    expect(links).toContainEqual({ text: 'Mitteilungen', href: '/notifications' });
  });

  it('groups the areas and names a group only if the person has something in it', async () => {
    privileges.set(grant('absence:read-own', 'absence:create-own'));
    await create();

    expect(groupTitles()).toEqual(['Persönlich', 'Portal']);
  });

  it('shows the areas without privilege (report a problem, FAQ) to everybody, but no others', async () => {
    privileges.set(grant());
    await create();

    const texts = sideLinks().map((l) => l.text);
    expect(texts).toContain('Störung melden');
    expect(texts).toContain('FAQ');
    expect(texts).not.toContain('Meine Krankmeldungen');
    expect(texts).not.toContain('Rollen und Rechte');
  });

  it('shows the administration group to the people who may administer', async () => {
    privileges.set(grant('access:role:read', 'template:manage'));
    await create();

    expect(groupTitles()).toContain('Verwaltung');
    expect(sideLinks().map((l) => l.text)).toContain('Rollen und Rechte');
  });

  it('names the messages once in the side bar', async () => {
    privileges.set(grant('notification:read-own'));
    await create();

    expect(sideLinks().filter((l) => l.href === '/notifications')).toHaveLength(1);
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

  describe('when a page is slow or cannot be loaded', () => {
    const loading = signal(false);
    const error = signal<string | null>(null);
    const errorDetail = signal<string | null>(null);
    let reloadNow: ReturnType<typeof vi.fn>;

    async function createWithNavigation(): Promise<void> {
      TestBed.configureTestingModule({
        providers: [
          provideRouter([]),
          { provide: AuthService, useValue: auth },
          { provide: ProfileService, useValue: profile },
          { provide: NotificationsService, useValue: notifications },
          { provide: NavigationState, useValue: { loading, error, errorDetail } },
          { provide: PageReloader, useValue: { reloadNow, reloadOnce: vi.fn() } },
        ],
      });
      fixture = TestBed.createComponent(Shell);
      root = fixture.nativeElement as HTMLElement;
      await fixture.whenStable();
    }

    beforeEach(() => {
      loading.set(false);
      error.set(null);
      errorDetail.set(null);
      reloadNow = vi.fn();
    });

    it('shows a loading bar while a page takes long', async () => {
      await createWithNavigation();
      expect(root.querySelector('[role="progressbar"]')).toBeNull();

      loading.set(true);
      fixture.detectChanges();

      expect(root.querySelector('[role="progressbar"]')).not.toBeNull();
    });

    it('says so when a page cannot be loaded, with the reason and a button to load again', async () => {
      await createWithNavigation();
      error.set('Diese Seite konnte nicht geladen werden.');
      errorDetail.set('Failed to fetch dynamically imported module');
      fixture.detectChanges();

      const banner = root.querySelector('[data-testid="nav-error"]')!;
      expect(banner.textContent).toContain('Diese Seite konnte nicht geladen werden.');
      expect(banner.textContent).toContain('Failed to fetch dynamically imported module');

      banner.querySelector<HTMLButtonElement>('button')!.click();
      expect(reloadNow).toHaveBeenCalled();
    });

    it('shows nothing when all is well', async () => {
      await createWithNavigation();

      expect(root.querySelector('[data-testid="nav-error"]')).toBeNull();
    });
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
