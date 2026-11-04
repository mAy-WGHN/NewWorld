import { Routes } from '@angular/router';
import { authGuard, loginGuard, privilegeGuard } from './core/auth/guards';
import { ALL_ITEMS } from './core/nav/nav-items';
import { DashboardPage } from './pages/dashboard/dashboard';
import { ForbiddenPage } from './pages/placeholder/forbidden';
import { PlaceholderPage } from './pages/placeholder/placeholder';
import { LoginPage } from './pages/login/login';
import { Shell } from './layout/shell';

/** Every area of the navigation is a placeholder page until it is built; the privilege guard already applies. */
const areaRoutes: Routes = ALL_ITEMS.map((item) => ({
  path: item.path,
  component: PlaceholderPage,
  canActivate: [privilegeGuard],
  data: { requires: item.requires, title: item.label, about: item.about },
}));

export const routes: Routes = [
  { path: 'login', component: LoginPage, canActivate: [loginGuard], title: 'Anmelden' },
  {
    path: '',
    component: Shell,
    canActivate: [authGuard],
    children: [
      { path: '', component: DashboardPage, title: 'Start' },
      ...areaRoutes,
      { path: 'forbidden', component: ForbiddenPage, title: 'Kein Zugriff' },
    ],
  },
  { path: '**', redirectTo: '' },
];
