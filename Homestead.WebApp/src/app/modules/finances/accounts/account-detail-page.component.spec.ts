import { Component, input, output, signal, provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient, HttpErrorResponse } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { of, throwError } from 'rxjs';
import { AccountDetailPageComponent } from './account-detail-page.component';
import { KindBadgeComponent } from '../shared/kind-badge.component';
import { AccountActivityTabComponent } from './account-activity-tab.component';
import { AccountPositionsTabComponent } from './account-positions-tab.component';
import { EditAccountSheetComponent } from './edit-account-sheet/edit-account-sheet.component';
import { BalanceEntrySheetComponent } from './balance-entry-sheet/balance-entry-sheet.component';
import { TransferSheetComponent } from './transfer-sheet/transfer-sheet.component';
import { TradeSheetComponent } from './trade-sheet/trade-sheet.component';
import { DividendSheetComponent } from './dividend-sheet/dividend-sheet.component';
import { DripSheetComponent } from './drip-sheet/drip-sheet.component';
import { OpeningBalanceSheetComponent } from './opening-balance-sheet/opening-balance-sheet.component';
import { AddPositionSheetComponent } from './add-position-sheet/add-position-sheet.component';
import { FinancesAccountApiService } from '@/api/services/finances-account-api.service';
import { FinancesCommodityApiService } from '@/api/services/finances-commodity-api.service';
import { FinancesTransactionApiService } from '@/api/services/finances-transaction-api.service';
import { FinanceSettingsService } from '../shared/finance-settings.service';
import { UserPreferencesService } from '@/shared/services/user-preferences.service';
import type { AccountResponse } from '@/api/generated/models/account-response';

/* eslint-disable @angular-eslint/component-selector, @typescript-eslint/no-explicit-any */
// Light stand-ins so the spec exercises only the detail page's load/error logic, not the children's
// data fetching. Same selectors + the exact bindings the page template uses.
@Component({ selector: 'app-kind-badge', template: '' })
class StubKindBadge { kind = input<any>(); }
@Component({ selector: 'app-account-positions-tab', template: '' })
class StubPositionsTab {
  account = input<any>(); refreshKey = input<any>();
  editOpeningBalance = output<any>(); heldCommodityIdsChange = output<any>(); heldCurrencyIdsChange = output<any>();
}
@Component({ selector: 'app-account-activity-tab', template: '' })
class StubActivityTab {
  account = input<any>(); refreshKey = input<any>(); transactionClicked = output<any>();
}
@Component({ selector: 'app-edit-account-sheet', template: '' })
class StubEditSheet { account = input<any>(); state = input<any>(); saved = output<any>(); closed = output<void>(); }
@Component({ selector: 'app-balance-entry-sheet', template: '' })
class StubBalanceSheet { account = input<any>(); state = input<any>(); mode = input<any>(); existing = input<any>(); saved = output<any>(); deleted = output<any>(); closed = output<void>(); }
@Component({ selector: 'app-transfer-sheet', template: '' })
class StubTransferSheet { account = input<any>(); state = input<any>(); mode = input<any>(); existing = input<any>(); saved = output<any>(); deleted = output<any>(); closed = output<void>(); }
@Component({ selector: 'app-trade-sheet', template: '' })
class StubTradeSheet { account = input<any>(); state = input<any>(); mode = input<any>(); side = input<any>(); existing = input<any>(); showFundingCcy = input<any>(); saved = output<any>(); deleted = output<any>(); closed = output<void>(); }
@Component({ selector: 'app-dividend-sheet', template: '' })
class StubDividendSheet { account = input<any>(); state = input<any>(); mode = input<any>(); existing = input<any>(); saved = output<any>(); deleted = output<any>(); closed = output<void>(); }
@Component({ selector: 'app-drip-sheet', template: '' })
class StubDripSheet { account = input<any>(); state = input<any>(); mode = input<any>(); existing = input<any>(); saved = output<any>(); deleted = output<any>(); closed = output<void>(); }
@Component({ selector: 'app-opening-balance-sheet', template: '' })
class StubOpeningBalanceSheet { account = input<any>(); state = input<any>(); mode = input<any>(); existing = input<any>(); saved = output<any>(); deleted = output<any>(); closed = output<void>(); }
@Component({ selector: 'app-add-position-sheet', template: '' })
class StubAddPositionSheet { account = input<any>(); state = input<any>(); excludedCommodityIds = input<any>(); saved = output<any>(); closed = output<void>(); }
/* eslint-enable @angular-eslint/component-selector, @typescript-eslint/no-explicit-any */

const ACCOUNT: AccountResponse = {
  id: 'acc1',
  name: 'Checking',
  kind: 'Cash',
  accountType: 'Asset',
  reportingCurrencyId: 'usd',
  balance: 1000,
  isArchived: false,
} as unknown as AccountResponse;

