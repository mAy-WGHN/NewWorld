import { Type } from '@angular/core';
import { Routes } from '@angular/router';
import { authGuard, loginGuard, privilegeGuard } from './core/auth/guards';
import { ALL_ITEMS } from './core/nav/nav-items';
import { HomePage } from './features/home/home.page';
import { Shell } from './layout/shell';
import { AreasPage } from './pages/areas/areas';
import { FavoritesPage } from './pages/areas/favorites';
import { LoginPage } from './pages/login/login';
import { ForbiddenPage } from './pages/placeholder/forbidden';
import { PlaceholderPage } from './pages/placeholder/placeholder';

/** Areas that are built; they replace the placeholder of the same path. Loaded on demand. */
const builtAreas: Record<string, () => Promise<Type<unknown>>> = {
  absences: () => import('./features/absences/absences.page').then((m) => m.AbsencesPage),
  'employee-card': () => import('./features/employee-card/employee-card.page').then((m) => m.EmployeeCardPage),
  'team-absences': () => import('./features/team-absences/team-absences.page').then((m) => m.TeamAbsencesPage),
  faq: () => import('./features/faq/faq.page').then((m) => m.FaqPage),
  statistics: () => import('./features/statistics/statistics.page').then((m) => m.StatisticsPage),
  employees: () => import('./features/employees/employees.page').then((m) => m.EmployeesPage),
  'admin/hr': () => import('./features/admin-hr/admin-hr.page').then((m) => m.AdminHrPage),
  'admin/access': () => import('./features/admin-access/admin-access.page').then((m) => m.AdminAccessPage),
  'admin/templates': () => import('./features/notification-templates/template.page').then((m) => m.TemplatePage),
  'admin/audit': () => import('./features/audit/audit.page').then((m) => m.AuditPage),
  'sick-notes': () => import('./features/sick-note-review/sick-note-review.page').then((m) => m.SickNoteReviewPage),
  'my-sick-notes': () => import('./features/sick-notes/sick-notes.page').then((m) => m.SickNotesPage),
  'my-vacation': () => import('./features/vacation/vacation.page').then((m) => m.VacationPage),
  reports: () => import('./features/reports/reports.page').then((m) => m.ReportsPage),
  approvals: () => import('./features/approvals/approvals.page').then((m) => m.ApprovalsPage),
  notifications: () => import('./features/notifications/notifications.page').then((m) => m.NotificationsPage),
  'working-time': () => import('./features/working-time/working-time.page').then((m) => m.WorkingTimePage),
  'report-issue': () => import('./features/report-issue/report-issue.page').then((m) => m.ReportIssuePage),
};

/** Every area of the navigation is a placeholder page until it is built; the privilege guard applies to both. */
const areaRoutes: Routes = ALL_ITEMS.map((item) => {
  const load = builtAreas[item.path];
  return {
    path: item.path,
    ...(load ? { loadComponent: load } : { component: PlaceholderPage }),
    canActivate: [privilegeGuard],
    title: item.label,
    data: { requires: item.requires, title: item.label, about: item.about },
  };
});

export const routes: Routes = [
  { path: 'login', component: LoginPage, canActivate: [loginGuard], title: 'Anmelden' },
  {
    path: '',
    component: Shell,
    canActivate: [authGuard],
    children: [
      { path: '', component: HomePage, title: 'Startseite' },
      { path: 'areas', component: AreasPage, title: 'Alle Bereiche' },
      { path: 'favorites', component: FavoritesPage, title: 'Favoriten' },
      { path: 'settings', loadComponent: () => import('./features/settings/settings.page').then((m) => m.SettingsPage), title: 'Einstellungen' },
      { path: 'help', component: PlaceholderPage, title: 'Hilfe', data: { title: 'Hilfe', about: 'Anleitungen und Ansprechpersonen.' } },
      ...areaRoutes,
      { path: 'step-up/done', loadComponent: () => import('./pages/step-up/step-up-done').then((m) => m.StepUpDonePage), title: 'Bestätigung mit Microsoft' },
      { path: 'forbidden', component: ForbiddenPage, title: 'Kein Zugriff' },
    ],
  },
  { path: '**', redirectTo: '' },
];
