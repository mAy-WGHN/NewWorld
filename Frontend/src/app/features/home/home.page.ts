import { Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { AuthService } from '../../core/auth/auth.service';
import { todayIso } from '../../core/dates';
import { NotificationsService } from '../../core/notifications.service';
import { ProfileService } from '../../core/profile.service';
import { IconComponent } from '../../shared/icon';
import { Absence } from '../absences/absence-models';
import { AbsencesApi } from '../absences/absences.api';
import { HOME_CARDS } from './home-cards';
import { buildTasks, upcomingAppointments } from './home-logic';
import { HomeApi } from './home.api';

/** The start page: greeting, the four areas, what needs attention, next appointments. Nothing here is invented: no data, no tile. */
@Component({
  selector: 'app-home',
  imports: [RouterLink, IconComponent],
  templateUrl: './home.page.html',
  styleUrl: './home.page.scss',
})
export class HomePage {
  private readonly auth = inject(AuthService);
  private readonly absencesApi = inject(AbsencesApi);
  private readonly homeApi = inject(HomeApi);
  private readonly notifications = inject(NotificationsService);

  protected readonly firstName = inject(ProfileService).firstName;

  private readonly absences = signal<Absence[]>([]);
  private readonly pendingApprovals = signal<number | null>(null);

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