class StubAccountApi {
  account: AccountResponse = ACCOUNT;
  getError = false;
  getErrorStatus = 404; // 404 → not-found; anything else → retryable loadError
  getCalls = 0;
  getAccount = (_p: { id: string }) => {
    this.getCalls++;
    return this.getError
      ? throwError(() => new HttpErrorResponse({ status: this.getErrorStatus }))
      : of(this.account);
  };
}

describe('AccountDetailPageComponent', () => {
  let api: StubAccountApi;

  beforeEach(() => {
    api = new StubAccountApi();
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([
          { path: 'finances/accounts/:id', component: AccountDetailPageComponent, data: { title: 'Account' } },
        ]),
        { provide: FinancesAccountApiService, useValue: api },
        { provide: FinancesCommodityApiService, useValue: { getCommodity: () => of(null) } },
        { provide: FinancesTransactionApiService, useValue: { getOpeningBalance: () => throwError(() => new HttpErrorResponse({ status: 404 })) } },
        { provide: FinanceSettingsService, useValue: { displayCurrency: signal(null) } },
        { provide: UserPreferencesService, useValue: { financesPrivacyMasked: signal(false) } },
      ],
    });
    TestBed.overrideComponent(AccountDetailPageComponent, {
      remove: {
        imports: [
          KindBadgeComponent, AccountActivityTabComponent, AccountPositionsTabComponent,
          EditAccountSheetComponent, BalanceEntrySheetComponent, TransferSheetComponent,
          TradeSheetComponent, DividendSheetComponent, DripSheetComponent,
          OpeningBalanceSheetComponent, AddPositionSheetComponent,
        ],
      },
      add: {
        imports: [
          StubKindBadge, StubActivityTab, StubPositionsTab,
          StubEditSheet, StubBalanceSheet, StubTransferSheet, StubTradeSheet,
          StubDividendSheet, StubDripSheet, StubOpeningBalanceSheet, StubAddPositionSheet,
        ],
      },
    });
  });

  async function navigate(url: string): Promise<{ harness: RouterTestingHarness; component: AccountDetailPageComponent }> {
    const harness = await RouterTestingHarness.create();
    const component = await harness.navigateByUrl(url, AccountDetailPageComponent);
    harness.detectChanges();
    await harness.fixture.whenStable();
    harness.detectChanges();
    return { harness, component };
  }

  function text(harness: RouterTestingHarness): string {
    return (harness.routeNativeElement as HTMLElement).textContent ?? '';
  }

  function buttons(harness: RouterTestingHarness): HTMLButtonElement[] {
    return Array.from((harness.routeNativeElement as HTMLElement).querySelectorAll<HTMLButtonElement>('button'));
  }

  it('renders the account when loaded', async () => {
    const { harness } = await navigate('/finances/accounts/acc1');
    expect(text(harness)).toContain('Checking');
  });

  it('shows the not-found state on a 404', async () => {
    api.getError = true;
    api.getErrorStatus = 404;
    const { harness, component } = await navigate('/finances/accounts/missing');

    expect(component.notFound()).toBe(true);
    expect(component.loadError()).toBe(false);
    expect(text(harness)).toContain('Account not found');
  });

  it('shows a retryable error state (not "not found") on a non-404 load failure, and Retry re-fetches', async () => {
    api.getError = true;
    api.getErrorStatus = 500;
    const { harness, component } = await navigate('/finances/accounts/acc1');

    expect(component.notFound()).toBe(false);
    expect(component.loadError()).toBe(true);
    expect(text(harness)).toContain("Couldn't load account");
    expect(text(harness)).not.toContain('Account not found');

    api.getError = false;
    buttons(harness).find(b => b.textContent?.trim() === 'Retry')!.click();
    harness.detectChanges();
    await harness.fixture.whenStable();
    harness.detectChanges();
    expect(api.getCalls).toBe(2);
    expect(text(harness)).toContain('Checking');
    expect(component.loadError()).toBe(false);
  });

  it('keeps the rendered page and shows a banner when a refetch fails (non-404)', async () => {
    const { harness, component } = await navigate('/finances/accounts/acc1');
    expect(text(harness)).toContain('Checking');

    api.getError = true;
    api.getErrorStatus = 500;
    component.retry(); // bump refreshTrigger → refetch fails transiently
    harness.detectChanges();
    await harness.fixture.whenStable();
    harness.detectChanges();

    expect(component.account()).not.toBeNull();
    expect(text(harness)).toContain('Checking'); // last-loaded page preserved
    expect(component.loadError()).toBe(true);
    expect(text(harness)).toContain("Couldn't refresh");
    expect(text(harness)).not.toContain('Account not found');
  });
});
