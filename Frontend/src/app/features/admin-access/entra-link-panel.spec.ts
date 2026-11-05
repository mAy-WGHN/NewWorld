import { HttpErrorResponse } from '@angular/common/http';
import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { AuthService } from '../../core/auth/auth.service';
import { PrivilegeSet } from '../../core/auth/privileges';
import { AdminAccessApi } from './admin-access.api';
import { EntraLink, validateUserPrincipalName } from './admin-access-models';
import { EntraLinkPanel } from './entra-link-panel';

const grant = (...keys: string[]) => new PrivilegeSet(keys.map((key) => ({ key, unrestricted: true, storeIds: [], employeeIds: [] })));
const ALL = ['access:person:read', 'access:entra-link:manage'];
const PERSON = '3f2504e0-4f89-11d3-9a0c-0305e82c3301';

describe('EntraLinkPanel', () => {
  let api: Record<'entraLink' | 'setEntraLink' | 'clearEntraLink', ReturnType<typeof vi.fn>>;
  const privileges = signal(grant(...ALL));
  let fixture: ComponentFixture<EntraLinkPanel>;
  let root: HTMLElement;

  const link = (over: Partial<EntraLink> = {}): EntraLink => ({ userPrincipalName: 'erika@firma.de', bound: false, boundAt: null, ...over });

  async function create(): Promise<void> {
    TestBed.configureTestingModule({ providers: [{ provide: AdminAccessApi, useValue: api }, { provide: AuthService, useValue: { privileges } }] });
    fixture = TestBed.createComponent(EntraLinkPanel);
    fixture.componentRef.setInput('personId', PERSON);
    root = fixture.nativeElement as HTMLElement;
    await settle();
  }

  async function settle(): Promise<void> {
    for (let i = 0; i < 4; i++) {
      await fixture.whenStable();
      await new Promise((r) => setTimeout(r));
    }
  }

  const q = <T extends Element = HTMLElement>(selector: string): T | null => root.querySelector<T>(selector);

  async function click(selector: string): Promise<void> {
    q<HTMLElement>(selector)!.click();
    await settle();
  }

  async function type(value: string): Promise<void> {
    const input = q<HTMLInputElement>('[data-testid="upn-input"]')!;
    input.value = value;
    input.dispatchEvent(new Event('input', { bubbles: true }));
    await settle();
  }

  beforeEach(() => {
    privileges.set(grant(...ALL));
    api = {
      entraLink: vi.fn().mockResolvedValue(link()),
      setEntraLink: vi.fn().mockResolvedValue(undefined),
      clearEntraLink: vi.fn().mockResolvedValue(undefined),
    };
  });

  it('shows the account and that the person has not confirmed yet', async () => {
    await create();

    expect(q('[data-testid="upn"]')!.textContent).toContain('erika@firma.de');
    expect(q('[data-testid="bound"]')!.textContent).toContain('Noch nicht');
  });

  it('shows that the account is bound after the first confirmation', async () => {
    api['entraLink'].mockResolvedValue(link({ bound: true, boundAt: '2026-11-05T10:00:00Z' }));
    await create();

    expect(q('[data-testid="bound"]')!.textContent).toContain('fest zugeordnet');
  });

  it('says that nothing is linked yet when Access does not know a link', async () => {
    api['entraLink'].mockRejectedValue(new HttpErrorResponse({ status: 404 }));
    await create();

    expect(q('[data-testid="no-link"]')).not.toBeNull();
    expect(q('[data-testid="load-error"]')).toBeNull();
  });

  it('enters the account and reloads it', async () => {
    await create();

    await type('  erika@firma.de ');
    await click('[data-action="save"]');

    expect(api['setEntraLink']).toHaveBeenCalledWith(PERSON, 'erika@firma.de');
    expect(q('[data-testid="status"]')!.textContent).toContain('eingetragen');
  });

  it('refuses an empty or malformed address without calling the service', async () => {
    await create();

    await click('[data-action="save"]');
    expect(q('[data-testid="form-error"]')!.textContent).toContain('Bitte');
    await type('keine-adresse');
    await click('[data-action="save"]');

    expect(q('[data-testid="form-error"]')!.textContent).toContain('gültig');
    expect(api['setEntraLink']).not.toHaveBeenCalled();
  });

  it('removes the link only after a confirmation', async () => {
    await create();

    await click('[data-action="ask-remove"]');
    expect(api['clearEntraLink']).not.toHaveBeenCalled();
    await click('[data-action="remove"]');

    expect(api['clearEntraLink']).toHaveBeenCalledWith(PERSON);
    expect(q('[data-testid="no-link"]')).not.toBeNull();
  });

  it('offers no change without the right to manage, and nothing to look at without the right to read', async () => {
    privileges.set(grant('access:person:read'));
    await create();
    expect(q('[data-testid="set-panel"]')).toBeNull();
    expect(q('[data-testid="upn"]')).not.toBeNull();

    TestBed.resetTestingModule();
    privileges.set(grant('access:entra-link:manage'));
    await create();

    expect(q('[data-testid="no-read"]')).not.toBeNull();
    expect(api['entraLink']).toHaveBeenCalledTimes(1);
    expect(q('[data-testid="set-panel"]')).not.toBeNull();
  });

  it('says plainly when the service refuses to show the account', async () => {
    api['entraLink'].mockRejectedValue(new HttpErrorResponse({ status: 403 }));
    await create();

    expect(q('[data-testid="load-error"]')!.textContent).toContain('nicht sehen');
  });
});

describe('validateUserPrincipalName', () => {
  it('accepts an e-mail address', () => {
    expect(validateUserPrincipalName('erika.mustermann@wintergerst-gruppe.de')).toBeNull();
  });

  it('refuses empty, without domain and with spaces', () => {
    for (const bad of ['', '   ', 'erika', 'erika@', '@firma.de', 'er ika@firma.de', 'erika@firma']) {
      expect(validateUserPrincipalName(bad)).not.toBeNull();
    }
  });
});
