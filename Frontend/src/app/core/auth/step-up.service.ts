import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { ConfigService } from '../config';
import { AuthService } from './auth.service';

/** Where the person was going when the second factor was asked for (session storage: it survives the trip to Microsoft). */
const RETURN_KEY = 'nw.stepup.return';

interface StartResponse {
  authorizationUrl: string;
}

interface TokenResponse {
  accessToken: string;
  expiresAt: string;
}

export type StepUpFailure = 'no-link' | 'unavailable' | 'not-confirmed' | 'error';

export class StepUpError extends Error {
  constructor(readonly failure: StepUpFailure) {
    super(failure);
  }
}

/** The problem title of an answer (`{"title": "..."}`), if there is one. */
export function problemTitleOf(error: unknown): string | undefined {
  if (error instanceof HttpErrorResponse && typeof error.error === 'object' && error.error !== null) {
    const title = (error.error as { title?: unknown }).title;
    return typeof title === 'string' ? title : undefined;
  }
  return undefined;
}

/** True if a service refused the call only because the second factor is missing (see `Shared.Authorization`). */
export const isSecondFactorRequired = (error: unknown): boolean => error instanceof HttpErrorResponse && error.status === 403 && problemTitleOf(error) === 'SecondFactorRequired';

/**
 * The second factor with the Microsoft account (decision 45): only for what needs it (the payroll statement). The sign-in stays personnel number + PIN.
 * `begin` sends the person to Microsoft (the Authenticator app asks there); on the way back `complete` fetches a new access token that carries the
 * confirmation for a short time. Nothing of Microsoft is kept here, and the PIN is never involved.
 */
@Injectable({ providedIn: 'root' })
export class StepUpService {
  private readonly http = inject(HttpClient);
  private readonly config = inject(ConfigService);
  private readonly auth = inject(AuthService);

  private beginning = false;

  /** The browser is sent away through this function (replaced in tests). */
  navigate: (url: string) => void = (url) => window.location.assign(url);

  /** Remembers where to return to and sends the person to Microsoft. Throws a `StepUpError` if that is not possible. */
  async begin(returnUrl: string): Promise<void> {
    if (this.beginning) {
      return; // several calls of one page may be refused at once; one trip to Microsoft is enough
    }
    this.beginning = true;
    this.writeReturn(returnUrl);
    try {
      const started = await firstValueFrom(this.http.post<StartResponse>(this.config.url('access', '/api/auth/step-up/start'), null));
      this.navigate(started.authorizationUrl);
    } catch (error) {
      this.beginning = false;
      throw this.toError(error);
    }
  }

  /** After the return from Microsoft: takes the new access token and gives back the address to continue with. */
  async complete(): Promise<string> {
    let token: TokenResponse;
    try {
      token = await firstValueFrom(this.http.post<TokenResponse>(this.config.url('access', '/api/auth/step-up/token'), null));
    } catch (error) {
      throw this.toError(error);
    }
    this.auth.acceptAccessToken(token.accessToken);
    return this.takeReturn();
  }

  /** The address stored for the way back (without removing it, e.g. for a retry). */
  pendingReturn(): string {
    return this.readReturn() ?? '/';
  }

  private toError(error: unknown): StepUpError {
    const title = problemTitleOf(error);
    if (title === 'NoEntraLink') return new StepUpError('no-link');
    if (title === 'StepUpUnavailable') return new StepUpError('unavailable');
    if (title === 'SecondFactorRequired') return new StepUpError('not-confirmed');
    return new StepUpError('error');
  }

  private takeReturn(): string {
    const url = this.readReturn() ?? '/';
    this.writeReturn(null);
    return url;
  }

  /** Only addresses inside the portal are taken back (a stored foreign address would be an open redirect). */
  private readReturn(): string | null {
    try {
      const value = sessionStorage.getItem(RETURN_KEY);
      return value && value.startsWith('/') && !value.startsWith('//') ? value : null;
    } catch {
      return null;
    }
  }

  private writeReturn(value: string | null): void {
    try {
      if (value) {
        sessionStorage.setItem(RETURN_KEY, value);
      } else {
        sessionStorage.removeItem(RETURN_KEY);
      }
    } catch {
      // storage blocked: the person returns to the start page
    }
  }
}
