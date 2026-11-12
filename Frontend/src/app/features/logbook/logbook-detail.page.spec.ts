import { HttpErrorResponse } from '@angular/common/http';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { LogbookDetailPage } from './logbook-detail.page';
import { LogbookApi } from './logbook.api';
import { LogbookComment, LogbookTemplate, ReportDetail, ViewerLevel } from './logbook-models';

const template: LogbookTemplate = {
  version: 1,
  publishedAt: null,
  sections: [
    { id: 's1', title: 'Stimmung', hint: null, kind: 'singleChoice', required: true, options: [{ id: 'o1', label: 'Gut' }, { id: 'o2', label: 'Schlecht' }] },
    { id: 's2', title: 'Was war los?', hint: null, kind: 'multiChoice', required: false, options: [{ id: 'o3', label: 'Krankheit' }, { id: 'o4', label: 'Lieferung' }] },
    { id: 's3', title: 'Sonstiges', hint: null, kind: 'freeText', required: false, options: [] },
  ],
};

const comment = (patch: Partial<LogbookComment>): LogbookComment => ({
  id: 'c',
  kind: 'reply',
  level: 'regional',
  authorId: 'p1',
  authorName: 'Ingo IT',
  text: 'Danke',
  createdAt: new Date(2026, 9, 24, 9, 30).toISOString(),
  own: false,
  ...patch,
});

const detail = (viewerLevel: ViewerLevel, patch: Partial<ReportDetail> = {}): ReportDetail => ({
  id: 'r1',
  storeId: 'st1',
  storeNumber: '001',
  storeName: 'Augsburg',
  week: '2026-10-19',
  templateVersion: 1,
  phase: 'submitted',
  state: 'new',
  answers: { s1: { optionIds: ['o2'], text: null }, s2: { optionIds: ['o3', 'o4'], text: null }, s3: { optionIds: [], text: 'Kasse hakt' } },
  submittedAt: new Date(2026, 9, 23, 14, 0).toISOString(),
  submittedByName: 'Erika Filiale',
  revisedAt: null,
  revisedByName: null,
  followedUpAt: null,
  followedUpByName: null,
  followUpNote: null,
  escalatedAt: null,
  escalatedByName: null,
  escalationNote: null,
  revisedSinceMyReply: false,
  viewerLevel,
  comments: [],
  ...patch,
});

