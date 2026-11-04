import { Injectable } from '@angular/core';

export type ServiceName = 'access' | 'workforce' | 'portal' | 'notifications';

export interface AppConfig {
  /** Base path of each backend service as the browser reaches it (dev server proxy or reverse proxy), without a trailing slash. */
  services: Record<ServiceName, string>;
}

const DEFAULTS: AppConfig = {
  services: { access: '/access', workforce: '/workforce', portal: '/portal', notifications: '/notifications' },
};

/** Runtime configuration (`config.json` next to the app), so the same build runs in every environment. */
@Injectable({ providedIn: 'root' })
export class ConfigService {
  private config: AppConfig = DEFAULTS;

  async load(fetcher: typeof fetch = fetch): Promise<void> {
    try {
      const response = await fetcher('config.json', { cache: 'no-store' });
      if (response.ok) {
        const loaded = (await response.json()) as Partial<AppConfig>;
        this.config = { services: { ...DEFAULTS.services, ...loaded.services } };
      }
    } catch {
      // without a config file the defaults apply
    }
  }

  url(service: ServiceName, path: string): string {
    return `${this.config.services[service].replace(/\/$/, '')}${path}`;
  }

  /** True for requests to one of our own services (they get the access token, nothing else does). */
  isServiceUrl(url: string): boolean {
    return Object.values(this.config.services).some((base) => url === base || url.startsWith(base + '/'));
  }
}
