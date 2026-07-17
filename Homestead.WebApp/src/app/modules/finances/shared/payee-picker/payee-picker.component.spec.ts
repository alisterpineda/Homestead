import { TestBed, ComponentFixture } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { of } from 'rxjs';
import { PayeePickerComponent } from './payee-picker.component';
import { FinancesPayeeApiService } from '@/api/services/finances-payee-api.service';
import type { PayeeResponse } from '@/api/generated/models/payee-response';

describe('PayeePickerComponent', () => {
  let getPayees: ReturnType<typeof vi.fn>;
  let createPayee: ReturnType<typeof vi.fn>;

  function setup(): PayeePickerComponent {
    const fixture = TestBed.createComponent(PayeePickerComponent);
    fixture.detectChanges();
    return fixture.componentInstance;
  }

  function setupFixture(): ComponentFixture<PayeePickerComponent> {
    const fixture = TestBed.createComponent(PayeePickerComponent);
    fixture.detectChanges();
    return fixture;
  }

  const payee = (id: string, name: string): PayeeResponse => ({
    id, name, isArchived: false, transactionCount: 0,
    createdAt: '2026-01-01T00:00:00Z', updatedAt: '2026-01-01T00:00:00Z',
  });

  beforeEach(() => {
    getPayees = vi.fn().mockReturnValue(of({ items: [], totalCount: 0 }));
    createPayee = vi.fn().mockReturnValue(of(payee('payee-1', 'Costco')));
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(),
        { provide: FinancesPayeeApiService, useValue: { getPayees, createPayee } },
      ],
    });
  });

  it('creates', () => {
    expect(setup()).toBeTruthy();
  });

  it('offers the create row for a non-empty search with no matching results', () => {
    const c = setup();
    expect(c.showCreateRow()).toBe(false); // empty search
    c.searchTerm.set('Costco');
    expect(c.showCreateRow()).toBe(true);
  });

  it('create flow: calls createPayee and emits the created payee', () => {
    const c = setup();
    const emitted: (PayeeResponse | null)[] = [];
    c.valueChange.subscribe(v => emitted.push(v));

    c.searchTerm.set('  Costco  ');
    c.onCreate();

    expect(createPayee).toHaveBeenCalledTimes(1);
    expect(createPayee.mock.calls[0][0]).toEqual({ body: { name: 'Costco' } });
    expect(emitted).toEqual([payee('payee-1', 'Costco')]);
    // Search resets and the picker isn't stuck in the creating state.
    expect(c.searchTerm()).toBe('');
    expect(c.creating()).toBe(false);
  });

  it('onSelect emits the full picked payee and closes', () => {
    const c = setup();
    const emitted: (PayeeResponse | null)[] = [];
    c.valueChange.subscribe(v => emitted.push(v));

    const p = payee('p2', 'Safeway');
    c.onSelect(p);

    expect(emitted).toEqual([p]);
    expect(c.popoverState()).toBe('closed');
  });

  it('onClear emits null (unattributed)', () => {
    const c = setup();
    const emitted: (PayeeResponse | null)[] = [];
    c.valueChange.subscribe(v => emitted.push(v));

    c.onClear();

    expect(emitted).toEqual([null]);
  });

  it('does not create when the search is blank', () => {
    const c = setup();
    c.searchTerm.set('   ');
    c.onCreate();
    expect(createPayee).not.toHaveBeenCalled();
  });

  it('surfaces search results (archived excluded server-side by default)', async () => {
    vi.useFakeTimers();
    try {
      getPayees.mockReturnValue(of({ items: [payee('p1', 'Costco'), payee('p2', 'Safeway')], totalCount: 2 }));
      const fixture = setupFixture();
      const c = fixture.componentInstance;
      c.searchTerm.set('Co');
      // Drain the 200ms search debounce so the results pipeline resolves.
      await vi.advanceTimersByTimeAsync(250);

      expect(c.results().map(p => p.id)).toEqual(['p1', 'p2']);
      // No IncludeArchived flag is passed — the server default hides archived payees.
      expect(getPayees.mock.calls.at(-1)?.[0]).not.toHaveProperty('IncludeArchived');
    } finally {
      vi.useRealTimers();
    }
  });
});
