import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { AuthService } from '../../core/auth/auth.service';
import { PrivilegeSet } from '../../core/auth/privileges';
import { FavoritesService } from '../../core/favorites.service';
import { AreasPage } from './areas';
import { FavoritesPage } from './favorites';

const grant = (...keys: string[]) => new PrivilegeSet(keys.map((key) => ({ key, unrestricted: true, storeIds: [], employeeIds: [] })));

describe('areas and favorites', () => {
  const privileges = signal(grant('absence:read-own', 'workingtime:read-own'));

  beforeEach(() => {
    localStorage.clear();
    privileges.set(grant('absence:read-own', 'workingtime:read-own'));
    TestBed.configureTestingModule({ providers: [provideRouter([]), { provide: AuthService, useValue: { privileges, roles: signal(['Public User']) } }] });
  });

  async function render<T>(component: new () => T): Promise<HTMLElement> {
    const fixture: ComponentFixture<T> = TestBed.createComponent(component);
    await fixture.whenStable();
    return fixture.nativeElement as HTMLElement;
  }

  it('lists the areas of the person grouped, and no others', async () => {
    const root = await render(AreasPage);

    expect(root.textContent).toContain('Meine Abwesenheiten');
    expect(root.textContent).toContain('Arbeitszeitkonto');
    expect(root.textContent).toContain('FAQ');
    expect(root.textContent).not.toContain('Genehmigungen');
    expect(root.textContent).not.toContain('Rollen und Rechte');
  });

  it('pins and unpins an area with the star', async () => {
    const root = await render(AreasPage);
    const favorites = TestBed.inject(FavoritesService);

    root.querySelector<HTMLButtonElement>('[data-action="favorite"]')!.click();
    expect(favorites.paths()).toHaveLength(1);

    root.querySelector<HTMLButtonElement>('[data-action="favorite"]')!.click();
    expect(favorites.paths()).toHaveLength(0);
  });

  it('shows the own privileges grouped by area', async () => {
    const root = await render(AreasPage);

    expect(root.textContent).toContain('absence:read-own');
    expect(root.textContent).toContain('2 Rechte');
  });

  it('explains how to pin an area when there are no favorites', async () => {
    const root = await render(FavoritesPage);

    expect(root.textContent).toContain('Noch keine Favoriten');
  });

  it('lists the pinned areas the person may still open', async () => {
    const favorites = TestBed.inject(FavoritesService);
    favorites.toggle('absences');
    favorites.toggle('admin/access'); // no longer allowed: not shown
    const root = await render(FavoritesPage);

    expect(root.textContent).toContain('Meine Abwesenheiten');
    expect(root.textContent).not.toContain('Rollen und Rechte');
  });
});
