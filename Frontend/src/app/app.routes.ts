import { Type } from '@angular/core';
import { Routes } from '@angular/router';
import { authGuard, loginGuard, privilegeGuard } from './core/auth/guards';
import { ALL_ITEMS } from './core/nav/nav-items';
import { DashboardPage } from './pages/dashboard/dashboard';
import { ForbiddenPage } from './pages/placeholder/forbidden';
import { PlaceholderPage } from './pages/placeholder/placeholder';
import { LoginPage } from './pages/login/login';
import { Shell } from './layout/shell';

/** Areas that are built; they replace the placeholder of the same path. Loaded on demand. */
const builtAreas: Record<string, () => Promise<Type<unknown>>> = {
  absences: () => import('./features/absences/absences.page').then((m) => m.AbsencesPage),
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
      { path: '', component: DashboardPage, title: 'Start' },
      ...areaRoutes,
      { path: 'forbidden', component: ForbiddenPage, title: 'Kein Zugriff' },
    ],
  },
  { path: '**', redirectTo: '' },
];
