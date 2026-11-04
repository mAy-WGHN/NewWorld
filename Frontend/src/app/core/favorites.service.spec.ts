import { TestBed } from '@angular/core/testing';
import { FavoritesService } from './favorites.service';

describe('FavoritesService', () => {
  beforeEach(() => localStorage.clear());

  const create = () => TestBed.inject(FavoritesService);

  it('starts empty', () => {
    expect(create().paths()).toEqual([]);
  });

  it('adds and removes an area and remembers the choice', () => {
    const service = create();

    service.toggle('absences');
    expect(service.has('absences')).toBe(true);
    expect(JSON.parse(localStorage.getItem('nw.favorites')!)).toEqual(['absences']);

    service.toggle('absences');
    expect(service.has('absences')).toBe(false);
  });

  it('keeps the order in which the areas were added', () => {
    const service = create();
    service.toggle('faq');
    service.toggle('absences');

    expect(service.paths()).toEqual(['faq', 'absences']);
  });

  it('reads the saved favorites again after a reload', () => {
    localStorage.setItem('nw.favorites', JSON.stringify(['faq']));

    expect(create().paths()).toEqual(['faq']);
  });

  it('ignores broken saved data', () => {
    localStorage.setItem('nw.favorites', '{not json');
    expect(create().paths()).toEqual([]);

    TestBed.resetTestingModule();
    localStorage.setItem('nw.favorites', JSON.stringify({ a: 1 }));
    expect(create().paths()).toEqual([]);
  });
});
