import { PrivilegeSet } from '../../core/auth/privileges';
import { notificationTarget } from './notification-link';

const grant = (...keys: string[]) => new PrivilegeSet(keys.map((key) => ({ key, unrestricted: true, storeIds: [], employeeIds: [] })));
const note = (templateKey: string, deepLink: string | null = '/x/1') => ({ templateKey, deepLink });

describe('notificationTarget', () => {
  it('sends a vacation request to the approvals of the person who decides', () => {
    expect(notificationTarget(note('workforce.vacation-request.supervisor', '/absences/1'), grant('absence:approve'))).toBe('/approvals');
  });

  it('falls back to the next page the person may open, then to the list of notifications', () => {
    expect(notificationTarget(note('workforce.sick-note.store-manager'), grant('absence:read-availability'))).toBe('/team-absences');
    expect(notificationTarget(note('workforce.sick-note.store-manager'), grant('certificate:review'))).toBe('/sick-notes');
    expect(notificationTarget(note('workforce.vacation-request.supervisor'), grant())).toBe('/notifications');
  });

  it('leads the person to their own pages for their own matters', () => {
    expect(notificationTarget(note('workforce.vacation-decision.requester', '/absences/1'), grant('absence:read-own'))).toBe('/my-vacation');
    expect(notificationTarget(note('workforce.sick-note-certificate-rejected.employee', '/absences/1'), grant('absence:read-own'))).toBe('/my-sick-notes');
    expect(notificationTarget(note('workforce.employee-change-decision.employee', '/me/employee-card'), grant('employee:read-own'))).toBe('/employee-card');
  });

  it('leads HR to the pages for HR matters', () => {
    expect(notificationTarget(note('workforce.employee-change-request.hr', '/employee-change-requests/1'), grant('employee:read'))).toBe('/employees');
    expect(notificationTarget(note('portal.report-created.handler', '/reports/1'), grant('report:read'))).toBe('/reports');
  });

  it('keeps the area of an unknown kind if it exists and is allowed, otherwise opens the list', () => {
    expect(notificationTarget(note('new.kind', '/reports/9'), grant('report:read'))).toBe('/reports');
    expect(notificationTarget(note('new.kind', '/reports/9'), grant())).toBe('/notifications');
    expect(notificationTarget(note('new.kind', '/nowhere/9'), grant())).toBe('/notifications');
    expect(notificationTarget(note('new.kind', 'https://evil.example'), grant())).toBe('/notifications');
    expect(notificationTarget(note('new.kind', null), grant())).toBe('/notifications');
  });
});
