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

  async function settle(): Promise<void> {
    await fixture.whenStable();
    await new Promise((r) => setTimeout(r));
    await fixture.whenStable();
  }

  const options = () => [...root.querySelectorAll('[data-testid="location-option"]')].map((o) => o.textContent?.trim());
  const chips = () => [...root.querySelectorAll('[data-testid="chosen"]')].map((o) => o.textContent?.trim().replace(/\s*×$/, '').replace(/\s+/g, ' '));
  const input = () => root.querySelector<HTMLInputElement>('input')!;

  async function focus(): Promise<void> {
    input().dispatchEvent(new Event('focus'));
    await settle();
  }

  async function type(value: string): Promise<void> {
    input().value = value;
    input().dispatchEvent(new Event('input', { bubbles: true }));
    await settle();
  }

  async function key(name: string): Promise<void> {
    input().dispatchEvent(new KeyboardEvent('keydown', { key: name, cancelable: true, bubbles: true }));
    await settle();
  }

  beforeEach(async () => {
    TestBed.configureTestingModule({});
    fixture = TestBed.createComponent(StorePicker);
    fixture.componentRef.setInput('locations', stores);
    root = fixture.nativeElement as HTMLElement;
    await settle();
  });

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
    await focus();

    await type('augs');
    expect(options()).toEqual(['Samurai Augsburg (0022)']);

    await type('9999');
    expect(options()).toEqual(['HO Head Office (9999)']);
  });

  it('says so when nothing fits', async () => {
    await focus();
    await type('xyz');

    expect(options()).toEqual([]);
    expect(root.textContent).toContain('Keine Filiale gefunden');
  });

  it('adds a store with a click, shows it as a chip and clears the search', async () => {
    await focus();
    await type('kemp');

    root.querySelector<HTMLButtonElement>('[data-testid="location-option"]')!.click();
    await settle();

    expect(fixture.componentInstance.selected().map((s) => s.id)).toEqual(['a']);
    expect(chips()).toEqual(['BaBu Kempten (0011)']);
    expect(input().value).toBe('');
  });

  it('allows several stores and does not offer a chosen one again', async () => {
    await focus();
    root.querySelector<HTMLButtonElement>('[data-id="a"]')!.click();
    await settle();
    await focus();
    root.querySelector<HTMLButtonElement>('[data-id="c"]')!.click();
    await settle();
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

  it('takes the first match with Enter, moves with the arrow keys and closes with Escape', async () => {
    await focus();
    await key('ArrowDown');
    await key('Enter');
    expect(fixture.componentInstance.selected().map((s) => s.id)).toEqual(['b']);

    await focus();
    expect(options().length).toBeGreaterThan(0);
    await key('Escape');
    expect(options()).toEqual([]);
  });

  it('shows the message of what is missing', async () => {
    fixture.componentRef.setInput('error', 'Bitte mindestens eine Filiale auswählen.');
    await settle();

    expect(root.querySelector('[role="alert"]')!.textContent).toContain('Bitte mindestens eine Filiale auswählen.');
  });
});
