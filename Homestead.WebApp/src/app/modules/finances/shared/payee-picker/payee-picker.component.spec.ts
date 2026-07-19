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

  it('offers a dedupe nudge for a case-insensitive near-duplicate', async () => {
    vi.useFakeTimers();
    try {
      getPayees.mockReturnValue(of({ items: [payee('p1', 'Blue Bottle Coffee')], totalCount: 1 }));
      const fixture = setupFixture();
      const c = fixture.componentInstance;
      // The fetch is gated on the picker being open — that's the real precondition now.
      c.popoverState.set('open');
      // Typed name is a substring of an existing payee → nudge, but still offer create (not an exact match).
      c.searchTerm.set('blue bottle');
      await vi.advanceTimersByTimeAsync(250);

      expect(c.showCreateRow()).toBe(true);
      expect(c.didYouMean()?.id).toBe('p1');
    } finally {
      vi.useRealTimers();
    }
  });

  it('does not nudge when the typed name exactly matches an existing payee', async () => {
    vi.useFakeTimers();
    try {
      getPayees.mockReturnValue(of({ items: [payee('p1', 'Costco')], totalCount: 1 }));
      const fixture = setupFixture();
      const c = fixture.componentInstance;
      c.popoverState.set('open');
      c.searchTerm.set('Costco');
      await vi.advanceTimersByTimeAsync(250);

      // Exact match → no create row, hence no nudge.
      expect(c.showCreateRow()).toBe(false);
      expect(c.didYouMean()).toBeNull();
    } finally {
      vi.useRealTimers();
    }
  });

  it('surfaces search results (archived excluded server-side by default)', async () => {
    vi.useFakeTimers();
    try {
      getPayees.mockReturnValue(of({ items: [payee('p1', 'Costco'), payee('p2', 'Safeway')], totalCount: 2 }));
      const fixture = setupFixture();
      const c = fixture.componentInstance;
      c.popoverState.set('open');
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

  // ─── Input-first combobox behavior ──────────────────────────────────────────

  // Reach the protected/private members the input-first control exposes only to its own template.
  type Internals = {
    openPicker(): void;
    onChevron(): void;
    onKeydown(e: KeyboardEvent): void;
    activeIndex(): number;
  };
  const internals = (c: PayeePickerComponent): Internals => c as unknown as Internals;

  it('resolves the underlying popover directive so it can be repositioned while scrolling', () => {
    const c = setup();
    // The scroll-follow logic no-ops silently if this query fails to match the host directive.
    expect((c as unknown as { popover(): unknown }).popover()).toBeTruthy();
  });

  it('displayText shows the committed name when closed and the live query when open', () => {
    const fixture = setupFixture();
    const c = fixture.componentInstance;
    fixture.componentRef.setInput('value', { id: 'p1', name: 'Costco' });

    expect(c.displayText()).toBe('Costco'); // closed → committed name
    c.popoverState.set('open');
    c.searchTerm.set('Cos');
    expect(c.displayText()).toBe('Cos');    // open → live query
  });

  it('openPicker seeds the query with the current name and opens', () => {
    const fixture = setupFixture();
    const c = fixture.componentInstance;
    fixture.componentRef.setInput('value', { id: 'p1', name: 'Costco' });

    internals(c).openPicker();

    expect(c.popoverState()).toBe('open');
    expect(c.searchTerm()).toBe('Costco');
  });

  it('autoOpen mounts the picker open and seeded with initialQuery (override entry point)', async () => {
    const fixture = TestBed.createComponent(PayeePickerComponent);
    fixture.componentRef.setInput('autoOpen', true);
    fixture.componentRef.setInput('initialQuery', 'Blue Bottle Coffee');
    fixture.detectChanges();
    await fixture.whenStable();
    const c = fixture.componentInstance;

    expect(c.popoverState()).toBe('open');
    expect(c.searchTerm()).toBe('Blue Bottle Coffee');
  });

  it('does not auto-open for a normal (autoOpen=false) host', async () => {
    const fixture = setupFixture();
    await fixture.whenStable();
    expect(fixture.componentInstance.popoverState()).toBe('closed');
  });

  it('emits (closed) when the panel is dismissed without a pick', () => {
    const fixture = setupFixture();
    const c = fixture.componentInstance;
    let closedCount = 0;
    c.closed.subscribe(() => closedCount++);

    c.popoverState.set('open');
    internals(c).onKeydown(new KeyboardEvent('keydown', { key: 'Escape' }));

    expect(closedCount).toBeGreaterThanOrEqual(1);
    expect(c.popoverState()).toBe('closed');
  });

  it('gates the fetch on being open: closed never fetches, open does', async () => {
    vi.useFakeTimers();
    try {
      const fixture = setupFixture();
      const c = fixture.componentInstance;

      // Closed → the search source is null, nothing downstream fires.
      c.searchTerm.set('Costco');
      await vi.advanceTimersByTimeAsync(250);
      expect(getPayees).not.toHaveBeenCalled();

      // Open → the same term now drives a fetch.
      c.popoverState.set('open');
      c.searchTerm.set('Costco2');
      await vi.advanceTimersByTimeAsync(250);
      expect(getPayees).toHaveBeenCalled();
    } finally {
      vi.useRealTimers();
    }
  });

  it('a bare focus does not open the picker (so restore-focus after an outside close cannot reopen it); a click does', () => {
    const fixture = setupFixture();
    const c = fixture.componentInstance;
    const input = (fixture.nativeElement as HTMLElement).querySelector('input')!;

    // The overlay refocuses this input when it closes — that focus must not reopen the picker.
    input.dispatchEvent(new FocusEvent('focus'));
    fixture.detectChanges();
    expect(c.popoverState()).toBe('closed');

    // A genuine tap/click still opens it.
    input.dispatchEvent(new MouseEvent('click'));
    fixture.detectChanges();
    expect(c.popoverState()).toBe('open');
  });

  it('the chevron toggles the picker open even when the input already holds focus', () => {
    // Post-select/clear/Escape state: closed but focused, so re-focusing fires no focus event. The chevron
    // must open the picker directly rather than leaning on onFocus.
    const fixture = setupFixture();
    const c = fixture.componentInstance;
    expect(c.popoverState()).toBe('closed');

    internals(c).onChevron();
    expect(c.popoverState()).toBe('open');

    internals(c).onChevron();
    expect(c.popoverState()).toBe('closed');
  });

  it('reopening after a close re-resolves the loading spinner (no stuck spinner on the same term)', async () => {
    vi.useFakeTimers();
    try {
      getPayees.mockReturnValue(of({ items: [payee('p1', 'Costco')], totalCount: 1 }));
      const fixture = setupFixture();
      const c = fixture.componentInstance;

      // First open: the fetch resolves and clears loading.
      internals(c).openPicker();
      await vi.advanceTimersByTimeAsync(250);
      expect(c.loading()).toBe(false);
      expect(c.results().length).toBe(1);

      // Close (the gated source emits null; give it a tick to flow through).
      c.popoverState.set('closed');
      c.searchTerm.set('');
      await vi.advanceTimersByTimeAsync(250);

      // Reopen with the identical (empty) term: must fetch again and clear the eager spinner.
      internals(c).openPicker();
      expect(c.loading()).toBe(true);
      await vi.advanceTimersByTimeAsync(250);
      expect(c.loading()).toBe(false); // was stuck true when distinctUntilChanged suppressed the refetch
      expect(getPayees.mock.calls.length).toBeGreaterThanOrEqual(2);
    } finally {
      vi.useRealTimers();
    }
  });

  it('Escape closes the picker and resets the query', () => {
    const fixture = setupFixture();
    const c = fixture.componentInstance;
    c.popoverState.set('open');
    c.searchTerm.set('typing');

    internals(c).onKeydown(new KeyboardEvent('keydown', { key: 'Escape' }));

    expect(c.popoverState()).toBe('closed');
    expect(c.searchTerm()).toBe('');
  });

  it('ArrowDown advances the keyboard highlight', async () => {
    vi.useFakeTimers();
    try {
      getPayees.mockReturnValue(of({ items: [payee('p1', 'Costco'), payee('p2', 'Safeway')], totalCount: 2 }));
      const fixture = setupFixture();
      const c = fixture.componentInstance;
      c.popoverState.set('open');
      c.searchTerm.set('Co');
      await vi.advanceTimersByTimeAsync(250);

      expect(internals(c).activeIndex()).toBe(-1); // nothing highlighted initially
      internals(c).onKeydown(new KeyboardEvent('keydown', { key: 'ArrowDown' }));
      expect(internals(c).activeIndex()).toBe(0);  // first row (No payee) highlighted
    } finally {
      vi.useRealTimers();
    }
  });

  it('resets the keyboard highlight when the picker reopens (no stale highlight/selection carried over)', async () => {
    vi.useFakeTimers();
    try {
      getPayees.mockReturnValue(of({ items: [payee('p1', 'Costco'), payee('p2', 'Safeway')], totalCount: 2 }));
      const fixture = setupFixture();
      const c = fixture.componentInstance;
      c.popoverState.set('open');
      c.searchTerm.set('Co');
      await vi.advanceTimersByTimeAsync(250);

      internals(c).onKeydown(new KeyboardEvent('keydown', { key: 'ArrowDown' }));
      internals(c).onKeydown(new KeyboardEvent('keydown', { key: 'ArrowDown' }));
      expect(internals(c).activeIndex()).toBeGreaterThan(-1);

      // Close, then reopen WITHOUT changing the term (row set unchanged): the highlight must not persist,
      // or a stray Enter on reopen would select/activate a row the user never navigated to.
      c.popoverState.set('closed');
      c.popoverState.set('open');
      expect(internals(c).activeIndex()).toBe(-1);
    } finally {
      vi.useRealTimers();
    }
  });

  it('Enter with the create row showing (nothing highlighted) creates the payee', async () => {
    vi.useFakeTimers();
    try {
      getPayees.mockReturnValue(of({ items: [], totalCount: 0 }));
      const fixture = setupFixture();
      const c = fixture.componentInstance;
      c.popoverState.set('open');
      c.searchTerm.set('Brand New Payee');
      await vi.advanceTimersByTimeAsync(250);
      expect(c.showCreateRow()).toBe(true);

      internals(c).onKeydown(new KeyboardEvent('keydown', { key: 'Enter' }));

      expect(createPayee).toHaveBeenCalledTimes(1);
      expect(createPayee.mock.calls[0][0]).toEqual({ body: { name: 'Brand New Payee' } });
    } finally {
      vi.useRealTimers();
    }
  });
});
