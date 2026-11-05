import { HttpErrorResponse } from '@angular/common/http';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { NotificationTemplate } from './template-models';
import { TemplateApi } from './template.api';
import { TemplatePage } from './template.page';

const text = (title: string, body: string) => ({ title, body });

const vacation: NotificationTemplate = {
  key: 'workforce.vacation-request.supervisor',
  mandatory: true,
  sensitiveText: true,
  mailMayContainSensitive: false,
  customized: false,
  default: { inApp: text('Urlaubsantrag', '{employeeName} hat Urlaub beantragt.'), push: text('Neue Mitteilung im Portal', 'Öffnen.'), email: text('Neue Mitteilung', 'Im Portal.') },
  effective: { inApp: text('Urlaubsantrag', '{employeeName} hat Urlaub beantragt.'), push: text('Neue Mitteilung im Portal', 'Öffnen.'), email: text('Neue Mitteilung', 'Im Portal.') },
  variables: [
    { name: 'USER.firstName', class: 1, source: 'Recipient' },
    { name: 'USER.lastName', class: 1, source: 'Recipient' },
    { name: 'employeeName', class: 2, source: 'Sender' },
  ],
  changedAt: null,
  changedBy: null,
};

const report: NotificationTemplate = {
  key: 'portal.report-created.handler',
  mandatory: false,
  sensitiveText: false,
  mailMayContainSensitive: false,
  customized: true,
  default: { inApp: text('Neue Meldung', 'Es liegt eine neue Meldung vor.'), push: text('Neue Mitteilung', 'Öffnen.'), email: text('Neue Mitteilung', 'Im Portal.') },
  effective: { inApp: text('Meldung!', 'Bitte ansehen.'), push: text('Neue Mitteilung', 'Öffnen.'), email: text('Neue Mitteilung', 'Im Portal.') },
  variables: [
    { name: 'USER.firstName', class: 1, source: 'Recipient' },
    { name: 'USER.lastName', class: 1, source: 'Recipient' },
  ],
  changedAt: '2026-11-05T09:30:00+00:00',
  changedBy: 'hr',
};

const problem = (title: string, status = 422) => new HttpErrorResponse({ status, error: { title } });

