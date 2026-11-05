import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, provideRouter } from '@angular/router';
import { StepUpError, StepUpService } from '../../core/auth/step-up.service';
import { StepUpDonePage } from './step-up-done';

describe('StepUpDonePage', () => {
  let fixture: ComponentFixture<StepUpDonePage>;
  let root: HTMLElement;
  let stepUp: { complete: ReturnType<typeof vi.fn>; begin: ReturnType<typeof vi.fn>; pendingReturn: ReturnType<typeof vi.fn> };
  let navigate: ReturnType<typeof vi.spyOn>;

  async function create(result: string | null): Promise<void> {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: StepUpService, useValue: stepUp },
        { provide: ActivatedRoute, useValue: { snapshot: { queryParamMap: { get: () => result } } } },
      ],
    });
    navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
    fixture = TestBed.createComponent(StepUpDonePage);
    root = fixture.nativeElement as HTMLElement;
    await fixture.whenStable();
    await new Promise((r) => setTimeout(r));
    await fixture.whenStable();
  }

  beforeEach(() => {
    stepUp = { complete: vi.fn().mockResolvedValue('/payslip'), begin: vi.fn().mockResolvedValue(undefined), pendingReturn: vi.fn().mockReturnValue('/payslip') };
  });

  it('continues where the person was after a successful confirmation', async () => {
    await create('ok');

    expect(stepUp.complete).toHaveBeenCalled();
    expect(navigate).toHaveBeenCalledWith('/payslip', { replaceUrl: true });
  });

  it('says so if Microsoft did not confirm, without asking the service', async () => {
    await create('failed');

    expect(root.querySelector('[data-testid="step-up-error"]')!.textContent).toContain('nicht geklappt');
    expect(stepUp.complete).not.toHaveBeenCalled();
  });

  it('says so if no Microsoft account is linked', async () => {
    stepUp.complete.mockRejectedValue(new StepUpError('no-link'));
    await create('ok');

    expect(root.querySelector('[data-testid="step-up-error"]')!.textContent).toContain('Personalabteilung');
    expect(navigate).not.toHaveBeenCalled();
  });

  it('can try again, with the same way back', async () => {
    await create('failed');

    root.querySelector<HTMLButtonElement>('[data-action="retry"]')!.click();
    await fixture.whenStable();

    expect(stepUp.begin).toHaveBeenCalledWith('/payslip');
  });
});
