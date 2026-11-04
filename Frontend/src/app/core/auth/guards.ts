import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from './auth.service';

/** Only for signed-in people; everybody else goes to the login page and comes back afterwards. */
export const authGuard: CanActivateFn = (_route, state) => {
  const auth = inject(AuthService);
  return auth.isAuthenticated() ? true : inject(Router).createUrlTree(['/login'], { queryParams: { returnUrl: state.url } });
};

/** The login page is for signed-out people only. */
export const loginGuard: CanActivateFn = () => {
  return inject(AuthService).isAuthenticated() ? inject(Router).createUrlTree(['/']) : true;
};

/** Route data `requires`: at least one of these privileges. This only guides the person; the services enforce the rights. */
export const privilegeGuard: CanActivateFn = (route) => {
  const requires = route.data['requires'] as string[] | undefined;
  return inject(AuthService).privileges().hasAny(requires) ? true : inject(Router).createUrlTree(['/forbidden']);
};
