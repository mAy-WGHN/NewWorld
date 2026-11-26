import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { PersonSource, WghnPerson, WghnPersonPicker, isGuid } from './wghn-person-picker';

const erika: WghnPerson = { employeeId: 'e1', name: 'Erika Mustermann' }; // a source that knows no personnel numbers

@Component({
  selector: 'wghn-picker-host',
  imports: [WghnPersonPicker],
  template: `<wghn-person-picker label="Wer?" inputId="who" [selectedId]="selectedId" [debounceMs]="0" [source]="source" (picked)="picked.push($event)" />`,
})
class Host {
  selectedId: string | null = null;
  readonly picked: (WghnPerson | null)[] = [];
  readonly searches: string[] = [];
  readonly source: PersonSource = {
    search: async (query) => {
      this.searches.push(query);
      return [erika];
    },
    byIds: async (ids) => (ids.includes('e1') ? [erika] : []),
  };
}

describe('wghn-person-picker with a source of its own', () => {
  async function settle(fixture: { whenStable(): Promise<unknown> }): Promise<void> {
    for (let i = 0; i < 4; i++) {
      await fixture.whenStable();
      await new Promise((r) => setTimeout(r));
    }
  }

  it('searches the source from two letters and shows a person without a personnel number as just a name', async () => {
    const fixture = TestBed.createComponent(Host);
    const root = fixture.nativeElement as HTMLElement;
    await settle(fixture);

    const input = root.querySelector<HTMLInputElement>('[data-testid="person-search"]')!;
    input.value = 'e';
    input.dispatchEvent(new Event('input', { bubbles: true }));
    await settle(fixture);
    expect(fixture.componentInstance.searches).toEqual([]); // one letter is too little

    input.value = 'erik';
    input.dispatchEvent(new Event('input', { bubbles: true }));
    await settle(fixture);

    expect(fixture.componentInstance.searches).toEqual(['erik']);
    const result = root.querySelector<HTMLButtonElement>('[data-testid="person-result"]')!;
    expect(result.textContent).toContain('Erika Mustermann');
    expect(result.textContent).not.toContain('Personalnummer');
    result.click();
    await settle(fixture);
    expect(fixture.componentInstance.picked).toEqual([erika]);
    expect(root.querySelector('[data-testid="person-selected"]')!.textContent).toContain('Erika Mustermann');
  });

  it('looks the name up for an id the page already has', async () => {
    TestBed.resetTestingModule();
    const fixture = TestBed.createComponent(Host);
    fixture.componentInstance.selectedId = 'e1';
    await settle(fixture);

    expect((fixture.nativeElement as HTMLElement).querySelector('[data-testid="person-selected"]')!.textContent).toContain('Erika Mustermann');
  });

  it('knows an id when it sees one', () => {
    expect(isGuid('0f8fad5b-d9cb-469f-a165-70867728950e')).toBe(true);
    expect(isGuid('erik')).toBe(false);
  });
});
