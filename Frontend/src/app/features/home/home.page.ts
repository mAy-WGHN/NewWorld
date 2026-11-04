import { Component, computed, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { RouterLink } from '@angular/router';
import { AuthService } from '../../core/auth/auth.service';
import { todayIso } from '../../core/dates';
import { NotificationsService } from '../../core/notifications.service';
import { ProfileService } from '../../core/profile.service';
import { Absence } from '../absences/absence-models';
import { AbsencesApi } from '../absences/absences.api';
import { HOME_CARDS } from './home-cards';
import { buildTasks, upcomingAppointments } from './home-logic';
import { HomeApi } from './home.api';

/** Background of a tile by its tone: Material's card colour is set through its own variable. */
const TONE: Record<string, string> = {
  blue: '[--mat-card-elevated-container-color:var(--app-tile-blue-bg)]',
  green: '[--mat-card-elevated-container-color:var(--app-tile-green-bg)]',
  purple: '[--mat-card-elevated-container-color:var(--app-tile-purple-bg)]',
  orange: '[--mat-card-elevated-container-color:var(--app-tile-orange-bg)]',
};

/** The start page: greeting, the four areas, what needs attention, next appointments. Nothing here is invented: no data, no tile. */
@Component({
  selector: 'app-home',
  imports: [RouterLink, MatButtonModule, MatCardModule, MatIconModule],
  templateUrl: './home.page.html',
})
export class HomePage {
  private readonly auth = inject(AuthService);
  private readonly absencesApi = inject(AbsencesApi);
  private readonly homeApi = inject(HomeApi);
  private readonly notifications = inject(NotificationsService);

  protected readonly firstName = inject(ProfileService).firstName;

  private readonly absences = signal<Absence[]>([]);
  private readonly pendingApprovals = signal<number | null>(null);

  protected readonly tone = TONE;

  protected readonly cards = computed(() => {
    const privileges = this.auth.privileges();
    return HOME_CARDS.map((card) => ({ ...card, chips: card.chips.filter((c) => privileges.hasAny(c.requires)) }));
  });

  protected readonly tasks = computed(() =>
    buildTasks({
      pendingApprovals: this.pendingApprovals(),
      missingCertificates: this.absences().filter((a) => a.type === 'Sickness' && a.status === 'Reported' && a.certificateStatus === 'Missing').length,
      unread: this.notifications.unread(),
    }),
  );

  protected readonly appointments = computed(() => upcomingAppointments(this.absences(), todayIso()));

  constructor() {
    const privileges = this.auth.privileges();
    if (privileges.has('absence:read-own')) {
      void this.absencesApi.list().then((list) => this.absences.set(list), () => undefined);
    }
    if (privileges.has('absence:approve')) {
      void this.homeApi.pendingApprovals().then((count) => this.pendingApprovals.set(count), () => undefined);
    }
  }
}
