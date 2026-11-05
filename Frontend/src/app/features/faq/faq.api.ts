import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom, map } from 'rxjs';
import { ConfigService } from '../../core/config';
import { Faq, FaqAttachment, FaqImage, FaqImageStatus, FaqImageUpload } from './faq-models';

/** The portal wraps answers as `{success, data}`; this unwraps them. Errors are left to the caller. */
interface Envelope<T> {
  success: boolean;
  data: T;
}

@Injectable({ providedIn: 'root' })
export class FaqApi {
  private readonly http = inject(HttpClient);
  private readonly config = inject(ConfigService);

  private url(path: string): string {
    return this.config.url('portal', path);
  }

  /** The active entries in the order set by the Personalabteilung (public). */
  list(): Promise<Faq[]> {
    return firstValueFrom(this.http.get<Envelope<Faq[]>>(this.url('/api/faqs')).pipe(map((a) => a.data)));
  }

  listArchived(): Promise<Faq[]> {
    return firstValueFrom(this.http.get<Envelope<Faq[]>>(this.url('/api/faqs/archived')).pipe(map((a) => a.data)));
  }

  create(title: string, description: string): Promise<Faq> {
    return firstValueFrom(this.http.post<Envelope<Faq>>(this.url('/api/faqs'), { title, description }).pipe(map((a) => a.data)));
  }

  update(id: string, body: { title?: string; description?: string; archived?: boolean }): Promise<Faq> {
    return firstValueFrom(this.http.patch<Envelope<Faq>>(this.url(`/api/faqs/${id}`), body).pipe(map((a) => a.data)));
  }

  /** The new order: all active entries, exactly once each. Answers with the active entries in the new order. */
  reorder(orderedIds: string[]): Promise<Faq[]> {
    return firstValueFrom(this.http.patch<Envelope<Faq[]>>(this.url('/api/faqs/reorder'), { orderedIds }).pipe(map((a) => a.data)));
  }

  /** Deletes an entry for good (archiving is `update` with `archived: true`). */
  async remove(id: string): Promise<void> {
    await firstValueFrom(this.http.delete(this.url(`/api/faqs/${id}`)));
  }

  /** Attaches a file to an entry (needs faq:update); the portal checks type and size on the content. */
  uploadAttachment(faqId: string, file: File): Promise<FaqAttachment> {
    const form = new FormData();
    form.append('file', file);
    return firstValueFrom(this.http.post<Envelope<FaqAttachment>>(this.url(`/api/faqs/${faqId}/attachments`), form).pipe(map((a) => a.data)));
  }

  async deleteAttachment(attachment: FaqAttachment, faqId: string): Promise<void> {
    await firstValueFrom(this.http.delete(this.url(`/api/faqs/${faqId}/attachments/${attachment.id}`)));
  }

  /** The file itself; it needs the sign-in, so it comes as data and is opened from there (no address with a token). */
  downloadAttachment(attachment: Pick<FaqAttachment, 'url'>): Promise<Blob> {
    return firstValueFrom(this.http.get(this.url(attachment.url), { responseType: 'blob' }));
  }

  uploadImage(file: File): Promise<FaqImageUpload> {
    const form = new FormData();
    form.append('file', file);
    return firstValueFrom(this.http.post<Envelope<FaqImageUpload>>(this.url('/api/admin/faq/images'), form).pipe(map((a) => a.data)));
  }

  listImages(status?: FaqImageStatus): Promise<FaqImage[]> {
    const params = status ? { status } : undefined;
    return firstValueFrom(this.http.get<Envelope<FaqImage[]>>(this.url('/api/admin/faq/images'), { params }).pipe(map((a) => a.data)));
  }

  async deleteImage(id: string): Promise<void> {
    await firstValueFrom(this.http.delete(this.url(`/api/admin/faq/images/${id}`)));
  }
}
