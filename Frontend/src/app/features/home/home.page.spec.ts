import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { AuthService } from '../../core/auth/auth.service';
import { PrivilegeSet } from '../../core/auth/privileges';
import { NotificationsService } from '../../core/notifications.service';
import { ProfileService } from '../../core/profile.service';
import { Absence } from '../absences/absence-models';
import { AbsencesApi } from '../absences/absences.api';
import { HomeApi } from './home.api';
import { HomePage } from './home.page';

const grant = (...keys: string[]) => new PrivilegeSet(keys.map((key) => ({ key, unrestricted: true, storeIds: [], employeeIds: [] })));

const vacation: Absence = { id: 'v1', employeeId: 'e1', type: 'Vacation', status: 'Approved', from: '2099-04-24', to: '2099-04-28', certificateStatus: null, certifiedThrough: null };
const sickWithoutAu: Absence = { id: 's1', employeeId: 'e1', type: 'Sickness', status: 'Reported', from: '2099-01-02', to: null, certificateStatus: 'Missing', certifiedThrough: null };

describe('HomePage', () => {
  let fixture: ComponentFixture<HomePage>;
  let root: HTMLElement;
  const privileges = signal(grant());
  const firstName = signal<string | null>('Mehmet');
  const unread = signal<number | null>(0);
  let absences: ReturnType<typeof vi.fn>;
  let pending: ReturnType<typeof vi.fn>;

  async function create(): Promise<void> {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: AuthService, useValue: { privileges } },
        { provide: ProfileService, useValue: { firstName } },
        { provide: NotificationsService, useValue: { unread } },
        { provide: AbsencesApi, useValue: { list: absences } },
        { provide: HomeApi, useValue: { pendingApprovals: pending } },
      ],
    });
    fixture = TestBed.createComponent(HomePage);
    root = fixture.nativeElement as HTMLElement;
    for (let i = 0; i < 4; i++) {
      await fixture.whenStable();
      await new Promise((r) => setTimeout(r));
    }
  }

  const text = () => root.textContent ?? '';
  const q = (selector: string) => root.querySelector(selector);
  const links = () => [...root.querySelectorAll('a')].map((a) => a.textContent?.trim());

  beforeEach(() => {
    privileges.set(grant('absence:read-own', 'absence:create-own'));
    firstName.set('Mehmet');
    unread.set(0);
    absences = vi.fn().mockResolvedValue([]);
    pending = vi.fn().mockResolvedValue(0);
  });

  it('greets the person by first name', async () => {
    await create();

    expect(q('h1')!.textContent).toContain('Hallo Mehmet!');
  });

  it('greets without a name if none is known', async () => {
    firstName.set(null);
    await create();

    expect(q('h1')!.textContent?.trim()).toMatch(/^Hallo!/);
  });

  it('shows the four areas of the portal', async () => {
    await create();

    for (const title of ['Erledigen', 'Finden', 'Lernen', 'Mein Bereich']) {
      expect(text()).toContain(title);
    }
  });

  it('offers quick links only for what the person may open', async () => {
    await create();

    expect(links()).toContain('Krankmeldung');
    expect(links()).toContain('Urlaub beantragen');
    expect(links()).toContain('Meine Abwesenheiten');
    expect(links()).toContain('FAQ');
    expect(links()).not.toContain('Genehmigungen');
    expect(links()).not.toContain('Arbeitszeitkonto');
  });

  it('says plainly that an area is not built yet instead of showing empty links', async () => {
    await create();

    expect(text()).toContain('Dieser Bereich folgt');
    expect(text()).toContain('Anleitungen, Filialordner und Angebote folgen');
  });

  it('says everything is done when no task is open', async () => {
    await create();

    expect(text()).toContain('Alles erledigt');
  });

  it('lists what needs attention: missing AU, requests to decide, unread messages', async () => {
    privileges.set(grant('absence:read-own', 'absence:approve', 'notification:read-own'));
    absences.mockResolvedValue([sickWithoutAu]);
    pending.mockResolvedValue(2);
    unread.set(3);
    await create();

    expect(text()).toContain('Für 1 Krankmeldung fehlt die AU');
    expect(text()).toContain('2 Urlaubsanträge warten auf deine Entscheidung');
    expect(text()).toContain('3 ungelesene Mitteilungen');
    expect(text()).not.toContain('Alles erledigt');
  });

  it('shows the next vacation as an appointment', async () => {
    absences.mockResolvedValue([vacation]);
    await create();

    const appointment = q('[data-testid="appointment"]')!;
    expect(appointment.textContent).toContain('APR');
    expect(appointment.textContent).toContain('24');
    expect(appointment.textContent).toContain('Urlaub');
    expect(appointment.textContent).toContain('24.04.2099 – 28.04.2099');
  });

  it('says so when there are no appointments', async () => {
    await create();

    expect(text()).toContain('Keine anstehenden Termine');
  });

  it('does not ask for absences or approvals without the privilege', async () => {
    privileges.set(grant());
    await create();

    expect(absences).not.toHaveBeenCalled();
    expect(pending).not.toHaveBeenCalled();
  });

  it('is honest about figures and news that do not exist yet', async () => {
    await create();

    expect(q('[data-testid="figures"]')!.textContent).toContain('folgen');
    expect(q('[data-testid="news"]')!.textContent).toContain('folgen');
  });

  it('still works if the services do not answer', async () => {
    privileges.set(grant('absence:read-own', 'absence:approve'));
    absences.mockRejectedValue(new Error('down'));
    pending.mockRejectedValue(new Error('down'));
    await create();

    expect(q('h1')).not.toBeNull();
    expect(text()).toContain('Alles erledigt');
  });
});
