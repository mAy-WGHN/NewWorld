import { OverlayContainer } from '@angular/cdk/overlay';
import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { AuthService } from '../core/auth/auth.service';
import { PrivilegeSet } from '../core/auth/privileges';
import { SearchBox } from './search-box';

const grant = (...keys: string[]) => new PrivilegeSet(keys.map((key) => ({ key, unrestricted: true, storeIds: [], employeeIds: [] })));

describe('SearchBox', () => {
  let fixture: ComponentFixture<SearchBox>;
  let root: HTMLElement;
  let overlay: OverlayContainer;
  let navigate: ReturnType<typeof vi.spyOn>;
  const privileges = signal(grant('absence:read-own', 'workingtime:read-own'));

  async function settle(): Promise<void> {
    for (let i = 0; i < 3; i++) {
      await fixture.whenStable();
      await new Promise((r) => setTimeout(r, 20));
    }
  }

  const input = () => root.querySelector<HTMLInputElement>('input')!;
  /** The options stand in the overlay (outside of the component), as Angular Material places them. */
  const options = () => [...overlay.getContainerElement().querySelectorAll('mat-option')];
  const optionNames = () => options().map((o) => o.querySelector('.font-medium')?.textContent?.trim() ?? o.textContent?.trim());

  async function type(value: string): Promise<void> {
    input().focus();
    input().value = value;
    input().dispatchEvent(new Event('input', { bubbles: true }));
    await settle();
  }

  beforeEach(async () => {
    privileges.set(grant('absence:read-own', 'workingtime:read-own'));
    TestBed.configureTestingModule({ providers: [provideRouter([]), { provide: AuthService, useValue: { privileges } }] });
    overlay = TestBed.inject(OverlayContainer);
    navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
    fixture = TestBed.createComponent(SearchBox);
    root = fixture.nativeElement as HTMLElement;
    await settle();
  });

  afterEach(() => overlay.ngOnDestroy());

  it('asks "Was möchtest du erledigen?" and gives examples from the areas the person has', () => {
    expect(input().placeholder).toBe('Was möchtest du erledigen?');
    expect(root.textContent).toContain('z. B.');
    expect(root.textContent).toContain('Meine Krankmeldungen');
  });

  it('finds an area by what people call it', async () => {
    await type('krank');

    expect(optionNames()).toEqual(['Meine Krankmeldungen', 'Meine Abwesenheiten']);
  });

  it('finds nothing in areas the person may not open', async () => {
    await type('genehmig');

    expect(optionNames().filter((n) => n === 'Genehmigungen')).toEqual([]);
    expect(overlay.getContainerElement().textContent).toContain('nichts gefunden');
  });

  it('opens the area of a chosen result and empties the field', async () => {
    await type('stunden');

    (options()[0] as HTMLElement).click();
    await settle();

    expect(navigate).toHaveBeenCalledWith('/working-time');
    expect(input().value).toBe('');
  });

  it('does not offer anything for an empty field', async () => {
    await type('');

    expect(options()).toEqual([]);
  });
});
