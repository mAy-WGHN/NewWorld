import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { ConfigService } from '../config';
import { MyPrivileges, PrivilegeGrant, PrivilegeSet } from './privileges';

interface TokenResponse {
  accessToken: string;
  expiresAt: string;
  refreshToken: string;
}

export type LoginFailure = 'invalid' | 'locked' | 'unavailable' | 'error';

export class LoginError extends Error {
  constructor(
    readonly failure: LoginFailure,
    readonly retryAfterSeconds?: number,
  ) {
    super(failure);
  }
}

/** Only the refresh token survives a reload, and only for the tab (session storage); the access token lives in memory. The PIN is never kept. */
const REFRESH_KEY = 'nw.refresh';

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly config = inject(ConfigService);

  private readonly accessToken = signal<string | null>(null);
  private readonly me = signal<MyPrivileges | null>(null);
  private refreshing: Promise<boolean> | null = null;

  readonly isAuthenticated = computed(() => this.me() !== null);
  readonly employeeId = computed(() => this.me()?.employeeId ?? null);
  readonly roles = computed(() => this.me()?.roles ?? []);
  /** The name and personnel number as Access knows them: there for everybody who is signed in, with or without an employee card. */
  readonly name = computed(() => this.me()?.name?.trim() || null);
  readonly personnelNumber = computed(() => this.me()?.personnelNumber?.trim() || null);
  /** The privileges the person really has (the services check against these, whatever the screen shows). */
  readonly realPrivileges = computed(() => (this.me() ? new PrivilegeSet(this.me()!.privileges) : PrivilegeSet.empty));
  private readonly preview = signal<{ role: string; set: PrivilegeSet } | null>(null);
  /** The name of the role whose view is shown as a preview, or null. */
  readonly previewRole = computed(() => this.preview()?.role ?? null);
  /** What the screen shows: the privileges of the previewed role if there is one, otherwise the real ones. Only guides the person; the services decide. */
  readonly privileges = computed(() => this.preview()?.set ?? this.realPrivileges());

  /** Shows the portal as a person with only this role would see it (menus, buttons, pages); the requests still go out with the real rights. */
  startPreview(role: string, grants: readonly PrivilegeGrant[]): void {
    this.preview.set({ role, set: new PrivilegeSet(grants) });
  }

  endPreview(): void {
    this.preview.set(null);
  }

  get token(): string | null {
    return this.accessToken();
  }

  /** Takes a new access token (e.g. the one that carries the second factor); the refresh token stays as it is. */
  acceptAccessToken(token: string): void {
    this.accessToken.set(token);
  }

  /** Restores the session after a reload from the refresh token. */
  async restore(): Promise<void> {
    if (this.readRefreshToken()) {
      await this.refresh();
    }
  }

  async login(personnelNumber: string, pin: string): Promise<void> {
    let tokens: TokenResponse;
    try {
      tokens = await firstValueFrom(
        this.http.post<TokenResponse>(this.config.url('access', '/api/auth/login'), { personnelNumber, pin }),
      );
    } catch (error) {
      throw this.toLoginError(error);
    }
    this.accept(tokens);
    await this.loadPrivileges();
  }

  /** Exchanges the refresh token for new tokens. Concurrent calls share one request (the refresh token rotates and can be used once). */
  refresh(): Promise<boolean> {
    this.refreshing ??= this.doRefresh().finally(() => (this.refreshing = null));
    return this.refreshing;
  }

  async logout(): Promise<void> {
    if (this.accessToken()) {
      try {
        await firstValueFrom(this.http.post(this.config.url('access', '/api/auth/logout'), null));
      } catch {
        // the local session ends anyway; the server side expires by itself
      }
    }
    this.clear();
  }

  /** Drops the local session without telling the server (e.g. after a refresh token was rejected). */
  clear(): void {
    this.preview.set(null);
    this.accessToken.set(null);
    this.me.set(null);
    this.writeRefreshToken(null);
  }

  private async doRefresh(): Promise<boolean> {
    const refreshToken = this.readRefreshToken();
    if (!refreshToken) {
      return false;
    }
    try {
      const tokens = await firstValueFrom(
        this.http.post<TokenResponse>(this.config.url('access', '/api/auth/refresh'), { refreshToken }),
      );
      this.accept(tokens);
      if (!this.me()) {
        await this.loadPrivileges();
      }
      return true;
    } catch (error) {
      // Only a rejected token ends the session; a network error keeps it for the next try.
      if (error instanceof HttpErrorResponse && error.status === 401) {
        this.clear();
      }
      return false;
    }
  }

  private accept(tokens: TokenResponse): void {
    this.accessToken.set(tokens.accessToken);
    this.writeRefreshToken(tokens.refreshToken);
  }

  private async loadPrivileges(): Promise<void> {
    this.me.set(await firstValueFrom(this.http.get<MyPrivileges>(this.config.url('access', '/api/me/privileges'))));
  }

  private toLoginError(error: unknown): LoginError {
    if (error instanceof HttpErrorResponse) {
      switch (error.status) {
        case 401:
          return new LoginError('invalid');
        case 429:
          return new LoginError('locked', Number(error.headers.get('Retry-After')) || undefined);
        case 503:
          return new LoginError('unavailable');
      }
    }
    return new LoginError('error');
  }

  private readRefreshToken(): string | null {
    try {
      return sessionStorage.getItem(REFRESH_KEY);
    } catch {
      return null;
    }
  }

  private writeRefreshToken(value: string | null): void {
    try {
      if (value) {
        sessionStorage.setItem(REFRESH_KEY, value);
      } else {
        sessionStorage.removeItem(REFRESH_KEY);
      }
    } catch {
      // storage blocked: the session then ends with the page
    }
  }
}
