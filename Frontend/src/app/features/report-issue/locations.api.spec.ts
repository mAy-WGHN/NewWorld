import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ReportsApi } from '../reports/reports.api';
import { LocationsApi } from './locations.api';

describe('LocationsApi', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('loads the stores of the portal', async () => {
    const done = TestBed.inject(LocationsApi).list();
    http.expectOne('/svc/portal/api/locations').flush({ success: true, data: { items: [{ id: 'a', name: 'Augsburg', storeCode: '017', archived: false }], meta: { total: 1, offset: 0, limit: null } } });

    expect(await done).toEqual([{ id: 'a', name: 'Augsburg', storeCode: '017', archived: false }]);
  });
});

describe('ReportsApi.create', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('files a report', async () => {
    const body = { personnelNumber: 4711, category: 'bug' as const, severity: 'medium' as const, description: 'Scanner defekt', locationIds: ['a'] };

    const done = TestBed.inject(ReportsApi).create(body);
    const request = http.expectOne('/svc/portal/api/reports');
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual(body);
    request.flush({ success: true, data: { id: 'r1' } }, { status: 201, statusText: 'Created' });

    expect((await done).id).toBe('r1');
  });
});
