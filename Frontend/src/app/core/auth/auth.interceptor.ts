import { HttpErrorResponse, HttpInterceptorFn, HttpRequest } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, from, switchMap, throwError } from 'rxjs';
import { ConfigService } from '../config';
import { AuthService } from './auth.service';

const withToken = (request: HttpRequest<unknown>, token: string | null) =>
  token ? request.clone({ setHeaders: { Authorization: `Bearer ${token}` } }) : request;

/**
 * Adds the access token to calls to our own services. A 401 triggers one refresh and one retry; if the refresh fails the session
 * ends and the person goes to the login page. Login and refresh calls themselves are left alone.
 */
export const authInterceptor: HttpInterceptorFn = (request, next) => {
  const config = inject(ConfigService);
  const auth = inject(AuthService);
  const router = inject(Router);

  if (!config.isServiceUrl(request.url) || request.url.includes('/api/auth/login') || request.url.includes('/api/auth/refresh')) {
    return next(request);
  }

  return next(withToken(request, auth.token)).pipe(
    catchError((error: unknown) => {
      if (!(error instanceof HttpErrorResponse) || error.status !== 401) {
        return throwError(() => error);
      }
      return from(auth.refresh()).pipe(
        switchMap((refreshed) => {
          if (!refreshed) {
            auth.clear();
            void router.navigate(['/login'], { queryParams: { returnUrl: router.url } });
            return throwError(() => error);
          }
          return next(withToken(request, auth.token));
        }),
      );
    }),
  );
};
