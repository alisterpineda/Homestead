import { provideZonelessChangeDetection, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { NEVER } from 'rxjs';
import { CreateAccountDialogComponent } from './create-account-dialog.component';
import { FinancesAccountApiService } from '@/api/services/finances-account-api.service';
import { FinanceSettingsService } from '../../shared/finance-settings.service';
import { Router } from '@angular/router';

/* eslint-disable @typescript-eslint/no-explicit-any */
class StubAccountApi {
  lastBody: any = null;
  createAccount = (params: { body: any }) => {
    this.lastBody = params.body;
    return NEVER; // never resolves → success path (toast/navigate) is not reached
  };
}
/* eslint-enable @typescript-eslint/no-explicit-any */

describe('CreateAccountDialogComponent', () => {
  let api: StubAccountApi;

  beforeEach(() => {
    api = new StubAccountApi();
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(),
        { provide: FinancesAccountApiService, useValue: api },
        { provide: FinanceSettingsService, useValue: { displayCurrency: signal(null) } },
        { provide: Router, useValue: { navigate: () => Promise.resolve(true) } },
      ],
    });
  });

  it('includes the trimmed account number in the create request', () => {
    const fixture = TestBed.createComponent(CreateAccountDialogComponent);
    const c = fixture.componentInstance;

    c.kind.set('Cash');
    c.name.set('Chequing');
    c.currencyId.set('cad');
    c.initialAmount.set('0');
    c.accountNumber.set('  1234567890  ');

    c.handleSubmit(new Event('submit'));

    expect(api.lastBody).not.toBeNull();
    expect(api.lastBody.accountNumber).toBe('1234567890');
  });

  it('sends null account number when left blank', () => {
    const fixture = TestBed.createComponent(CreateAccountDialogComponent);
    const c = fixture.componentInstance;

    c.kind.set('Cash');
    c.name.set('Chequing');
    c.currencyId.set('cad');
    c.initialAmount.set('0');
    c.accountNumber.set('   ');

    c.handleSubmit(new Event('submit'));

    expect(api.lastBody.accountNumber).toBeNull();
  });
});
