import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideZonelessChangeDetection } from '@angular/core';
import { provideRouter } from '@angular/router';
import { AccountTableComponent } from './account-table.component';
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

describe('AccountTableComponent', () => {
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
    const fixture = TestBed.createComponent(AccountTableComponent);
    fixture.componentRef.setInput('accounts', accounts);
    fixture.componentRef.setInput('displayCurrency', displayCurrency);
    fixture.detectChanges();
    return fixture;
  }

  it('renders rows for each account with name, kind label, and balance', () => {
    const fixture = setup([
      makeAccount({ id: 'a', name: 'Chequing', kind: 'Cash', displayValue: 1234.5 }),
      makeAccount({ id: 'b', name: 'Mortgage', kind: 'Loan', accountType: 'Liability', displayValue: -200000 }),
    ]);
    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Chequing');
    expect(text).toContain('Mortgage');
    expect(text).toContain('Cash');
    expect(text).toContain('Loan');
    expect(text).toContain('1,234.50');
    expect(text).toContain('200,000.00');
  });

  it('renders the Unpriced indicator when displayValue is null and there are unpriced holdings', () => {
    const fixture = setup([
      makeAccount({ id: 'inv', name: 'TFSA', kind: 'Investment', displayValue: null, unpricedHoldingCount: 2 }),
    ]);
    const html = (fixture.nativeElement as HTMLElement).innerHTML;
    expect(html).toContain('Unpriced');
  });

  it('default-sorts rows alphabetically by name', () => {
    const fixture = setup([
      makeAccount({ id: 'z', name: 'Zebra' }),
      makeAccount({ id: 'a', name: 'Alpha' }),
      makeAccount({ id: 'm', name: 'Mike' }),
    ]);
    const rowOrder = Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll('tbody tr'),
    ).map(tr => tr.textContent ?? '');
    expect(rowOrder[0]).toContain('Alpha');
    expect(rowOrder[1]).toContain('Mike');
    expect(rowOrder[2]).toContain('Zebra');
  });

  it('sorts by total balance with nulls last', () => {
    const fixture = setup([
      makeAccount({ id: 'a', name: 'A-Account', displayValue: null, unpricedHoldingCount: 1 }),
      makeAccount({ id: 'b', name: 'B-Account', displayValue: 50 }),
      makeAccount({ id: 'c', name: 'C-Account', displayValue: 1000 }),
    ]);
    const table = fixture.componentInstance['table'];
    table.getColumn('displayValue')!.toggleSorting(false);
    fixture.detectChanges();
    const order = Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll('tbody tr'),
    ).map(tr => tr.textContent ?? '');
    expect(order[0]).toContain('B-Account');
    expect(order[1]).toContain('C-Account');
    expect(order[2]).toContain('A-Account');
  });

  it('applies the archived styling class to archived rows', () => {
    const fixture = setup([
      makeAccount({ id: 'archived', name: 'Old Account', isArchived: true }),
      makeAccount({ id: 'active', name: 'Active Account', isArchived: false }),
    ]);
    const rows = Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll('tbody tr'),
    );
    const archivedRow = rows.find(tr => (tr.textContent ?? '').includes('Old Account'))!;
    const activeRow = rows.find(tr => (tr.textContent ?? '').includes('Active Account'))!;
    expect(archivedRow.className).toContain('italic');
    expect(archivedRow.className).toContain('text-muted-foreground');
    expect(activeRow.className).not.toContain('italic');
  });
});
