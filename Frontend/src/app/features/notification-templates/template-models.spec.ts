import {
  NotificationTemplate,
  allowedVariables,
  draftOf,
  editable,
  insertAt,
  isChanged,
  problemMessage,
  toSaveRequest,
} from './template-models';

const text = (title: string, body: string) => ({ title, body });

const template = (over: Partial<NotificationTemplate> = {}): NotificationTemplate => ({
  key: 'workforce.vacation-request.supervisor',
  mandatory: true,
  sensitiveText: false,
  mailMayContainSensitive: false,
  customized: false,
  default: { inApp: text('Urlaubsantrag', 'Neu: {employeeName}'), push: text('Neue Mitteilung', 'Öffnen.'), email: text('Betreff', 'Mailtext') },
  effective: { inApp: text('Urlaubsantrag', 'Neu: {employeeName}'), push: text('Neue Mitteilung', 'Öffnen.'), email: text('Betreff', 'Mailtext') },
  variables: [
    { name: 'USER.firstName', class: 1, source: 'Recipient' },
    { name: 'employeeName', class: 2, source: 'Sender' },
  ],
  changedAt: null,
  changedBy: null,
  ...over,
});

describe('editable', () => {
  it('lets HR change everything in a template without sensitive text', () => {
    expect(Object.values(editable(template())).every(Boolean)).toBe(true);
  });

  it('fixes push text and mail subject in a sensitive template, and the mail text unless it may carry sensitive data', () => {
    expect(editable(template({ sensitiveText: true }))).toEqual({
      inAppTitle: true, inAppBody: true, pushTitle: false, pushBody: false, emailTitle: false, emailBody: false,
    });
    expect(editable(template({ sensitiveText: true, mailMayContainSensitive: true })).emailBody).toBe(true);
    expect(editable(template({ sensitiveText: true, mailMayContainSensitive: true })).emailTitle).toBe(false);
  });
});

describe('allowedVariables', () => {
  const t = template();

  it('allows sensitive variables only in the in-app text, and in the mail text where the template allows it', () => {
    const names = (c: 'inApp' | 'push' | 'email', p: 'title' | 'body', tpl = t) => allowedVariables(tpl, c, p).map((v) => v.name);

    expect(names('inApp', 'body')).toEqual(['USER.firstName', 'employeeName']);
    expect(names('push', 'body')).toEqual(['USER.firstName']);
    expect(names('email', 'title')).toEqual(['USER.firstName']);
    expect(names('email', 'body')).toEqual(['USER.firstName']);
    expect(names('email', 'body', template({ mailMayContainSensitive: true }))).toEqual(['USER.firstName', 'employeeName']);
  });
});

describe('toSaveRequest', () => {
  it('sends all texts of a template without sensitive text', () => {
    const draft = draftOf(template());
    draft.push = text('Neuer Titel', 'Neuer Text');

    expect(toSaveRequest(template(), draft)).toEqual({ inApp: draft.inApp, push: draft.push, email: draft.email });
  });

  it('sends nothing for the fixed push and mail texts of a sensitive template', () => {
    const t = template({ sensitiveText: true });
    const draft = draftOf(t);
    draft.inApp = text('Neu', 'Neuer Text');

    expect(toSaveRequest(t, draft)).toEqual({ inApp: draft.inApp, push: null, email: null });
  });

  it('keeps the fixed mail subject when only the mail text may change', () => {
    const t = template({ sensitiveText: true, mailMayContainSensitive: true });
    const draft = draftOf(t);
    draft.email = text('Anderer Betreff', 'Neuer Mailtext');

    expect(toSaveRequest(t, draft).email).toEqual({ title: 'Betreff', body: 'Neuer Mailtext' });
  });
});

describe('draftOf and isChanged', () => {
  it('starts with the texts in force and knows when something changed', () => {
    const t = template({ effective: { inApp: text('Eigener Titel', 'Eigener Text'), push: null, email: null }, customized: true });
    const draft = draftOf(t);

    expect(draft.inApp).toEqual(text('Eigener Titel', 'Eigener Text'));
    expect(draft.push).toEqual(text('Neue Mitteilung', 'Öffnen.')); // not changed: the default
    expect(isChanged(t, draft)).toBe(false);

    draft.email = { ...draft.email, body: 'Anders' };
    expect(isChanged(t, draft)).toBe(true);
  });

  it('does not share the objects of the template', () => {
    const t = template();
    const draft = draftOf(t);
    draft.inApp.title = 'x';

    expect(t.effective.inApp!.title).toBe('Urlaubsantrag');
  });
});

describe('insertAt', () => {
  it('inserts at the caret', () => {
    expect(insertAt('Hallo !', 6, 6, '$USER.firstName')).toEqual({ value: 'Hallo $USER.firstName!', caret: 21 });
  });

  it('replaces a selection', () => {
    expect(insertAt('Hallo Welt', 6, 10, '$x')).toEqual({ value: 'Hallo $x', caret: 8 });
  });

  it('keeps a wrong position inside the text', () => {
    expect(insertAt('ab', 99, 99, 'c')).toEqual({ value: 'abc', caret: 3 });
    expect(insertAt('ab', -3, 1, 'c')).toEqual({ value: 'cb', caret: 1 });
  });
});

describe('problemMessage', () => {
  it('names the rules in plain words and falls back to a general message', () => {
    expect(problemMessage('UnknownVariable')).toContain('Variable');
    expect(problemMessage('SensitiveInPushOrSubject')).toContain('Push');
    expect(problemMessage('SensitiveTextLocked')).toContain('fest vorgegeben');
    expect(problemMessage('SomethingNew')).toContain('nicht geklappt');
    expect(problemMessage(undefined)).toContain('nicht geklappt');
  });
});
