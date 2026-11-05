import { HttpErrorResponse } from '@angular/common/http';
import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { AuthService } from '../../core/auth/auth.service';
import { PrivilegeSet } from '../../core/auth/privileges';
import { NotificationsService } from '../../core/notifications.service';
import { NotificationItem, NotificationType } from './notification-models';
import { NotificationsApi, PAGE_SIZE } from './notifications.api';
import { NotificationsPage } from './notifications.page';

const item = (patch: Partial<NotificationItem>): NotificationItem => ({
  id: 'n',
  templateKey: 'workforce.vacation-decision.requester',
  title: 'Urlaub genehmigt',
  body: 'Dein Urlaub wurde genehmigt.',
  deepLink: null,
  createdAt: new Date(2026, 10, 4, 9, 30).toISOString(),
  readAt: null,
  ...patch,
});

const unread1 = item({ id: 'u1', title: 'Neuer Urlaubsantrag', body: 'Ein Antrag wartet.', deepLink: '/approvals' });
const unread2 = item({ id: 'u2', title: 'AU nachreichen', body: 'Bitte reiche die AU nach.' });
const read1 = item({ id: 'r1', title: 'Alte Mitteilung', readAt: new Date(2026, 10, 3).toISOString() });

const types: NotificationType[] = [
  { templateKey: 'workforce.vacation-decision.requester', mandatory: false, channels: [{ channel: 'InApp', enabled: true }, { channel: 'Push', enabled: true }, { channel: 'Email', enabled: false }] },
  { templateKey: 'workforce.sick-note-certificate-rejected.employee', mandatory: true, channels: [{ channel: 'InApp', enabled: true }, { channel: 'Push', enabled: true }, { channel: 'Email', enabled: true }] },
];

const grant = (...keys: string[]) => new PrivilegeSet(keys.map((key) => ({ key, unrestricted: true, storeIds: [], employeeIds: [] })));
const ALL = ['notification:read-own', 'notification:update-own', 'preference:read-own', 'preference:update-own'];

