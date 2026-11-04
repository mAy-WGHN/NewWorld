import { Component, computed, input } from '@angular/core';

/** Circle as a path: centre and radius. */
const circle = (cx: number, cy: number, r: number): string => `M${cx - r} ${cy}a${r} ${r} 0 1 0 ${2 * r} 0a${r} ${r} 0 1 0 ${-2 * r} 0`;

/** Simple line icons on a 24x24 grid. Each icon is a list of path data. */
const ICONS: Record<string, string[]> = {
  home: ['M3 11l9-8 9 8', 'M5 10v10h5v-6h4v6h5V10'],
  star: ['M12 3l2.7 5.6 6.1.9-4.4 4.3 1 6.1L12 17l-5.4 2.9 1-6.1L3.2 9.5l6.1-.9z'],
  bell: ['M6 16v-5a6 6 0 1112 0v5l1.5 2h-15z', 'M10 21h4'],
  settings: ['M4 7h9', 'M17 7h3', 'M4 17h3', 'M11 17h9', circle(15, 7, 2), circle(9, 17, 2)],
  help: [circle(12, 12, 9), 'M9.5 9.5a2.5 2.5 0 115 0c0 1.7-2.5 2-2.5 4', 'M12 17h.01'],
  search: [circle(11, 11, 6), 'M20 20l-4.2-4.2'],
  check: [circle(12, 12, 9), 'M8 12.5l2.8 2.8L16 9.5'],
  folder: ['M3 7a2 2 0 012-2h4l2 2h8a2 2 0 012 2v8a2 2 0 01-2 2H5a2 2 0 01-2-2z'],
  cap: ['M2 9l10-5 10 5-10 5z', 'M6 11.5V16c0 1.5 3 3 6 3s6-1.5 6-3v-4.5'],
  chart: ['M5 20V10', 'M12 20V4', 'M19 20v-7'],
  calendar: ['M4 6h16v14H4z', 'M4 10h16', 'M8 3v4', 'M16 3v4'],
  'chevron-down': ['M6 9l6 6 6-6'],
  'chevron-right': ['M9 6l6 6-6 6'],
  grid: ['M4 4h7v7H4z', 'M13 4h7v7h-7z', 'M4 13h7v7H4z', 'M13 13h7v7h-7z'],
  menu: ['M4 7h16', 'M4 12h16', 'M4 17h16'],
  alert: [circle(12, 12, 9), 'M12 7.5v5', 'M12 16h.01'],
  info: [circle(12, 12, 9), 'M12 11v5', 'M12 8h.01'],
  megaphone: ['M3 11v3h3l7 4V7L6 11z', 'M16 9a4 4 0 010 6'],
  tasks: ['M9 6h11', 'M9 12h11', 'M9 18h11', 'M4.5 6l1 1 2-2', 'M4.5 12l1 1 2-2', 'M4.5 18l1 1 2-2'],
  logout: ['M10 4H5v16h5', 'M15 8l4 4-4 4', 'M19 12H9'],
};

/** A line icon in the current text colour; decorative unless a `label` is given. */
@Component({
  selector: 'app-icon',
  template: `
    <svg viewBox="0 0 24 24" [attr.width]="size()" [attr.height]="size()" fill="none" stroke="currentColor" stroke-width="1.8"
         stroke-linecap="round" stroke-linejoin="round" [attr.aria-hidden]="label() ? null : 'true'" [attr.aria-label]="label() || null"
         [attr.role]="label() ? 'img' : null">
      @for (d of paths(); track $index) {
        <path [attr.d]="d" />
      }
    </svg>
  `,
  styles: ':host { display: inline-flex; flex-shrink: 0; }',
})
export class IconComponent {
  readonly name = input.required<string>();
  readonly size = input(20);
  readonly label = input('');

  protected readonly paths = computed(() => ICONS[this.name()] ?? []);
}
