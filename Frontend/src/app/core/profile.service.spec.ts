import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { AuthService } from './auth/auth.service';
import { PrivilegeSet } from './auth/privileges';
import { ProfileService } from './profile.service';

const grant = (...keys: string[]) => new PrivilegeSet(keys.map((key) => ({ key, unrestricted: true, storeIds: [], employeeIds: [] })));

describe('ProfileService', () => {
  let http: HttpTestingController;
  const privileges = signal(grant('employee:read-own'));

  function create(): ProfileService {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), { provide: AuthService, useValue: { privileges } }],
    });
    http = TestBed.inject(HttpTestingController);
    return TestBed.inject(ProfileService);
  }

  beforeEach(() => privileges.set(grant('employee:read-own')));

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
