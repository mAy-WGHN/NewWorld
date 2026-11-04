import { OverlayContainer } from '@angular/cdk/overlay';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Location } from './report-issue-models';
import { StorePicker } from './store-picker';

const stores: Location[] = [
  { id: 'a', name: 'BaBu Kempten', storeCode: '0011', archived: false },
  { id: 'b', name: 'Samurai Augsburg', storeCode: '0022', archived: false },
  { id: 'c', name: 'HO Head Office', storeCode: '9999', archived: false },
];

describe('StorePicker', () => {
  let fixture: ComponentFixture<StorePicker>;
  let root: HTMLElement;
  let overlay: OverlayContainer;

  async function settle(): Promise<void> {
    for (let i = 0; i < 3; i++) {
      await fixture.whenStable();
      await new Promise((r) => setTimeout(r, 20));
    }
  }

  /** The options stand in the overlay (outside of the component), as Angular Material places them. */
  const options = () => [...overlay.getContainerElement().querySelectorAll('mat-option')].map((o) => o.textContent?.trim());
  const chips = () => [...root.querySelectorAll('[data-testid="chosen"]')].map((c) => c.textContent?.replace(/\s+/g, ' ').replace(/cancel$/, '').trim());
  const input = () => root.querySelector<HTMLInputElement>('input')!;

  async function focus(): Promise<void> {
    input().focus();
    input().dispatchEvent(new Event('focusin', { bubbles: true }));
    input().dispatchEvent(new Event('focus'));
    await settle();
  }

  async function type(value: string): Promise<void> {
    await focus();
    input().value = value;
    input().dispatchEvent(new Event('input', { bubbles: true }));
    await settle();
  }

  async function choose(id: string): Promise<void> {
    overlay.getContainerElement().querySelector<HTMLElement>(`mat-option[data-id="${id}"]`)!.click();
    await settle();
  }

  beforeEach(async () => {
    TestBed.configureTestingModule({});
    overlay = TestBed.inject(OverlayContainer);
    fixture = TestBed.createComponent(StorePicker);
    fixture.componentRef.setInput('locations', stores);
    root = fixture.nativeElement as HTMLElement;
    await settle();
  });

  afterEach(() => overlay.ngOnDestroy());

  it('asks for the store with the same wording as the existing system', () => {
    expect(root.textContent).toContain('Filiale');
    expect(input().placeholder).toBe('Filiale suchen…');
  });

  it('shows the stores as "Name (Nummer)" once the field is used', async () => {
    expect(options()).toEqual([]);

    await focus();

    expect(options()).toEqual(['BaBu Kempten (0011)', 'Samurai Augsburg (0022)', 'HO Head Office (9999)']);
  });

  it('narrows the list by name or number', async () => {
    await type('augs');
    expect(options()).toEqual(['Samurai Augsburg (0022)']);

    await type('9999');
    expect(options()).toEqual(['HO Head Office (9999)']);
  });

  it('says so when nothing fits', async () => {
    await type('xyz');

    expect(options()).toEqual(['Keine Filiale gefunden.']);
  });

  it('adds a store with a click, shows it as a chip and clears the search', async () => {
    await type('kemp');

    await choose('a');

    expect(fixture.componentInstance.selected().map((s) => s.id)).toEqual(['a']);
    expect(chips()).toEqual(['BaBu Kempten (0011)']);
    expect(input().value).toBe('');
  });

  it('allows several stores and does not offer a chosen one again', async () => {
    await focus();
    await choose('a');
    await focus();
    await choose('c');
    await focus();

    expect(fixture.componentInstance.selected().map((s) => s.id)).toEqual(['a', 'c']);
    expect(options()).toEqual(['Samurai Augsburg (0022)']);
  });

  it('removes a store from the chips', async () => {
    fixture.componentInstance.selected.set([stores[0], stores[1]]);
    await settle();

    root.querySelector<HTMLButtonElement>('[data-id="a"][data-action="remove"]')!.click();
    await settle();

    expect(fixture.componentInstance.selected().map((s) => s.id)).toEqual(['b']);
  });

  it('shows the message of what is missing', async () => {
    fixture.componentRef.setInput('error', 'Bitte mindestens eine Filiale auswählen.');
    await settle();

    expect(root.querySelector('[role="alert"]')!.textContent).toContain('Bitte mindestens eine Filiale auswählen.');
  });
});
