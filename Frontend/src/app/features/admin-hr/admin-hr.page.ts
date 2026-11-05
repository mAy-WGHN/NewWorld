import { Component, computed, inject, signal } from '@angular/core';
import { AuthService } from '../../core/auth/auth.service';
import { PAGE_UI } from '../../shared/ui/material';
import { CertificatesSection } from './certificates-section';
import { EmployeeCardSection } from './employee-card-section';
import { HolidaySection } from './holiday-section';
import { UnavailabilitySection } from './unavailability-section';
import { VacationSection } from './vacation-section';
import { WorkingTimeSection } from './working-time-section';

type SectionId = 'vacation' | 'working-time' | 'certificates' | 'employee-card' | 'unavailability' | 'holidays';

interface SectionTab {
  id: SectionId;
  label: string;
  /** Any of these privileges shows the section (the services still check every call). */
  requires: readonly string[];
}

const SECTIONS: readonly SectionTab[] = [
  { id: 'vacation', label: 'Urlaub', requires: ['absence:manage-vacation-rules'] },
  { id: 'working-time', label: 'Arbeitszeitkonto', requires: ['workingtime:manage-rules'] },
  { id: 'certificates', label: 'Krankmeldung und AU', requires: ['certificate:manage-retention', 'certificate:read-access-log'] },
  { id: 'employee-card', label: 'Mitarbeiterkarte', requires: ['employee:manage-number-series', 'employee:manage-lists'] },
  { id: 'unavailability', label: 'Nicht-Verfügbarkeitsarten', requires: ['unavailability:manage-kinds'] },
  { id: 'holidays', label: 'Feiertage', requires: ['absence:manage-holiday-opening'] },
];

/**
 * "Personal-Einstellungen" for HR: the settings that the personnel department maintains itself, one tab per topic. A tab is shown only
 * with its privilege; each section loads its own data and saves with its own button.
 */
@Component({
  selector: 'app-admin-hr',
  imports: [...PAGE_UI, VacationSection, WorkingTimeSection, CertificatesSection, EmployeeCardSection, UnavailabilitySection, HolidaySection],
  templateUrl: './admin-hr.page.html',
})
export class AdminHrPage {
  private readonly auth = inject(AuthService);

  protected readonly tabs = computed(() => SECTIONS.filter((s) => this.auth.privileges().hasAny(s.requires)));
  private readonly chosen = signal<SectionId | null>(null);
  protected readonly active = computed(() => {
    const tabs = this.tabs();
    return tabs.find((t) => t.id === this.chosen())?.id ?? tabs[0]?.id ?? null;
  });

  protected choose(id: SectionId): void {
    this.chosen.set(id);
  }

  /** Arrow keys move between the tabs (WAI-ARIA tabs pattern). */
  protected move(event: KeyboardEvent, index: number): void {
    const tabs = this.tabs();
    const next = event.key === 'ArrowRight' ? index + 1 : event.key === 'ArrowLeft' ? index - 1 : event.key === 'Home' ? 0 : event.key === 'End' ? tabs.length - 1 : null;
    if (next === null) {
      return;
    }
    event.preventDefault();
    const target = tabs[(next + tabs.length) % tabs.length];
    this.chosen.set(target.id);
    queueMicrotask(() => (event.currentTarget as HTMLElement | null)?.parentElement?.querySelector<HTMLElement>(`#tab-${target.id}`)?.focus());
  }
}
