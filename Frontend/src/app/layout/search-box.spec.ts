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
  let navigate: ReturnType<typeof vi.spyOn>;
  const privileges = signal(grant('absence:read-own', 'workingtime:read-own'));

  async function settle(): Promise<void> {
    await fixture.whenStable();
    await new Promise((r) => setTimeout(r));
    await fixture.whenStable();
  }

  async function type(value: string): Promise<void> {
    const input = root.querySelector<HTMLInputElement>('input')!;
    input.value = value;
    input.dispatchEvent(new Event('input', { bubbles: true }));
    await settle();
  }

  async function key(name: string): Promise<void> {
    root.querySelector('input')!.dispatchEvent(new KeyboardEvent('keydown', { key: name, cancelable: true, bubbles: true }));
    await settle();
  }

  const results = () => [...root.querySelectorAll('[role="option"]')].map((o) => o.querySelector('strong')?.textContent ?? o.textContent?.trim());

  beforeEach(async () => {
    privileges.set(grant('absence:read-own', 'workingtime:read-own'));
    TestBed.configureTestingModule({ providers: [provideRouter([]), { provide: AuthService, useValue: { privileges } }] });
    navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
    fixture = TestBed.createComponent(SearchBox);
    root = fixture.nativeElement as HTMLElement;
    await settle();
  });

  it('shows examples taken from the areas the person has', () => {
    expect(root.textContent).toContain('z. B.');
    expect(root.textContent).toContain('Meine Abwesenheiten');
  });

  it('finds an area by what people call it', async () => {
    await type('krank');

    // the sick note page first; the absences page only follows because its description mentions sick notes
    expect(results()).toEqual(['Meine Krankmeldungen', 'Meine Abwesenheiten']);
  });

  it('finds nothing in areas the person may not open', async () => {
    await type('genehmig');

    expect(results()).toEqual([]);
    expect(root.textContent).toContain('nichts gefunden');
  });

  it('opens the best match with Enter and clears the search', async () => {
    await type('stunden');
    await key('Enter');

    expect(navigate).toHaveBeenCalledWith('/working-time');
    expect(root.querySelector('input')!.value).toBe('');
  });

  it('moves through the results with the arrow keys, wrapping around', async () => {
    privileges.set(grant('absence:read-own', 'absence:create-own', 'workingtime:read-own'));
    await type('u');
    const first = results();
    expect(first.length).toBeGreaterThan(1);

    await key('ArrowDown');
    expect(root.querySelector('[role="option"][aria-selected="true"]')!.textContent).toContain(first[1]!);

    await key('ArrowUp');
    await key('ArrowUp');
    expect(root.querySelector('[role="option"][aria-selected="true"]')!.textContent).toContain(first[first.length - 1]!);
  });

  it('closes the list with Escape', async () => {
    await type('krank');
    expect(results()).not.toEqual([]);

    await key('Escape');

    expect(results()).toEqual([]);
  });

  it('does nothing on Enter without a result', async () => {
    await type('xyz');
    await key('Enter');

    expect(navigate).not.toHaveBeenCalled();
  });
});
