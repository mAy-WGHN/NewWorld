import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom, map } from 'rxjs';
import { ConfigService } from '../../core/config';
import { CashRegister, CashRegisterEvent, CashRegisterList, PersonOption, ReminderSettings, SealPayload } from './cash-register-models';

interface Envelope<T> {
  success: boolean;
  data: T;
}

/** The Kassenregister of the portal service (`/api/cash-registers`); answers come wrapped as `{success, data}`. Errors are left to the caller. */
@Injectable({ providedIn: 'root' })
export class CashRegisterApi {
  private readonly http = inject(HttpClient);
  private readonly config = inject(ConfigService);

  private url(path = ''): string {
    return this.config.url('portal', `/api/cash-registers${path}`);
  }

  list(): Promise<CashRegisterList> {
    return firstValueFrom(this.http.get<Envelope<CashRegisterList>>(this.url()).pipe(map((a) => a.data)));
  }

  /** "Kasse erfassen"; with the form of a seal the drawer is sealed right away. */
  create(storeId: string, name: string, seal: SealPayload | null = null): Promise<CashRegister> {
    const body = seal ? { storeId, name, sealed: true, photoId: seal.photoId, sealedBy: seal.sealedBy } : { storeId, name, sealed: false };
    return firstValueFrom(this.http.post<Envelope<CashRegister>>(this.url(), body).pipe(map((a) => a.data)));
  }

  /** "Bearbeiten": name, store and whether the drawer is sealed (a new seal comes with its form); counts as a new recording. */
  update(id: string, storeId: string, name: string, sealed: boolean, seal: SealPayload | null = null): Promise<CashRegister> {
    const body = seal ? { storeId, name, sealed, photoId: seal.photoId, sealedBy: seal.sealedBy } : { storeId, name, sealed };
    return firstValueFrom(this.http.put<Envelope<CashRegister>>(this.url(`/${id}`), body).pipe(map((a) => a.data)));
  }

  /** The photo of a seal is uploaded first; its id goes into the form. */
  uploadPhoto(file: File): Promise<string> {
    const form = new FormData();
    form.append('file', file);
    return firstValueFrom(this.http.post<Envelope<{ id: string }>>(this.url('/photos'), form).pipe(map((a) => a.data.id)));
  }

  /** "Siegel anbringen / erneuern": the form of a seal. */
  seal(id: string, seal: SealPayload): Promise<CashRegister> {
    return firstValueFrom(this.http.post<Envelope<CashRegister>>(this.url(`/${id}/seal`), { photoId: seal.photoId, sealedBy: seal.sealedBy }).pipe(map((a) => a.data)));
  }

  /** "Siegel aufheben": the drawer is not sealed any more. */
  unseal(id: string): Promise<CashRegister> {
    return firstValueFrom(this.http.post<Envelope<CashRegister>>(this.url(`/${id}/unseal`), {}).pipe(map((a) => a.data)));
  }

  delete(id: string): Promise<unknown> {
    return firstValueFrom(this.http.delete(this.url(`/${id}`)));
  }

  /** The photo of a seal as an image (the request carries the sign-in, so it is loaded here and not by an address in the page). */
  photo(id: string): Promise<Blob> {
    return firstValueFrom(this.http.get(this.url(`/photos/${id}`), { responseType: 'blob' }));
  }

  /** Who attached the seal: name search in the copy of the employees (from two letters). */
  people(query: string): Promise<PersonOption[]> {
    return firstValueFrom(this.http.get<Envelope<PersonOption[]>>(this.url('/people'), { params: new HttpParams().set('query', query) }).pipe(map((a) => a.data)));
  }

  reminderSettings(): Promise<ReminderSettings> {
    return firstValueFrom(this.http.get<Envelope<ReminderSettings>>(this.url('/reminder-settings')).pipe(map((a) => a.data)));
  }

  saveReminderSettings(settings: ReminderSettings): Promise<ReminderSettings> {
    return firstValueFrom(this.http.put<Envelope<ReminderSettings>>(this.url('/reminder-settings'), settings).pipe(map((a) => a.data)));
  }

  /** "Filialhistorie": every step, newest first, of one store or of all. */
  history(storeId?: string): Promise<CashRegisterEvent[]> {
    const params = storeId ? new HttpParams().set('storeId', storeId) : undefined;
    return firstValueFrom(this.http.get<Envelope<CashRegisterEvent[]>>(this.url('/history'), { params }).pipe(map((a) => a.data)));
  }
}
