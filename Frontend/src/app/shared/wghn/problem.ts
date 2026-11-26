import { HttpErrorResponse } from '@angular/common/http';

/** What a person reads when a call failed and the service said nothing usable. */
export const GENERIC_PROBLEM = 'Das hat nicht geklappt. Bitte erneut versuchen.';

/** The `message` of an error answer of the portal services (`{success:false,message,errors[]}`), else the generic text. */
export function envelopeMessage(error: unknown): string {
  if (error instanceof HttpErrorResponse && typeof error.error === 'object' && error.error !== null) {
    const message = (error.error as { message?: unknown }).message;
    if (typeof message === 'string' && message) {
      return message;
    }
  }
  return GENERIC_PROBLEM;
}

/**
 * The problem as the person should read it: the messages of the fields for a validation error (422), otherwise the message of the service. One
 * function for every page that talks to the portal service; the pages do not keep their own copies.
 */
export function envelopeProblem(error: unknown): string {
  if (error instanceof HttpErrorResponse && error.status === 422 && typeof error.error === 'object' && error.error !== null) {
    const errors = (error.error as { errors?: { message?: unknown }[] }).errors;
    const messages = (errors ?? []).map((e) => e.message).filter((m): m is string => typeof m === 'string' && m.length > 0);
    if (messages.length > 0) {
      return [...new Set(messages)].join(' ');
    }
  }
  return envelopeMessage(error);
}
