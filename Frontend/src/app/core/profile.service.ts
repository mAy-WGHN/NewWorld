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

/** `GET /api/me/profile` of Workforce: name, personnel number and main store of the signed-in person (organisation data). */
interface ProfileResponse {
  name: string;
  personnelNumber: string | null;
  mainStore: { storeId: string; number: string; name: string } | null;
}

/**
 * First and last name from the one name of BC: "Vorname Nachname" or "Nachname, Vorname". Several words after the first belong to the last name.
 */
export function splitName(name: string): { firstName: string; lastName: string } {
  const text = name.trim().replace(/\s+/g, ' ');
  const comma = text.indexOf(',');
  if (comma >= 0) {
    return { firstName: text.slice(comma + 1).trim(), lastName: text.slice(0, comma).trim() };
  }
  const space = text.indexOf(' ');
  return space < 0 ? { firstName: text, lastName: '' } : { firstName: text.slice(0, space), lastName: text.slice(space + 1).trim() };
}

/**
 * Who is signed in, for greeting and header. The names come from the own employee card in Workforce (people without one, for example
 * people that only exist in BC, have none there); then the name that Access knows from BC stands in, so everybody who is signed in has a
 * name. The personnel number comes from the card or from Access.
 */
@Injectable({ providedIn: 'root' })
export class ProfileService {
  private readonly http = inject(HttpClient);
  private readonly config = inject(ConfigService);
  private readonly auth = inject(AuthService);

  private readonly cardFirstName = signal<string | null>(null);
  private readonly cardLastName = signal<string | null>(null);
  private readonly cardNumber = signal<string | null>(null);

  /** The name of Access split into first and last name (only used where the card has none). */
  private readonly accessName = computed(() => {
    const name = this.auth.name();
    return name ? splitName(name) : null;
  });

  readonly firstName = computed(() => this.cardFirstName() ?? this.accessName()?.firstName ?? null);
  readonly lastName = computed(() => this.cardLastName() ?? this.accessName()?.lastName ?? null);
  readonly personnelNumber = computed(() => this.cardNumber() ?? this.auth.personnelNumber());

  readonly displayName = computed(() => [this.firstName(), this.lastName()].filter(Boolean).join(' ') || null);
  readonly initials = computed(() => {
    const letters = [this.firstName(), this.lastName()].map((n) => n?.trim()[0]?.toUpperCase() ?? '').join('');
    return letters || '?';
  });

  /** The main store with number and name (`GET /api/me/profile`, no privilege needed); null where the person has none or the call fails. */
  readonly storeName = signal<string | null>(null);

  async load(): Promise<void> {
    void this.loadMainStore(); // not awaited: the name does not wait for the store
    if (!this.auth.privileges().has('employee:read-own')) {
      return;
    }
    try {
      const card = await firstValueFrom(this.http.get<EmployeeCardName>(this.config.url('workforce', '/api/me/employee-card')));
      this.cardFirstName.set(card.firstName ?? null);
      this.cardLastName.set(card.lastName ?? null);
      this.cardNumber.set(card.personnelNumber ?? null);
    } catch {
      // no card or the service is not reachable: the page works without a name
    }
  }

  private async loadMainStore(): Promise<void> {
    try {
      const profile = await firstValueFrom(this.http.get<ProfileResponse>(this.config.url('workforce', '/api/me/profile')));
      this.storeName.set(profile.mainStore ? `${profile.mainStore.name} (${profile.mainStore.number})` : null);
    } catch {
      // no copy of the person in Workforce yet, or the service is not reachable: the page works without a store
    }
  }

  clear(): void {
    this.storeName.set(null);
    this.cardFirstName.set(null);
    this.cardLastName.set(null);
    this.cardNumber.set(null);
  }
}