describe('LogbookDetailPage', () => {
  let api: Record<'detail' | 'template' | 'comment' | 'setState' | 'escalate', ReturnType<typeof vi.fn>>;
  let fixture: ComponentFixture<LogbookDetailPage>;
  let root: HTMLElement;

  async function settle(): Promise<void> {
    for (let i = 0; i < 4; i++) {
      await fixture.whenStable();
      await new Promise((r) => setTimeout(r));
    }
  }

  async function create(report: ReportDetail): Promise<void> {
    api.detail.mockResolvedValue(report);
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: LogbookApi, useValue: api },
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap({ id: 'r1' }) } } },
      ],
    });
    fixture = TestBed.createComponent(LogbookDetailPage);
    root = fixture.nativeElement as HTMLElement;
    await settle();
  }

  const q = <T extends Element = HTMLElement>(selector: string): T | null => root.querySelector<T>(selector);
  const button = (action: string): HTMLButtonElement | null => q<HTMLButtonElement>(`[data-action="${action}"]`);

  async function click(action: string): Promise<void> {
    button(action)!.click();
    await settle();
  }

  async function type(selector: string, value: string): Promise<void> {
    const field = q<HTMLTextAreaElement>(selector)!;
    field.value = value;
    field.dispatchEvent(new Event('input'));
    await settle();
  }

  beforeEach(() => {
    api = {
      detail: vi.fn(),
      template: vi.fn().mockResolvedValue(template),
      comment: vi.fn().mockResolvedValue(comment({})),
      setState: vi.fn().mockResolvedValue({}),
      escalate: vi.fn().mockResolvedValue({}),
    };
  });

  it('shows the answers in the order of the template with the label of every chosen answer', async () => {
    await create(detail('regional'));

    expect(q('h1')!.textContent).toContain('001 Augsburg');
    expect(api.template).toHaveBeenCalledWith(1);
    const answers = [...root.querySelectorAll('[data-testid="answer"]')].map((e) => [...e.querySelectorAll('h2, li, p')].map((n) => n.textContent!.trim()));
    expect(answers).toEqual([['Stimmung', 'Schlecht'], ['Was war los?', 'Krankheit', 'Lieferung'], ['Sonstiges', 'Kasse hakt']]);
    expect(q('[data-testid="submitted-info"]')!.textContent).toContain('Erika Filiale');
    expect(q('[data-testid="state"]')!.textContent).toContain('Neu');
  });

  it('shows the store manager only the replies, with no internal comments and none of the leaders\' buttons', async () => {
    await create(detail('none', { comments: [comment({ id: 'c1', text: 'Bitte die Kasse prüfen' })] }));

    expect(root.querySelectorAll('[data-testid="reply"]').length).toBe(1);
    expect(q('[data-testid="reply"]')!.textContent).toContain('Bitte die Kasse prüfen');
    expect(q('[data-testid="reply"]')!.textContent).toContain('Ingo IT · Regionalleitung');
    expect(q('[data-testid="internals"]')).toBeNull();
    expect(q('[data-testid="actions"]')).toBeNull();
    expect(button('reply')).toBeNull();
    expect(q('aside')!.textContent).toContain('Berichtsheft');
  });

  it('shows a regional leader the replies and the own internal comments next to each other, and the state buttons', async () => {
    await create(detail('regional', { comments: [comment({ id: 'c1' }), comment({ id: 'c2', kind: 'internal', text: 'Nur für uns', own: true })] }));

    expect(root.querySelectorAll('[data-testid="reply"]').length).toBe(1);
    expect(root.querySelectorAll('[data-testid="internal"]').length).toBe(1);
    expect(q('[data-testid="internal"]')!.textContent).toContain('Nur für uns');
    expect(button('done')).not.toBeNull();
    expect(button('in-progress')).not.toBeNull();
    expect(button('escalate-open')).not.toBeNull();
  });

  it('does not let a district leader escalate and names the level of the author of each reply', async () => {
    await create(detail('district', { comments: [comment({ id: 'c1', level: 'district', authorName: 'Rudi Revisor' })] }));

    expect(button('escalate-open')).toBeNull();
    expect(q('[data-testid="reply"]')!.textContent).toContain('Rudi Revisor · Bezirksleitung');
    expect(root.textContent).toContain('Antwort an die Regionalleitung');
  });

  it('sends a reply to the store manager, empties the field and loads the report again', async () => {
    await create(detail('regional'));
    expect(button('reply')!.disabled).toBe(true);

    await type('#reply-text', '  Danke, bitte nachbereiten  ');
    expect(button('reply')!.disabled).toBe(false);
    api.detail.mockResolvedValue(detail('regional', { state: 'inProgress', comments: [comment({ text: 'Danke, bitte nachbereiten' })] }));
    await click('reply');

    expect(api.comment).toHaveBeenCalledWith('r1', 'reply', 'Danke, bitte nachbereiten');
    expect(q('[data-testid="act-status"]')!.textContent).toContain('Antwort ist gesendet');
    expect(q<HTMLTextAreaElement>('#reply-text')!.value).toBe('');
    expect(q('[data-testid="state"]')!.textContent).toContain('In Bearbeitung');
    expect(root.querySelectorAll('[data-testid="reply"]').length).toBe(1);
    expect(api.template).toHaveBeenCalledTimes(1); // the template of the same version is not fetched again
  });

  it('saves an internal comment as internal', async () => {
    await create(detail('district'));

    await type('#internal-text', 'Mit der Revision besprechen');
    await click('internal');

    expect(api.comment).toHaveBeenCalledWith('r1', 'internal', 'Mit der Revision besprechen');
  });

  it('keeps the text and shows the error if the reply is refused', async () => {
    api.comment.mockRejectedValue(new HttpErrorResponse({ status: 409, error: { success: false, message: 'Der Bericht ist abgeschlossen.', errors: [] } }));
    await create(detail('regional'));
    await type('#reply-text', 'Hallo');

    await click('reply');

    expect(q('[data-testid="act-error"]')!.textContent).toContain('abgeschlossen');
    expect(q<HTMLTextAreaElement>('#reply-text')!.value).toBe('Hallo');
  });

  it('sets the state done and, for a done report, offers to open it again', async () => {
    await create(detail('regional'));
    api.detail.mockResolvedValue(detail('regional', { state: 'done' }));
    await click('done');
    expect(api.setState).toHaveBeenCalledWith('r1', 'done');

    expect(q('[data-testid="state"]')!.textContent).toContain('Erledigt');
    expect(button('done')).toBeNull();
    api.detail.mockResolvedValue(detail('regional', { state: 'inProgress' }));
    await click('reopen');
    expect(api.setState).toHaveBeenLastCalledWith('r1', 'inProgress');
    expect(button('reopen')).toBeNull();
  });

  it('lets a regional leader escalate with a note and then shows the escalation', async () => {
    await create(detail('regional'));
    expect(q('#escalate-note')).toBeNull(); // the form is closed until asked for

    await click('escalate-open');
    await type('#escalate-note', '  Brauche Hilfe bei der Kasse ');
    api.detail.mockResolvedValue(detail('regional', { escalatedAt: new Date(2026, 9, 24, 10, 0).toISOString(), escalatedByName: 'Ingo IT', escalationNote: 'Brauche Hilfe bei der Kasse' }));
    await click('escalate');

    expect(api.escalate).toHaveBeenCalledWith('r1', 'Brauche Hilfe bei der Kasse');
    expect(q('[data-testid="escalated"]')).not.toBeNull();
    expect(q('[data-testid="escalation"]')!.textContent).toContain('Brauche Hilfe bei der Kasse');
    expect(button('escalate-open')).toBeNull(); // once per report
  });

  it('escalates without a note if none is written', async () => {
    await create(detail('regional'));
    await click('escalate-open');
    await click('escalate');

    expect(api.escalate).toHaveBeenCalledWith('r1', null);
  });

  it('marks a report the store manager changed since the leader replied, for leaders only', async () => {
    await create(detail('regional', { revisedSinceMyReply: true }));
    expect(q('[data-testid="revised"]')).not.toBeNull();
  });

  it('shows the follow-up of the store manager with the note', async () => {
    await create(detail('regional', { state: 'followedUp', followedUpAt: new Date(2026, 9, 25, 8, 0).toISOString(), followedUpByName: 'Erika Filiale', followUpNote: 'Kasse neu eingewiesen' }));

    expect(q('[data-testid="followed-up"]')!.textContent).toContain('Kasse neu eingewiesen');
    expect(q('[data-testid="followed-up"]')!.textContent).toContain('Erika Filiale');
  });

  it('says plainly that the report was not found if the service does not show it', async () => {
    api.detail.mockRejectedValue(new HttpErrorResponse({ status: 404, error: { success: false, message: 'Nicht gefunden.', errors: [] } }));
    TestBed.configureTestingModule({
      providers: [provideRouter([]), { provide: LogbookApi, useValue: api }, { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap({ id: 'r1' }) } } }],
    });
    fixture = TestBed.createComponent(LogbookDetailPage);
    root = fixture.nativeElement as HTMLElement;
    await settle();

    expect(q('[data-testid="load-error"]')!.textContent).toContain('Nicht gefunden');
    expect(q('[data-testid="answer"]')).toBeNull();
  });
});
