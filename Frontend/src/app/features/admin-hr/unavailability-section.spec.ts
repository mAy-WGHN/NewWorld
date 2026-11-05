import { HttpErrorResponse } from '@angular/common/http';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { UnavailabilityKind } from './admin-hr-models';
import { AdminHrApi } from './admin-hr.api';
import { UnavailabilitySection } from './unavailability-section';

const kind = (id: string, code: string, description: string, over: Partial<UnavailabilityKind> = {}): UnavailabilityKind => ({
  id, code, description, paidTime: true, scheduleSymbol: null, workCode: null, isActive: true, isSystem: false, ...over,
});

type Methods = 'kinds' | 'addKind' | 'updateKind';

describe('UnavailabilitySection', () => {
  let api: Record<Methods, ReturnType<typeof vi.fn>>;
  let fixture: ComponentFixture<UnavailabilitySection>;
  let root: HTMLElement;

  async function settle(): Promise<void> {
    for (let i = 0; i < 4; i++) {
      await fixture.whenStable();
      await new Promise((r) => setTimeout(r));
    }
  }

  async function create(): Promise<void> {
    TestBed.configureTestingModule({ providers: [{ provide: AdminHrApi, useValue: api }] });
    fixture = TestBed.createComponent(UnavailabilitySection);
    root = fixture.nativeElement as HTMLElement;
    await settle();
  }

  const q = <T extends Element = HTMLElement>(selector: string): T | null => root.querySelector<T>(selector);
  const rowOf = (id: string) => root.querySelector<HTMLElement>(`[data-testid="kind-row"][data-id="${id}"]`)!;

  async function type(selector: string, value: string, scope: ParentNode = root): Promise<void> {
    const el = scope.querySelector<HTMLInputElement>(selector)!;
    el.value = value;
    el.dispatchEvent(new Event('input', { bubbles: true }));
    await settle();
  }

  async function click(selector: string, scope: ParentNode = root): Promise<void> {
    scope.querySelector<HTMLElement>(selector)!.click();
    await settle();
  }

  beforeEach(() => {
    api = {
      kinds: vi.fn().mockResolvedValue([
        kind('k1', 'KRANK', 'Krank', { isSystem: true }),
        kind('k2', 'SU', 'Sonderurlaub', { scheduleSymbol: 'SU' }),
        kind('k3', 'ALT', 'Alte Art', { isActive: false, paidTime: false }),
      ]),
      addKind: vi.fn().mockImplementation(async (form) => kind('k4', form.code, form.description, { paidTime: form.paidTime })),
      updateKind: vi.fn().mockResolvedValue(undefined),
    };
  });

  it('lists all kinds with their state and marks the system kinds', async () => {
    await create();

    expect(root.querySelectorAll('[data-testid="kind-row"]')).toHaveLength(3);
    expect(rowOf('k1').querySelector('[data-testid="system-chip"]')).not.toBeNull();
    expect(rowOf('k2').querySelector('[data-testid="system-chip"]')).toBeNull();
    expect(rowOf('k3').querySelector('[data-testid="kind-state"]')!.textContent).toContain('Deaktiviert');
    expect(rowOf('k3').textContent).toContain('unbezahlt');
    expect(rowOf('k2').textContent).toContain('Zeichen SU');
  });

  it('offers no change for a system kind', async () => {
    await create();

    expect(rowOf('k1').querySelector('[data-action="edit"]')).toBeNull();
    expect(rowOf('k1').querySelector('[data-action="deactivate"]')).toBeNull();
    expect(rowOf('k2').querySelector('[data-action="edit"]')).not.toBeNull();
  });

  it('adds a kind; optional fields left empty are sent as null', async () => {
    await create();
    await type('[data-testid="new-code"]', 'SCHUL');
    await type('[data-testid="new-description"]', 'Schulung');
    await click('[data-testid="new-paid"]');
    await click('[data-action="add-kind"]');

    expect(api['addKind']).toHaveBeenCalledWith({ code: 'SCHUL', description: 'Schulung', paidTime: true, scheduleSymbol: '', workCode: '' });
    expect(rowOf('k4').textContent).toContain('Schulung');
    expect((q('[data-testid="new-code"]') as HTMLInputElement).value).toBe('');
  });

  it('does not add a kind without code or description', async () => {
    await create();
    await type('[data-testid="new-description"]', 'Ohne Code');
    await click('[data-action="add-kind"]');
    expect(api['addKind']).not.toHaveBeenCalled();
    expect(q('[data-testid="error"]')!.textContent).toContain('Code');

    await type('[data-testid="new-code"]', 'X');
    await type('[data-testid="new-description"]', '  ');
    await click('[data-action="add-kind"]');
    expect(api['addKind']).not.toHaveBeenCalled();
    expect(q('[data-testid="error"]')!.textContent).toContain('Beschreibung');
  });

  it('tells that a code is already taken', async () => {
    api['addKind'].mockRejectedValue(new HttpErrorResponse({ status: 409, error: { title: 'DuplicateCode' } }));
    await create();
    await type('[data-testid="new-code"]', 'SU');
    await type('[data-testid="new-description"]', 'Doppelt');
    await click('[data-action="add-kind"]');

    expect(q('[data-testid="error"]')!.textContent).toContain('schon vergeben');
  });

  it('changes description, symbol and paid time but never the code', async () => {
    await create();
    await click('[data-action="edit"]', rowOf('k2'));
    await type('[data-testid="edit-description"]', 'Sonderurlaub (bezahlt)', rowOf('k2'));
    await type('[data-testid="edit-symbol"]', 'S', rowOf('k2'));
    await click('[data-action="save-kind"]', rowOf('k2'));

    expect(api['updateKind']).toHaveBeenCalledWith('k2', expect.objectContaining({ code: 'SU', description: 'Sonderurlaub (bezahlt)', scheduleSymbol: 'S', paidTime: true }));
    expect(api['updateKind'].mock.calls[0]).toHaveLength(2); // the state is not part of a rename
    expect(rowOf('k2').textContent).toContain('Sonderurlaub (bezahlt)');
  });

  it('deactivates and reactivates a kind', async () => {
    await create();
    await click('[data-action="deactivate"]', rowOf('k2'));
    expect(api['updateKind']).toHaveBeenCalledWith('k2', expect.anything(), false);
    expect(rowOf('k2').querySelector('[data-testid="kind-state"]')!.textContent).toContain('Deaktiviert');

    await click('[data-action="activate"]', rowOf('k3'));
    expect(api['updateKind']).toHaveBeenCalledWith('k3', expect.anything(), true);
  });

  it('explains a refused change of a system kind', async () => {
    api['updateKind'].mockRejectedValue(new HttpErrorResponse({ status: 409, error: { title: 'SystemKind' } }));
    await create();
    await click('[data-action="deactivate"]', rowOf('k2'));

    expect(q('[data-testid="error"]')!.textContent).toContain('vom System');
  });

  it('says so when the kinds cannot be loaded', async () => {
    api['kinds'].mockRejectedValue(new HttpErrorResponse({ status: 503 }));
    await create();

    expect(q('[data-testid="load-error"]')!.textContent).toContain('Laden hat nicht geklappt');
    expect(root.querySelectorAll('[data-testid="kind-row"]')).toHaveLength(0);
  });
});
