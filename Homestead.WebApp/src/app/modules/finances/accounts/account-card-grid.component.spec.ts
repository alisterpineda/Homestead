import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideZonelessChangeDetection } from '@angular/core';
import { provideRouter } from '@angular/router';
import { AccountCardGridComponent } from './account-card-grid.component';
import type { AccountResponse } from '@/api/generated/models/account-response';
import type { CommodityResponse } from '@/api/generated/models/commodity-response';

const cad: CommodityResponse = {
  id: 'cad',
  symbol: 'CAD',
  exchange: null,
  name: 'Canadian Dollar',
  kind: 'Currency',
  displayPrecision: 2,
  isSystemManaged: true,
  canDelete: false,
  latestPriceAt: null,
  priceCount: 0,
};

function makeAccount(overrides: Partial<AccountResponse>): AccountResponse {
  return {
    id: overrides.id ?? 'a',
    name: overrides.name ?? 'Account',
    accountType: overrides.accountType ?? 'Asset',
    kind: overrides.kind ?? 'Cash',
    reportingCurrencyId: 'cad',
    institution: overrides.institution ?? null,
    isSystem: false,
    isArchived: overrides.isArchived ?? false,
    createdAt: '2026-01-01T00:00:00Z',
    updatedAt: '2026-01-01T00:00:00Z',
    displayValue: overrides.displayValue ?? null,
    nativeBalance: null,
    unpricedHoldingCount: overrides.unpricedHoldingCount ?? 0,
    hasTransactions: false,
    latestTransactionDate: overrides.latestTransactionDate ?? null,
    ...overrides,
  };
}

describe('AccountCardGridComponent', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(),
        provideRouter([]),
      ],
    });
  });

  function setup(accounts: AccountResponse[], displayCurrency: CommodityResponse | null = cad) {
    const fixture = TestBed.createComponent(AccountCardGridComponent);
    fixture.componentRef.setInput('accounts', accounts);
    fixture.componentRef.setInput('displayCurrency', displayCurrency);
    fixture.detectChanges();
    return fixture;
  }

  it('renders one card per account with name, kind label, and balance', () => {
    const fixture = setup([
      makeAccount({ id: 'a', name: 'Chequing', kind: 'Cash', displayValue: 1234.5, institution: 'Bank of Test' }),
      makeAccount({ id: 'b', name: 'Mortgage', kind: 'Loan', accountType: 'Liability', displayValue: -200000 }),
    ]);
    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Chequing');
    expect(text).toContain('Bank of Test');
    expect(text).toContain('Mortgage');
    expect(text).toContain('Cash');
    expect(text).toContain('Loan');
    expect(text).toContain('1,234.50');
    expect(text).toContain('200,000.00');
  });

  it('renders the Unpriced indicator for investment accounts with unpriced holdings', () => {
    const fixture = setup([
      makeAccount({ id: 'inv', name: 'TFSA', kind: 'Investment', displayValue: null, unpricedHoldingCount: 2 }),
    ]);
    const html = (fixture.nativeElement as HTMLElement).innerHTML;
    expect(html).toContain('Unpriced');
  });

  it('renders an em dash when balance is unresolved with no unpriced holdings', () => {
    const fixture = setup([
      makeAccount({ id: 'a', name: 'No price', displayValue: null, unpricedHoldingCount: 0 }),
    ]);
    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('—');
  });

  it('builds the subtitle from institution and kind label', () => {
    setup([makeAccount({ id: 'a', name: 'Chequing' })]);
    const instance = TestBed.createComponent(AccountCardGridComponent).componentInstance as unknown as {
      subtitle: (a: AccountResponse) => string;
    };
    expect(instance.subtitle(makeAccount({ kind: 'Cash', institution: 'TD' }))).toBe('TD · Cash');
    expect(instance.subtitle(makeAccount({ kind: 'CreditCard', accountType: 'Liability', institution: null }))).toBe('Credit Card');
  });

  it('applies italic + muted styling to archived accounts', () => {
    const fixture = setup([
      makeAccount({ id: 'archived', name: 'Old Account', isArchived: true }),
      makeAccount({ id: 'active', name: 'Active Account', isArchived: false }),
    ]);
    const cards = Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll('a[hlmCard]'),
    );
    const archived = cards.find(c => (c.textContent ?? '').includes('Old Account'))!;
    const active = cards.find(c => (c.textContent ?? '').includes('Active Account'))!;
    expect(archived.className).toContain('italic');
    expect(archived.className).toContain('text-muted-foreground');
    expect(active.className).not.toContain('italic');
  });

  it('links each card to the account detail route', () => {
    const fixture = setup([
      makeAccount({ id: 'abc-123', name: 'Linked' }),
    ]);
    const link = (fixture.nativeElement as HTMLElement).querySelector('a[hlmCard]');
    expect(link?.getAttribute('href')).toBe('/finances/accounts/abc-123');
  });

  it('renders the kebab actions trigger on every card', () => {
    const fixture = setup([
      makeAccount({ id: 'a', name: 'A' }),
      makeAccount({ id: 'b', name: 'B' }),
    ]);
    const triggers = (fixture.nativeElement as HTMLElement)
      .querySelectorAll('button[aria-label="Account actions"]');
    expect(triggers.length).toBe(2);
  });

  it('renders the latest transaction date when present, em dash otherwise', () => {
    const fixture = setup([
      makeAccount({ id: 'a', name: 'Has tx', latestTransactionDate: '2026-04-15' }),
      makeAccount({ id: 'b', name: 'No tx', latestTransactionDate: null }),
    ]);
    const cards = Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll('a[hlmCard]'),
    );
    const hasTx = cards.find(c => (c.textContent ?? '').includes('Has tx'))!;
    const noTx = cards.find(c => (c.textContent ?? '').includes('No tx'))!;
    expect(hasTx.textContent ?? '').toMatch(/Apr 15, 2026/);
    expect(noTx.textContent ?? '').toContain('—');
  });
});
