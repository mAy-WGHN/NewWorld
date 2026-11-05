import { Component, computed, inject, signal } from '@angular/core';
import { AuthService } from '../../core/auth/auth.service';
import { PAGE_UI } from '../../shared/ui/material';
import { CatalogSection } from './catalog-section';
import { PeopleSection } from './people-section';
import { RolesSection } from './roles-section';
import { ScopesSection } from './scopes-section';

type SectionKey = 'roles' | 'catalog' | 'scopes' | 'people';

const SECTIONS: readonly { key: SectionKey; label: string; any: readonly string[] }[] = [
  { key: 'roles', label: 'Rollen', any: ['access:role:read'] },
  { key: 'catalog', label: 'Rechte-Katalog', any: ['access:catalog:read'] },
  { key: 'scopes', label: 'Filial-Gruppen', any: ['access:scope:read'] },
  {
    key: 'people',
    label: 'Personen',
    any: ['access:assignment:read', 'access:responsibility:read', 'access:person:read', 'access:main-store:update'],
  },
];

/**
 * "Rollen und Rechte" (area Verwaltung): the administration of the ACL in Access in tabs. A tab only shows if the person has the
 * privilege to read what it holds; the service checks every call again.
 */
@Component({
  selector: 'app-admin-access',
  imports: [...PAGE_UI, RolesSection, CatalogSection, ScopesSection, PeopleSection],
  templateUrl: './admin-access.page.html',
})
export class AdminAccessPage {
  private readonly auth = inject(AuthService);

  protected readonly chosen = signal<SectionKey | null>(null);

  protected readonly sections = computed(() => SECTIONS.filter((s) => this.auth.privileges().hasAny(s.any)));
  protected readonly active = computed<SectionKey | null>(() => {
    const visible = this.sections();
    return visible.find((s) => s.key === this.chosen())?.key ?? visible[0]?.key ?? null;
  });

  protected select(key: SectionKey): void {
    this.chosen.set(key);
  }

  /** Left and right arrows move between the tabs (keyboard use of the tab list). */
  protected onKey(event: KeyboardEvent): void {
    const visible = this.sections();
    const index = visible.findIndex((s) => s.key === this.active());
    const next = event.key === 'ArrowRight' ? index + 1 : event.key === 'ArrowLeft' ? index - 1 : null;
    if (next === null || visible.length === 0) {
      return;
    }
    event.preventDefault();
    const target = visible[(next + visible.length) % visible.length];
    this.chosen.set(target.key);
    queueMicrotask(() => (document.getElementById(`access-tab-${target.key}`) as HTMLElement | null)?.focus());
  }
}
