import { TestBed } from '@angular/core/testing';
import { ThemeService } from './theme.service';

describe('ThemeService', () => {
  beforeEach(() => {
    localStorage.clear();
    document.documentElement.style.colorScheme = '';
  });

  const create = () => TestBed.inject(ThemeService);

  it('follows the device by default', () => {
    const theme = create();

    expect(theme.mode()).toBe('system');
    expect(document.documentElement.style.colorScheme).toBe('light dark');
  });

  it('applies and remembers the light and dark choice', () => {
    create().set('dark');
    expect(document.documentElement.style.colorScheme).toBe('dark');

    TestBed.resetTestingModule();
    const again = create();
    expect(again.mode()).toBe('dark');
    expect(document.documentElement.style.colorScheme).toBe('dark');

    again.set('light');
    expect(document.documentElement.style.colorScheme).toBe('light');
  });

  it('goes back to the device setting', () => {
    const theme = create();
    theme.set('dark');
    theme.set('system');

    expect(document.documentElement.style.colorScheme).toBe('light dark');
  });

  it('ignores a stored value it does not know', () => {
    localStorage.setItem('nw.theme', 'pink');

    expect(create().mode()).toBe('system');
  });
});
