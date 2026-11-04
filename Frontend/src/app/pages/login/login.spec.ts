import { safeReturnUrl } from './login';

describe('safeReturnUrl', () => {
  it('keeps paths inside the app', () => {
    expect(safeReturnUrl('/absences')).toBe('/absences');
    expect(safeReturnUrl('/admin/hr?tab=1')).toBe('/admin/hr?tab=1');
  });

  it('falls back to the start page without or with an unsafe address', () => {
    expect(safeReturnUrl(null)).toBe('/');
    expect(safeReturnUrl('')).toBe('/');
    expect(safeReturnUrl('https://evil.example')).toBe('/');
    expect(safeReturnUrl('//evil.example')).toBe('/');
    expect(safeReturnUrl('absences')).toBe('/');
  });

  it('does not return to the login page itself', () => {
    expect(safeReturnUrl('/login')).toBe('/');
  });
});
