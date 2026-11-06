import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { MatPaginatorIntl } from '@angular/material/paginator';
import { GermanPaginatorIntl } from './shared/ui/table';
import { ApplicationConfig, inject, provideAppInitializer, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideRouter } from '@angular/router';
import { routes } from './app.routes';
import { AuthService } from './core/auth/auth.service';
import { authInterceptor } from './core/auth/auth.interceptor';
import { ConfigService } from './core/config';

export const appConfig: ApplicationConfig = {
  providers: [
    { provide: MatPaginatorIntl, useClass: GermanPaginatorIntl },
    provideBrowserGlobalErrorListeners(),
    provideRouter(routes),
    provideHttpClient(withInterceptors([authInterceptor])),
    // Before the first page: read config.json, then restore a running session from the refresh token.
    provideAppInitializer(async () => {
      // inject() only works before the first await
      const config = inject(ConfigService);
      const auth = inject(AuthService);
      await config.load();
      await auth.restore();
    }),
  ],
};
