import { channelLabel, formatDateTime, safeDeepLink, typeLabel } from './notification-models';

describe('typeLabel', () => {
  it('names the known kinds of notification in plain words', () => {
    expect(typeLabel('workforce.vacation-decision.requester')).toBe('Entscheidung über deinen Urlaubsantrag');
    expect(typeLabel('workforce.vacation-request.supervisor')).toBe('Neuer Urlaubsantrag zu entscheiden');
    expect(typeLabel('workforce.sick-note-certificate-reminder.employee')).toBe('Erinnerung: AU nachreichen');
    expect(typeLabel('portal.report-created.handler')).toBe('Neue Meldung im Portal');
  });

  it('shows an unknown key as it is, so nothing disappears', () => {
    expect(typeLabel('something.new.thing')).toBe('something.new.thing');
  });
});

describe('channelLabel', () => {
  it('names the channels', () => {
    expect(channelLabel('InApp')).toBe('Im Portal');
    expect(channelLabel('Push')).toBe('Push');
    expect(channelLabel('Email')).toBe('E-Mail');
    expect(channelLabel('Fax')).toBe('Fax');
  });
});

describe('formatDateTime', () => {
  it('shows day and time of the local zone the German way', () => {
    const date = new Date(2026, 10, 4, 9, 5);

    expect(formatDateTime(date.toISOString())).toBe('04.11.2026 09:05');
  });

  it('handles the year change and the leap day', () => {
    expect(formatDateTime(new Date(2026, 11, 31, 23, 59).toISOString())).toBe('31.12.2026 23:59');
    expect(formatDateTime(new Date(2028, 1, 29, 0, 0).toISOString())).toBe('29.02.2028 00:00');
  });
});

describe('safeDeepLink', () => {
  it('keeps paths inside the portal', () => {
    expect(safeDeepLink('/my-vacation')).toBe('/my-vacation');
    expect(safeDeepLink('/employees/123/working-time')).toBe('/employees/123/working-time');
  });

  it('refuses everything that leaves the portal', () => {
    expect(safeDeepLink('https://evil.example')).toBeNull();
    expect(safeDeepLink('//evil.example')).toBeNull();
    expect(safeDeepLink('javascript:alert(1)')).toBeNull();
    expect(safeDeepLink('my-vacation')).toBeNull();
    expect(safeDeepLink('')).toBeNull();
    expect(safeDeepLink(null)).toBeNull();
  });
});
