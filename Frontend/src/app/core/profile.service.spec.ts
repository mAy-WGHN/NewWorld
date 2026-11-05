import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { AuthService } from './auth/auth.service';
import { PrivilegeSet } from './auth/privileges';
import { ProfileService, splitName } from './profile.service';

const grant = (...keys: string[]) => new PrivilegeSet(keys.map((key) => ({ key, unrestricted: true, storeIds: [], employeeIds: [] })));

describe('ProfileService', () => {
  let http: HttpTestingController;
  const privileges = signal(grant('employee:read-own'));
  const accessName = signal<string | null>(null);
  const accessNumber = signal<string | null>(null);

  function create(): ProfileService {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), { provide: AuthService, useValue: { privileges, name: accessName, personnelNumber: accessNumber } }],
    });
    http = TestBed.inject(HttpTestingController);
    return TestBed.inject(ProfileService);
  }

  beforeEach(() => {
    privileges.set(grant('employee:read-own'));
    accessName.set(null);
    accessNumber.set(null);
  });

  afterEach(() => http.verify());

  it('takes the name and personnel number from the own employee card', async () => {
    const profile = create();

    const done = profile.load();
    http.expectOne('/svc/workforce/api/me/employee-card').flush({ firstName: 'Mehmet', lastName: 'Demir', personnelNumber: '15302' });
    await done;

    expect(profile.firstName()).toBe('Mehmet');
    expect(profile.displayName()).toBe('Mehmet Demir');
    expect(profile.initials()).toBe('MD');
    expect(profile.personnelNumber()).toBe('15302');
  });

  it('has no name for a person without a card (e.g. coming from BC) and stays quiet about it', async () => {
    const profile = create();

    const done = profile.load();
    http.expectOne('/svc/workforce/api/me/employee-card').flush({}, { status: 404, statusText: 'Not Found' });
    await done;

    expect(profile.firstName()).toBeNull();
    expect(profile.displayName()).toBeNull();
    expect(profile.initials()).toBe('?');
  });

  it('uses the name from Access for a person without a card, so that everybody has a name', async () => {
    accessName.set('Erika Mustermann');
    accessNumber.set('10042');
    const profile = create();

    const done = profile.load();
    http.expectOne('/svc/workforce/api/me/employee-card').flush({}, { status: 404, statusText: 'Not Found' });
    await done;

    expect(profile.displayName()).toBe('Erika Mustermann');
    expect(profile.firstName()).toBe('Erika');
    expect(profile.initials()).toBe('EM');
    expect(profile.personnelNumber()).toBe('10042');
  });

  it('shows the name from Access at once, before the card has answered', () => {
    accessName.set('Erika Mustermann');
    const profile = create();

    expect(profile.displayName()).toBe('Erika Mustermann');
    http.expectNone('/svc/workforce/api/me/employee-card');
  });

  it('prefers the names of the card to the one from Access', async () => {
    accessName.set('Demir, Mehmet Ali');
    accessNumber.set('99');
    const profile = create();

    const done = profile.load();
    http.expectOne('/svc/workforce/api/me/employee-card').flush({ firstName: 'Mehmet', lastName: 'Demir', personnelNumber: '15302' });
    await done;

    expect(profile.displayName()).toBe('Mehmet Demir');
    expect(profile.personnelNumber()).toBe('15302');
  });

  it('falls back to the name from Access when the privilege to read the card is missing', async () => {
    privileges.set(grant('faq:read'));
    accessName.set('Max Muster');
    const profile = create();

    await profile.load();

    expect(profile.displayName()).toBe('Max Muster');
  });

  it('does not ask without the privilege to read the own card', async () => {
    privileges.set(grant('faq:read'));
    const profile = create();

    await profile.load();

    http.expectNone('/svc/workforce/api/me/employee-card');
    expect(profile.firstName()).toBeNull();
  });

  it('forgets the person on sign-out', async () => {
    const profile = create();
    const done = profile.load();
    http.expectOne('/svc/workforce/api/me/employee-card').flush({ firstName: 'Mehmet', lastName: 'Demir', personnelNumber: '1' });
    await done;

    profile.clear();

    expect(profile.firstName()).toBeNull();
    expect(profile.personnelNumber()).toBeNull();
  });
});

describe('splitName', () => {
  it('splits "Vorname Nachname"', () => {
    expect(splitName('Erika Mustermann')).toEqual({ firstName: 'Erika', lastName: 'Mustermann' });
  });

  it('splits "Nachname, Vorname"', () => {
    expect(splitName('Mustermann, Erika')).toEqual({ firstName: 'Erika', lastName: 'Mustermann' });
  });

  it('lets several words after the first belong to the last name', () => {
    expect(splitName('Anna van der Berg')).toEqual({ firstName: 'Anna', lastName: 'van der Berg' });
  });

  it('copes with one word, extra blanks and nothing', () => {
    expect(splitName('Cher')).toEqual({ firstName: 'Cher', lastName: '' });
    expect(splitName('  Erika   Mustermann ')).toEqual({ firstName: 'Erika', lastName: 'Mustermann' });
    expect(splitName('')).toEqual({ firstName: '', lastName: '' });
  });
});
