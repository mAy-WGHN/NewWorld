import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom, map } from 'rxjs';
import { ConfigService } from '../../core/config';
import {
  Assignment,
  CatalogEntry,
  CreateAssignmentRequest,
  CreateResponsibilityRequest,
  MainStore,
  Person,
  Role,
  StoreGroup,
  StoreOption,
  TopicResponsibility,
} from './admin-access-models';

/** The administration of the ACL in Access (every call needs its privilege). Errors are left to the caller; see `accessProblemMessage`. */
@Injectable({ providedIn: 'root' })
export class AdminAccessApi {
  private readonly http = inject(HttpClient);
  private readonly config = inject(ConfigService);

  private url(path: string): string {
    return this.config.url('access', `/api${path}`);
  }

  // roles
  roles(): Promise<Role[]> {
    return firstValueFrom(this.http.get<Role[]>(this.url('/roles')));
  }

  createRole(name: string, privileges: string[]): Promise<{ id: string }> {
    return firstValueFrom(this.http.post<{ id: string }>(this.url('/roles'), { name, privileges }));
  }

  updateRole(id: string, privileges: string[]): Promise<unknown> {
    return firstValueFrom(this.http.put(this.url(`/roles/${id}`), { privileges }));
  }

  deleteRole(id: string): Promise<unknown> {
    return firstValueFrom(this.http.delete(this.url(`/roles/${id}`)));
  }

  // catalogue and own privileges
  catalog(): Promise<CatalogEntry[]> {
    return firstValueFrom(this.http.get<CatalogEntry[]>(this.url('/catalog')));
  }

  createCustomPrivilege(key: string, scopable: boolean, description: string | null): Promise<unknown> {
    return firstValueFrom(this.http.post(this.url('/custom-privileges'), { key, scopable, description }));
  }

  deleteCustomPrivilege(key: string): Promise<unknown> {
    return firstValueFrom(this.http.delete(this.url(`/custom-privileges/${encodeURIComponent(key)}`)));
  }

  // store groups ("scopes")
  scopes(): Promise<StoreGroup[]> {
    return firstValueFrom(this.http.get<StoreGroup[]>(this.url('/scopes')));
  }

  createScope(name: string, storeIds: string[]): Promise<{ id: string }> {
    return firstValueFrom(this.http.post<{ id: string }>(this.url('/scopes'), { name, storeIds }));
  }

  updateScope(id: string, storeIds: string[]): Promise<unknown> {
    return firstValueFrom(this.http.put(this.url(`/scopes/${id}`), { storeIds }));
  }

  deleteScope(id: string): Promise<unknown> {
    return firstValueFrom(this.http.delete(this.url(`/scopes/${id}`)));
  }

  // assignments
  assignments(employeeId: string): Promise<Assignment[]> {
    return firstValueFrom(this.http.get<Assignment[]>(this.url(`/people/${employeeId}/assignments`)));
  }

  assign(employeeId: string, request: CreateAssignmentRequest): Promise<{ id: string }> {
    return firstValueFrom(this.http.post<{ id: string }>(this.url(`/people/${employeeId}/assignments`), request));
  }

  removeAssignment(id: string): Promise<unknown> {
    return firstValueFrom(this.http.delete(this.url(`/assignments/${id}`)));
  }

  // responsibilities
  responsibilities(employeeId: string): Promise<TopicResponsibility[]> {
    return firstValueFrom(this.http.get<TopicResponsibility[]>(this.url(`/people/${employeeId}/responsibilities`)));
  }

  addResponsibility(request: CreateResponsibilityRequest): Promise<{ id: string }> {
    return firstValueFrom(this.http.post<{ id: string }>(this.url('/responsibilities'), request));
  }

  removeResponsibility(id: string): Promise<unknown> {
    return firstValueFrom(this.http.delete(this.url(`/responsibilities/${id}`)));
  }

  // finding people by name or personnel number (only people in the caller's reach)
  searchPeople(query: string, take = 20): Promise<Person[]> {
    return firstValueFrom(this.http.get<Person[]>(this.url('/people'), { params: { query, take } }));
  }

  /** The people for known ids (to show names instead of ids); unknown ids and people out of reach are missing in the answer. */
  peopleByIds(ids: readonly string[]): Promise<Person[]> {
    return ids.length === 0 ? Promise.resolve([]) : firstValueFrom(this.http.get<Person[]>(this.url('/people'), { params: { ids: ids.join(',') } }));
  }

  // main store
  mainStore(employeeId: string): Promise<MainStore> {
    return firstValueFrom(this.http.get<MainStore>(this.url(`/people/${employeeId}/main-store`)));
  }

  setMainStore(employeeId: string, storeId: string): Promise<unknown> {
    return firstValueFrom(this.http.put(this.url(`/people/${employeeId}/main-store`), { storeId }));
  }

  clearMainStore(employeeId: string): Promise<unknown> {
    return firstValueFrom(this.http.delete(this.url(`/people/${employeeId}/main-store`)));
  }

  /** The stores of the portal for names and choices (Portal, no Access call). */
  stores(): Promise<StoreOption[]> {
    return firstValueFrom(
      this.http
        .get<{ data: { items: StoreOption[] } }>(this.config.url('portal', '/api/locations'))
        .pipe(map((answer) => answer.data.items)),
    );
  }
}