describe('TemplatePage', () => {
  let api: Record<'list' | 'save' | 'reset' | 'preview', ReturnType<typeof vi.fn>>;
  let fixture: ComponentFixture<TemplatePage>;
  let root: HTMLElement;

  async function create(): Promise<void> {
    TestBed.configureTestingModule({ providers: [{ provide: TemplateApi, useValue: api }] });
    fixture = TestBed.createComponent(TemplatePage);
    root = fixture.nativeElement as HTMLElement;
    document.body.appendChild(root); // the insert uses the real focus and caret of the fields
    await settle();
  }

  async function settle(): Promise<void> {
    for (let i = 0; i < 4; i++) {
      await fixture.whenStable();
      await new Promise((r) => setTimeout(r));
    }
  }

  const q = <T extends Element = HTMLElement>(selector: string): T | null => root.querySelector<T>(selector);
  const row = (key: string) => q(`[data-key="${key}"]`)!;

  async function click(selector: string): Promise<void> {
    q<HTMLButtonElement>(selector)!.click();
    await settle();
  }

  async function type(selector: string, value: string): Promise<void> {
    const input = q<HTMLInputElement | HTMLTextAreaElement>(selector)!;
    input.value = value;
    input.dispatchEvent(new Event('input', { bubbles: true }));
    await settle();
  }

  async function open(key: string): Promise<void> {
    row(key).querySelector<HTMLButtonElement>('[data-action="edit"]')!.click();
    await settle();
  }

  beforeEach(() => {
    api = {
      list: vi.fn().mockResolvedValue([vacation, report]),
      save: vi.fn().mockResolvedValue(undefined),
      reset: vi.fn().mockResolvedValue(undefined),
      preview: vi.fn().mockResolvedValue({ inApp: text('Vorschau', 'Erika hat Urlaub beantragt.'), push: null, email: null }),
    };
  });

  afterEach(() => root?.remove());

  it('is titled "Benachrichtigungstexte"', async () => {
    await create();

    expect(q('h1')!.textContent).toContain('Benachrichtigungstexte');
  });

  it('lists the notifications by their plain name with their state', async () => {
    await create();

    const names = [...root.querySelectorAll('[data-testid="template-row"] strong')].map((e) => e.textContent!.trim());
    expect(names).toEqual(['Neue Meldung im Portal', 'Neuer Urlaubsantrag zu entscheiden']);
    expect(row(report.key).querySelector('[data-testid="state-chip"]')!.textContent).toContain('Angepasst');
    expect(row(report.key).textContent).toContain('Geändert am');
    expect(row(vacation.key).querySelector('[data-testid="state-chip"]')!.textContent).toContain('Standard');
    expect(row(vacation.key).querySelector('[data-testid="sensitive-chip"]')).not.toBeNull();
  });

  it('says so when the list cannot be loaded', async () => {
    api['list'].mockRejectedValue(new HttpErrorResponse({ status: 503 }));
    await create();

    expect(q('[role="alert"]')!.textContent).toContain('konnten nicht geladen werden');
  });

  it('opens the editor with the texts in force', async () => {
    await create();

    await open(report.key);

    expect(q<HTMLInputElement>('#tpl-inApp-title')!.value).toBe('Meldung!');
    expect(q<HTMLTextAreaElement>('#tpl-inApp-body')!.value).toBe('Bitte ansehen.');
    expect(q<HTMLInputElement>('#tpl-push-title')!.value).toBe('Neue Mitteilung');
  });

  it('shows what is fixed in a sensitive notification and cannot be changed', async () => {
    await create();

    await open(vacation.key);

    expect(q('[data-testid="locked-note"]')).not.toBeNull();
    expect(q<HTMLInputElement>('#tpl-inApp-title')!.readOnly).toBe(false);
    expect(q<HTMLInputElement>('#tpl-push-title')!.readOnly).toBe(true);
    expect(q<HTMLTextAreaElement>('#tpl-push-body')!.readOnly).toBe(true);
    expect(q<HTMLInputElement>('#tpl-email-title')!.readOnly).toBe(true);
    expect(q<HTMLTextAreaElement>('#tpl-email-body')!.readOnly).toBe(true);
  });

  it('saves only when something changed, and sends the texts of the editable channels', async () => {
    await create();
    await open(report.key);
    expect(q<HTMLButtonElement>('[data-action="save"]')!.disabled).toBe(true);

    await type('#tpl-inApp-body', 'Hallo $USER.firstName, bitte ansehen.');
    await click('[data-action="save"]');

    expect(api['save']).toHaveBeenCalledWith(report.key, {
      inApp: text('Meldung!', 'Hallo $USER.firstName, bitte ansehen.'),
      push: text('Neue Mitteilung', 'Öffnen.'),
      email: text('Neue Mitteilung', 'Im Portal.'),
    });
    expect(api['list']).toHaveBeenCalledTimes(2);
    expect(q('[data-testid="status"]')!.textContent).toContain('Gespeichert');
    expect(q('[data-testid="editor"]')).toBeNull();
  });

  it('sends nothing for the fixed push and mail texts of a sensitive notification', async () => {
    await create();
    await open(vacation.key);

    await type('#tpl-inApp-title', 'Neuer Antrag');
    await click('[data-action="save"]');

    expect(api['save']).toHaveBeenCalledWith(vacation.key, { inApp: text('Neuer Antrag', '{employeeName} hat Urlaub beantragt.'), push: null, email: null });
  });

  it('offers only the variables that may stand in the field with the caret', async () => {
    await create();
    await open(vacation.key);
    expect(q('[data-testid="no-variables"]')).not.toBeNull();

    q<HTMLTextAreaElement>('#tpl-inApp-body')!.focus();
    await settle();
    const inApp = [...root.querySelectorAll('[data-action="insert"]')].map((b) => b.getAttribute('data-name'));
    expect(inApp).toEqual(['USER.firstName', 'USER.lastName', 'employeeName']);

    q<HTMLInputElement>('#tpl-push-title')!.focus(); // fixed: nothing offered
    await settle();
    expect(root.querySelectorAll('[data-action="insert"]').length).toBe(0);
  });

  it('shows no sensitive variable for the push text of a template that may be changed', async () => {
    const free: NotificationTemplate = { ...report, variables: [...report.variables, { name: 'DP.store', class: 2, source: 'Sender' }] };
    api['list'].mockResolvedValue([free]);
    await create();
    await open(report.key);

    q<HTMLTextAreaElement>('#tpl-push-body')!.focus();
    await settle();
    expect([...root.querySelectorAll('[data-action="insert"]')].map((b) => b.getAttribute('data-name'))).toEqual(['USER.firstName', 'USER.lastName']);

    q<HTMLTextAreaElement>('#tpl-inApp-body')!.focus();
    await settle();
    expect([...root.querySelectorAll('[data-action="insert"]')].map((b) => b.getAttribute('data-name'))).toContain('DP.store');
  });

  it('adds a variable at the caret of the field', async () => {
    await create();
    await open(report.key);
    const body = q<HTMLTextAreaElement>('#tpl-inApp-body')!;
    body.focus();
    body.value = 'Hallo !';
    body.dispatchEvent(new Event('input', { bubbles: true }));
    body.setSelectionRange(6, 6);
    await settle();

    await click('[data-action="insert"][data-name="USER.firstName"]');

    expect(body.value).toBe('Hallo $USER.firstName!');
    await click('[data-action="save"]');
    expect(api['save']).toHaveBeenCalledWith(report.key, expect.objectContaining({ inApp: text('Meldung!', 'Hallo $USER.firstName!') }));
  });

  it('shows a preview without saving', async () => {
    await create();
    await open(vacation.key);

    await type('#tpl-inApp-body', '$employeeName ist weg');
    await click('[data-action="preview"]');

    expect(api['preview']).toHaveBeenCalledWith(vacation.key, expect.objectContaining({ inApp: text('Urlaubsantrag', '$employeeName ist weg') }));
    expect(q('[data-testid="preview"]')!.textContent).toContain('Erika hat Urlaub beantragt.');
    expect(api['save']).not.toHaveBeenCalled();
  });

  it('drops an old preview when the text is changed again', async () => {
    await create();
    await open(report.key);
    await click('[data-action="preview"]');
    expect(q('[data-testid="preview"]')).not.toBeNull();

    await type('#tpl-inApp-body', 'Anders');

    expect(q('[data-testid="preview"]')).toBeNull();
  });

  it('explains a refused text in plain words and keeps the editor open', async () => {
    api['save'].mockRejectedValue(problem('UnknownVariable'));
    await create();
    await open(report.key);

    await type('#tpl-inApp-body', 'Hallo $nobody');
    await click('[data-action="save"]');

    expect(q('[data-testid="error"]')!.textContent).toContain('Variable');
    expect(q('[data-testid="editor"]')).not.toBeNull();
    expect((q<HTMLTextAreaElement>('#tpl-inApp-body')!).value).toBe('Hallo $nobody');
  });

  it('says when the texts of the notification are fixed', async () => {
    api['save'].mockRejectedValue(problem('SensitiveTextLocked', 409));
    await create();
    await open(vacation.key);

    await type('#tpl-inApp-body', 'x');
    await click('[data-action="save"]');

    expect(q('[data-testid="error"]')!.textContent).toContain('fest vorgegeben');
  });

  it('resets to the default only after a question, and only if the texts were changed', async () => {
    await create();
    await open(vacation.key);
    expect(q('[data-action="ask-reset"]')).toBeNull();
    await click('[data-action="close"]');

    await open(report.key);
    await click('[data-action="ask-reset"]');
    expect(api['reset']).not.toHaveBeenCalled();
    await click('[data-action="reset"]');

    expect(api['reset']).toHaveBeenCalledWith(report.key);
    expect(q('[data-testid="status"]')!.textContent).toContain('Standardtexte');
    expect(q('[data-testid="editor"]')).toBeNull();
  });

  it('can cancel the reset and the editor', async () => {
    await create();
    await open(report.key);

    await click('[data-action="ask-reset"]');
    await click('[data-action="cancel-reset"]');
    expect(q('[data-testid="reset-panel"]')).toBeNull();
    expect(api['reset']).not.toHaveBeenCalled();

    await click('[data-action="close"]');
    expect(q('[data-testid="editor"]')).toBeNull();
  });

  it('edits one notification at a time', async () => {
    await create();

    await open(report.key);
    await open(vacation.key);

    expect(root.querySelectorAll('[data-testid="editor"]').length).toBe(1);
    expect(row(vacation.key).querySelector('[data-testid="editor"]')).not.toBeNull();
  });
});
