import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideZonelessChangeDetection } from '@angular/core';
import { EditAccountSheetComponent } from './edit-account-sheet.component';
import type { AccountResponse } from '@/api/generated/models/account-response';

const cashAccount: AccountResponse = {
  id: 'cash-1',
  name: 'Chequing',
  accountType: 'Asset',
  kind: 'Cash',
  reportingCurrencyId: 'cad',
  institution: 'Bank A',
  accountNumber: '1234567890',
  isSystem: false,
  isArchived: false,
  createdAt: '',
  updatedAt: '',
  displayValue: 1000,
  nativeBalance: 1000,
  unpricedHoldingCount: 0,
  hasTransactions: true,
};

const otherAccount: AccountResponse = {
  ...cashAccount,
  id: 'cash-2',
  name: 'Savings',
  institution: 'Bank B',
  isArchived: true,
};

describe('EditAccountSheetComponent', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideZonelessChangeDetection(), provideHttpClient()],
    });
  });

  it('seeds form values from account on open', () => {
    const fixture = TestBed.createComponent(EditAccountSheetComponent);
    fixture.componentRef.setInput('state', 'open');
    fixture.componentRef.setInput('account', cashAccount);
    const c = fixture.componentInstance;

    expect(c.editName()).toBe('Chequing');
    expect(c.editInstitution()).toBe('Bank A');
    expect(c.editAccountNumber()).toBe('1234567890');
    expect(c.editArchived()).toBe(false);
  });

  it('reports a change when the account number is edited', () => {
    const fixture = TestBed.createComponent(EditAccountSheetComponent);
    fixture.componentRef.setInput('state', 'open');
    fixture.componentRef.setInput('account', cashAccount);
    const c = fixture.componentInstance;

    expect(c.hasChanges()).toBe(false);
    c.editAccountNumber.set('9999995678');
    expect(c.hasChanges()).toBe(true);
  });

  it('preserves user input when account input re-emits with same id', () => {
    const fixture = TestBed.createComponent(EditAccountSheetComponent);
    fixture.componentRef.setInput('state', 'open');
    fixture.componentRef.setInput('account', cashAccount);
    const c = fixture.componentInstance;

    c.editName.set('Renamed');
    c.editInstitution.set('Bank Z');
    c.editArchived.set(true);

    fixture.componentRef.setInput('account', { ...cashAccount });

    expect(c.editName()).toBe('Renamed');
    expect(c.editInstitution()).toBe('Bank Z');
    expect(c.editArchived()).toBe(true);
  });

  it('reseeds when account switches to a different id', () => {
    const fixture = TestBed.createComponent(EditAccountSheetComponent);
    fixture.componentRef.setInput('state', 'open');
    fixture.componentRef.setInput('account', cashAccount);
    const c = fixture.componentInstance;

    c.editName.set('Renamed');

    fixture.componentRef.setInput('account', otherAccount);

    expect(c.editName()).toBe('Savings');
    expect(c.editInstitution()).toBe('Bank B');
    expect(c.editArchived()).toBe(true);
  });
});
