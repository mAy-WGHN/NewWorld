import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { AuthService } from './auth/auth.service';
import { ConfigService } from './config';

interface EmployeeCardName {
  firstName: string;
  lastName: string;
  personnelNumber: string;
}

/**
 * Who is signed in, for greeting and header. The name comes from the own employee card in Workforce; people without such a card
 * (for example employees that only exist in BC) simply have no name here, and the page greets without it.
 * TODO(Backend): a small "who am I" call (name, main store) would serve everybody; until then only the card is available.
 */
@Injectable({ providedIn: 'root' })
export class ProfileService {
  private readonly http = inject(HttpClient);
  private readonly config = inject(ConfigService);
  private readonly auth = inject(AuthService);

  readonly firstName = signal<string | null>(null);
  readonly lastName = signal<string | null>(null);
  readonly personnelNumber = signal<string | null>(null);

  readonly displayName = computed(() => [this.firstName(), this.lastName()].filter(Boolean).join(' ') || null);
  readonly initials = computed(() => {
    const letters = [this.firstName(), this.lastName()].map((n) => n?.trim()[0]?.toUpperCase() ?? '').join('');
    return letters || '?';
  });

  async load(): Promise<void> {
    if (!this.auth.privileges().has('employee:read-own')) {
      return;
    }
    try {
      const card = await firstValueFrom(this.http.get<EmployeeCardName>(this.config.url('workforce', '/api/me/employee-card')));
      this.firstName.set(card.firstName ?? null);
      this.lastName.set(card.lastName ?? null);
      this.personnelNumber.set(card.personnelNumber ?? null);
    } catch {
      // no card or the service is not reachable: the page works without a name
    }
  }

  clear(): void {
    this.firstName.set(null);
    this.lastName.set(null);
    this.personnelNumber.set(null);
  }
}
