import { HttpErrorResponse } from '@angular/common/http';
import { GENERIC_PROBLEM, envelopeMessage, envelopeProblem } from './problem';

const answer = (status: number, body: unknown) => new HttpErrorResponse({ status, error: body });

describe('problem texts of the portal services', () => {
  it('takes the message of the service, else the generic text', () => {
    expect(envelopeMessage(answer(409, { success: false, message: 'Schon vorhanden.', errors: [] }))).toBe('Schon vorhanden.');
    expect(envelopeMessage(answer(500, { success: false }))).toBe(GENERIC_PROBLEM);
    expect(envelopeMessage(answer(500, 'Internal Server Error'))).toBe(GENERIC_PROBLEM);
    expect(envelopeMessage(new Error('boom'))).toBe(GENERIC_PROBLEM);
  });

  it('names what is wrong with the fields of a validation error, each text once', () => {
    const body = { success: false, message: 'Validierung fehlgeschlagen.', errors: [{ field: 'a', message: 'Zu lang.' }, { field: 'b', message: 'Zu lang.' }, { field: 'c', message: 'Pflicht.' }] };

    expect(envelopeProblem(answer(422, body))).toBe('Zu lang. Pflicht.');
  });

  it('falls back to the message when a validation error names no field, and for other statuses', () => {
    expect(envelopeProblem(answer(422, { success: false, message: 'Nicht lesbar.', errors: [] }))).toBe('Nicht lesbar.');
    expect(envelopeProblem(answer(403, { success: false, message: 'Keine Berechtigung.', errors: [{ message: 'egal' }] }))).toBe('Keine Berechtigung.');
    expect(envelopeProblem(undefined)).toBe(GENERIC_PROBLEM);
  });
});