describe('NotificationsPage', () => {
  let api: Record<'list' | 'markRead' | 'markAllRead' | 'preferences' | 'setPreference', ReturnType<typeof vi.fn>>;
  let refreshCount: ReturnType<typeof vi.fn>;
  let fixture: ComponentFixture<NotificationsPage>;
  let root: HTMLElement;

  async function create(privileges = grant(...ALL)): Promise<void> {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([{ path: 'my-vacation', children: [] }]),
        { provide: NotificationsApi, useValue: api },
        { provide: AuthService, useValue: { privileges: signal(privileges) } },
        { provide: NotificationsService, useValue: { refresh: refreshCount } },
      ],
    });
    fixture = TestBed.createComponent(NotificationsPage);
    root = fixture.nativeElement as HTMLElement;
    await settle();
  }

  async function settle(): Promise<void> {
    for (let i = 0; i < 4; i++) {
      await fixture.whenStable();
      await new Promise((r) => setTimeout(r));
    }
  }

  const q = <T extends Element = HTMLElement>(selector: string): T | null => root.querySelector<T>(selector);
  const ids = () => [...root.querySelectorAll('[data-testid="notification"]')].map((e) => e.getAttribute('data-id'));

  async function click(selector: string): Promise<void> {
    q<HTMLButtonElement>(selector)!.click();
    await settle();
  }

  beforeEach(() => {
    api = {
      list: vi.fn().mockResolvedValue([unread1, unread2, read1]),
      markRead: vi.fn().mockResolvedValue(undefined),
      markAllRead: vi.fn().mockResolvedValue(undefined),
      preferences: vi.fn().mockResolvedValue(types),
      setPreference: vi.fn().mockResolvedValue(undefined),
    };
    refreshCount = vi.fn().mockResolvedValue(undefined);
  });

  it('is titled "Mitteilungen" with "Alle als gelesen markieren" at the top right', async () => {
    await create();

    expect(q('h1')!.textContent).toContain('Mitteilungen');
    expect(q('.head [data-testid="read-all"]')!.textContent).toContain('Alle als gelesen markieren');
  });

  it('puts the unread ones at the top in a yellow block, the read ones below, each once', async () => {
    await create();

    expect(ids()).toEqual(['u1', 'u2', 'r1']);
    const block = q('[data-testid="attention"]')!;
    expect(block.textContent).toContain('Neu');
    expect(block.contains(q('[data-id="u1"]'))).toBe(true);
    expect(q('[data-id="u1"]')!.classList.contains('attention')).toBe(true);
    expect(q('[data-id="r1"]')!.classList.contains('attention')).toBe(false);
    expect(root.querySelectorAll('[data-id="u1"]').length).toBe(1);
    expect(root.textContent).toContain('Frühere Mitteilungen');
  });

  it('shows title, text and the time of a notification', async () => {
    await create();

    const text = q('[data-id="u1"]')!.textContent!;
    expect(text).toContain('Neuer Urlaubsantrag');
    expect(text).toContain('Ein Antrag wartet.');
    expect(text).toContain('04.11.2026 09:30');
  });

  it('says so when there are none', async () => {
    api['list'].mockResolvedValue([]);
    await create();

    expect(root.textContent).toContain('Keine Mitteilungen');
    expect(q('[data-testid="read-all"]')).toBeNull();
  });

  it('shows no yellow block and no "read all" when everything is read', async () => {
    api['list'].mockResolvedValue([read1]);
    await create();

    expect(q('[data-testid="attention"]')).toBeNull();
    expect(q('[data-testid="read-all"]')).toBeNull();
  });

  it('marks one as read, loads again and tells the bell', async () => {
    await create();
    api['list'].mockClear();

    await click('[data-id="u2"] [data-action="read"]');

    expect(api['markRead']).toHaveBeenCalledWith('u2');
    expect(api['list']).toHaveBeenCalled();
    expect(refreshCount).toHaveBeenCalled();
  });

  it('marks all as read', async () => {
    await create();

    await click('[data-testid="read-all"]');

    expect(api['markAllRead']).toHaveBeenCalled();
    expect(refreshCount).toHaveBeenCalled();
  });

  it('opens the page a notification points to (by its kind) and marks it as read on the way', async () => {
    await create(grant(...ALL, 'absence:read-own', 'absence:create-own'));

    const link = q<HTMLAnchorElement>('[data-id="u1"] a[data-action="open"]')!;
    expect(link.getAttribute('href')).toBe('/my-vacation');
    link.addEventListener('click', (e) => e.preventDefault());
    link.click();
    await settle();

    expect(api['markRead']).toHaveBeenCalledWith('u1');
  });

  it('offers no link for an address that leaves the portal', async () => {
    api['list'].mockResolvedValue([item({ id: 'x', deepLink: 'https://evil.example' })]);
    await create();

    expect(q('a[data-action="open"]')).toBeNull();
  });

  it('loads older ones when a full page came back', async () => {
    const page = Array.from({ length: PAGE_SIZE }, (_, i) => item({ id: `p${i}`, createdAt: new Date(2026, 9, 1, 0, 0, PAGE_SIZE - i).toISOString(), readAt: new Date(2026, 9, 2).toISOString() }));
    api['list'].mockImplementation((_take: number, before?: string) => Promise.resolve(before ? [item({ id: 'older', readAt: new Date(2026, 8, 1).toISOString() })] : page));
    await create();
    expect(ids()).toHaveLength(PAGE_SIZE);

    await click('[data-testid="more"]');

    expect(api['list']).toHaveBeenCalledWith(PAGE_SIZE, page[PAGE_SIZE - 1].createdAt);
    expect(ids()).toContain('older');
    expect(q('[data-testid="more"]')).toBeNull();
  });

  it('shows what the person gets, per kind and channel, in plain words', async () => {
    await create();

    const text = q('[data-testid="preferences"]')!.textContent!;
    expect(text).toContain('Entscheidung über deinen Urlaubsantrag');
    expect(text).toContain('Im Portal');
    expect(text).toContain('Push');
    expect(text).toContain('E-Mail');
    expect(q<HTMLInputElement>('[data-key="workforce.vacation-decision.requester"][data-channel="Email"] input')!.checked).toBe(false);
    expect(q<HTMLInputElement>('[data-key="workforce.vacation-decision.requester"][data-channel="Push"] input')!.checked).toBe(true);
  });

  it('switches a channel on or off', async () => {
    await create();

    q<HTMLInputElement>('[data-key="workforce.vacation-decision.requester"][data-channel="Push"] input')!.click();
    await settle();

    expect(api['setPreference']).toHaveBeenCalledWith('workforce.vacation-decision.requester', 'Push', false);
  });

  it('cannot switch off what is mandatory', async () => {
    await create();

    const box = q<HTMLInputElement>('[data-key="workforce.sick-note-certificate-rejected.employee"][data-channel="Push"] input')!;
    expect(box.disabled).toBe(true);
    expect(q('[data-key="workforce.sick-note-certificate-rejected.employee"]')!.closest('tr')!.textContent).toContain('Pflicht');
  });

  it('shows the choices but does not let a person change them without the privilege', async () => {
    await create(grant('notification:read-own', 'preference:read-own'));

    expect(q('[data-testid="preferences"]')).not.toBeNull();
    expect(q<HTMLInputElement>('[data-key="workforce.vacation-decision.requester"][data-channel="Push"] input')!.disabled).toBe(true);
  });

  it('does not show the choices without the privilege to read them', async () => {
    await create(grant('notification:read-own', 'notification:update-own'));

    expect(q('[data-testid="preferences"]')).toBeNull();
    expect(api['preferences']).not.toHaveBeenCalled();
  });

  it('shows no actions to a person who may only read', async () => {
    await create(grant('notification:read-own'));

    expect(ids()).toHaveLength(3);
    expect(q('[data-testid="read-all"]')).toBeNull();
    expect(q('[data-action="read"]')).toBeNull();
  });

  it('shows the answer of the service when a choice is refused and puts the switch back', async () => {
    api['setPreference'].mockRejectedValue(new HttpErrorResponse({ status: 409, error: { title: 'Mandatory' } }));
    await create();

    q<HTMLInputElement>('[data-key="workforce.vacation-decision.requester"][data-channel="Push"] input')!.click();
    await settle();

    expect(q('[role="alert"]')!.textContent).toContain('nicht');
    expect(q<HTMLInputElement>('[data-key="workforce.vacation-decision.requester"][data-channel="Push"] input')!.checked).toBe(true);
  });

  it('tells the person if the notifications cannot be loaded', async () => {
    api['list'].mockRejectedValue(new HttpErrorResponse({ status: 500 }));
    await create();

    expect(q('[role="alert"]')!.textContent).toContain('geladen');
  });
});
