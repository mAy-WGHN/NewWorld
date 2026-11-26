import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom, map } from 'rxjs';
import { ConfigService } from '../../core/config';
import { CashRegister, CashRegisterEvent, CashRegisterList } from './cash-register-models';

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

  /** "Kasse erfassen". */
  create(storeId: string, name: string): Promise<CashRegister> {
    return firstValueFrom(this.http.post<Envelope<CashRegister>>(this.url(), { storeId, name }).pipe(map((a) => a.data)));
  }

  /** "Bearbeiten": counts as a new recording, the seal falls away. */
  update(id: string, storeId: string, name: string): Promise<CashRegister> {
    return firstValueFrom(this.http.put<Envelope<CashRegister>>(this.url(`/${id}`), { storeId, name }).pipe(map((a) => a.data)));
  }

  /** "Siegel erneuern". */
  seal(id: string): Promise<CashRegister> {
    return firstValueFrom(this.http.post<Envelope<CashRegister>>(this.url(`/${id}/seal`), {}).pipe(map((a) => a.data)));
  }

  delete(id: string): Promise<unknown> {
    return firstValueFrom(this.http.delete(this.url(`/${id}`)));
  }

  /** "Filialhistorie": every step, newest first, of one store or of all. */
  history(storeId?: string): Promise<CashRegisterEvent[]> {
    const params = storeId ? new HttpParams().set('storeId', storeId) : undefined;
    return firstValueFrom(this.http.get<Envelope<CashRegisterEvent[]>>(this.url('/history'), { params }).pipe(map((a) => a.data)));
  }
}
