import { provideZonelessChangeDetection, type WritableSignal } from '@angular/core';
import { TestBed, type ComponentFixture } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { HttpErrorResponse } from '@angular/common/http';
import { ItemActivityFeedComponent } from './item-activity-feed.component';
import { InventoryItemApiService } from '@/api/services/inventory-item-api.service';
import { UserPreferencesService } from '@/shared/services/user-preferences.service';
import type { GetItemActivityResponse } from '@/api/generated/models/get-item-activity-response';
import type { NotePayload } from './item-note-composer.component';

// The autocomplete anchors its popover with ResizeObserver.
if (typeof globalThis.ResizeObserver === 'undefined') {
  globalThis.ResizeObserver = class {
    observe() {}
    unobserve() {}
    disconnect() {}
  } as unknown as typeof ResizeObserver;
}

interface ActivityCall {
  id: string;
  Page: number;
  PageSize: number;
  notesOnly: boolean;
  filter?: string;
}

function auditRow(over: Partial<GetItemActivityResponse> = {}): GetItemActivityResponse {
  return {
    id: 'a1',
    type: 'audit_log',
    summary: 'Alice updated location',
    timestamp: '2026-06-01T00:00:00Z',
    ...over,
  };
}

function noteRow(over: Partial<GetItemActivityResponse> = {}): GetItemActivityResponse {
  return {
    id: 'n1',
    type: 'note',
    timestamp: '2026-06-02T12:00:00Z',
    occurredAt: '2026-06-02T12:00:00Z',
    createdAt: '2026-06-02T12:00:00Z',
    updatedAt: '2026-06-02T12:00:00Z',
    subject: 'Oil change',
    body: 'At Midas',
    isEdited: false,
    userDisplayName: 'Bob',
    userEmail: null,
    ...over,
  };
}

class StubItemApi {
  calls: ActivityCall[] = [];
  /** Keyed by `notesOnly` so the default-view probe can be exercised. */
  pages: { notes: GetItemActivityResponse[]; all: GetItemActivityResponse[] } = { notes: [], all: [] };
  totalPages = 1;
  fail = false;

  subjects: string[] = [];
  subjectCalls = 0;

  createCalls: { id: string; body: NotePayload }[] = [];
  updateCalls: { id: string; noteId: string; body: unknown }[] = [];
  deleteCalls: { id: string; noteId: string }[] = [];
  deleteFailure: unknown = null;

  getItemActivity = (p: ActivityCall) => {
    this.calls.push(p);
    if (this.fail) return throwError(() => new Error('boom'));
    const items = p.notesOnly ? this.pages.notes : this.pages.all;
    return of({ items, page: p.Page, pageSize: p.PageSize, totalCount: items.length, totalPages: this.totalPages });
  };
  getItemNoteSubjects = (_p: { id: string }) => {
    this.subjectCalls++;
    return of(this.subjects);
  };
  createItemNote = (p: { id: string; body: NotePayload }) => {
    this.createCalls.push(p);
    return of({ id: 'new' });
  };
  updateItemNote = (p: { id: string; noteId: string; body: unknown }) => {
    this.updateCalls.push(p);
    return of({ id: p.noteId });
  };
  deleteItemNote = (p: { id: string; noteId: string }) => {
    this.deleteCalls.push(p);
    return this.deleteFailure ? throwError(() => this.deleteFailure) : of(undefined);
  };
}

describe('ItemActivityFeedComponent', () => {
  let api: StubItemApi;
  let fixture: ComponentFixture<ItemActivityFeedComponent>;
  let store: Record<string, string>;

  beforeEach(() => {
    store = {};
    vi.stubGlobal('localStorage', {
      getItem: vi.fn((key: string) => store[key] ?? null),
      setItem: vi.fn((key: string, value: string) => { store[key] = value; }),
      removeItem: vi.fn((key: string) => { delete store[key]; }),
    });
    api = new StubItemApi();
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        { provide: InventoryItemApiService, useValue: api },
      ],
    });
  });

  afterEach(() => vi.unstubAllGlobals());

  async function create(itemId = 'item1'): Promise<ItemActivityFeedComponent> {
    fixture = TestBed.createComponent(ItemActivityFeedComponent);
    fixture.componentRef.setInput('itemId', itemId);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    return fixture.componentInstance;
  }

  async function settle(): Promise<void> {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  const host = () => fixture.nativeElement as HTMLElement;
  const text = () => host().textContent ?? '';
  const buttons = () => Array.from(host().querySelectorAll<HTMLButtonElement>('button'));
  const byText = (label: string) => buttons().find(b => b.textContent?.trim() === label);
  const internals = <T,>(c: ItemActivityFeedComponent) => c as unknown as T;

  describe('default view', () => {
    it('probes notes-only first and flips to All when the item has none', async () => {
      api.pages.all = [auditRow()];
      const component = await create();

      expect(api.calls.map(c => c.notesOnly)).toEqual([true, false]);
      expect(internals<{ view(): string }>(component).view()).toBe('all');
      expect(text()).toContain('Alice updated location');
    });

    it('stays on Notes and makes a single call when the item already has notes', async () => {
      api.pages.notes = [noteRow()];
      const component = await create();

      expect(api.calls).toHaveLength(1);
      expect(api.calls[0].notesOnly).toBe(true);
      expect(internals<{ view(): string }>(component).view()).toBe('notes');
    });

    it('honours a stored preference without probing', async () => {
      store['homestead-inventory-activity-view'] = '"all"';
      api.pages.all = [auditRow()];
      await create();

      expect(api.calls).toHaveLength(1);
      expect(api.calls[0].notesOnly).toBe(false);
    });
  });

  it('persists a toggle tap and resets to page 1', async () => {
    api.pages.notes = [noteRow()];
    api.totalPages = 3;
    const component = await create();
    internals<{ page: WritableSignal<number> }>(component).page.set(2);
    await settle();

    byText('All')!.click();
    await settle();

    expect(TestBed.inject(UserPreferencesService).inventoryActivityView()).toBe('all');
    expect(api.calls.at(-1)).toMatchObject({ Page: 1, notesOnly: false });
  });

  describe('filter', () => {
    beforeEach(() => {
      vi.useFakeTimers();
    });
    afterEach(() => {
      vi.useRealTimers();
    });

    it('sends the filter, forces Notes, and disables All', async () => {
      api.pages.notes = [noteRow()];
      const component = await create();
      const before = api.calls.length;

      internals<{ onFilterInput(v: string): void }>(component).onFilterInput('oil');
      await vi.advanceTimersByTimeAsync(300);
      await settle();

      expect(api.calls.at(-1)).toMatchObject({ Page: 1, notesOnly: true, filter: 'oil' });
      expect(api.calls.length).toBe(before + 1); // debounced: exactly one extra request
      expect(byText('All')!.disabled).toBe(true);
    });

    it('restores the previous view once the filter is cleared', async () => {
      store['homestead-inventory-activity-view'] = '"all"';
      api.pages.all = [auditRow()];
      const component = await create();

      internals<{ onFilterInput(v: string): void }>(component).onFilterInput('oil');
      await vi.advanceTimersByTimeAsync(300);
      await settle();
      expect(api.calls.at(-1)!.notesOnly).toBe(true);

      internals<{ onFilterInput(v: string): void }>(component).onFilterInput('');
      await vi.advanceTimersByTimeAsync(300);
      await settle();
      expect(api.calls.at(-1)).toMatchObject({ notesOnly: false, filter: undefined });
      expect(byText('All')!.disabled).toBe(false);
    });
  });

  describe('filter suggestions', () => {
    const many = ['Oil change', 'Oil filter', 'Tyres', 'Brakes', 'Battery', 'Coolant', 'Wipers', 'Air filter', 'Spark plugs'];
    const suggestions = (c: ItemActivityFeedComponent) =>
      internals<{ filterSuggestions(): readonly string[] }>(c).filterSuggestions();

    it('caps the unfiltered list at 8 and filters case-insensitively on the typed text', async () => {
      api.subjects = many;
      api.pages.notes = [noteRow()];
      const component = await create();
      expect(suggestions(component)).toHaveLength(8);

      internals<{ onFilterInput(v: string): void }>(component).onFilterInput('OIL');
      expect(suggestions(component)).toEqual(['Oil change', 'Oil filter']);
    });

    it('applies a picked suggestion as the filter', async () => {
      vi.useFakeTimers();
      try {
        api.subjects = many;
        api.pages.notes = [noteRow()];
        const component = await create();

        internals<{ onFilterPicked(v: unknown): void }>(component).onFilterPicked('Oil change');
        await vi.advanceTimersByTimeAsync(300);
        await settle();

        expect(api.calls.at(-1)).toMatchObject({ Page: 1, notesOnly: true, filter: 'Oil change' });
      } finally {
        vi.useRealTimers();
      }
    });
  });

  describe('rows', () => {
    it('renders audit and note rows interleaved', async () => {
      api.pages.notes = [noteRow()];
      api.pages.all = [noteRow(), auditRow()];
      await create();
      byText('All')!.click();
      await settle();

      const content = text();
      expect(content).toContain('Alice updated location'); // audit summary
      expect(content).toContain('Bob'); // note author
      expect(content).toContain('Oil change'); // note subject
      expect(content).toContain('At Midas'); // note body
    });

    it('renders a subject-less note as body only', async () => {
      api.pages.notes = [noteRow({ subject: null, body: 'Just a remark' })];
      await create();

      expect(text()).toContain('Just a remark');
      expect(text()).not.toContain('Oil change');
    });

    it('marks an edited note and names the updater in the tooltip', async () => {
      api.pages.notes = [noteRow({
        isEdited: true,
        updatedAt: '2026-06-03T09:00:00Z',
        updatedByDisplayName: 'Carol',
      })];
      await create();

      expect(text()).toContain('edited');
      const marker = Array.from(host().querySelectorAll('span[title]'))
        .find(s => s.textContent?.includes('edited'));
      expect(marker?.getAttribute('title')).toContain('Edited by Carol');
    });

    it('clamps a long body behind Show more and expands it', async () => {
      api.pages.notes = [noteRow({ body: 'x'.repeat(400) })];
      await create();

      const paragraph = () => Array.from(host().querySelectorAll('p')).find(p => p.textContent?.startsWith('xxx'))!;
      expect(paragraph().classList.contains('line-clamp-3')).toBe(true);

      byText('Show more')!.click();
      await settle();
      expect(paragraph().classList.contains('line-clamp-3')).toBe(false);

      byText('Show less')!.click();
      await settle();
      expect(paragraph().classList.contains('line-clamp-3')).toBe(true);
      expect(byText('Show more')).toBeDefined();
    });

    it('offers no Show more for a short body', async () => {
      api.pages.notes = [noteRow({ body: 'short' })];
      await create();
      expect(byText('Show more')).toBeUndefined();
    });
  });

  describe('create', () => {
    it('posts the note, clears the composer text, and reloads page 1 plus subjects', async () => {
      api.pages.notes = [noteRow()];
      api.totalPages = 2;
      const component = await create();
      internals<{ page: WritableSignal<number> }>(component).page.set(2);
      await settle();
      const subjectCallsBefore = api.subjectCalls;

      const composer = host().querySelector('app-item-note-composer')!;
      const textarea = composer.querySelector('textarea')!;
      textarea.value = 'New note';
      textarea.dispatchEvent(new Event('input'));
      await settle();
      composer.querySelector<HTMLButtonElement>('button[aria-label="Add note"]')!.click();
      await settle();

      expect(api.createCalls).toEqual([
        { id: 'item1', body: { subject: null, body: 'New note', occurredAt: null } },
      ]);
      expect(textarea.value).toBe('');
      expect(api.calls.at(-1)).toMatchObject({ Page: 1 });
      expect(api.subjectCalls).toBe(subjectCallsBefore + 1);
    });
  });

  describe('edit', () => {
    it('swaps the row for a composer, disables the header composer, saves, then reloads', async () => {
      api.pages.notes = [noteRow()];
      const component = await create();

      internals<{ startEdit(r: GetItemActivityResponse): void }>(component).startEdit(api.pages.notes[0]);
      await settle();

      const composers = host().querySelectorAll('app-item-note-composer');
      expect(composers).toHaveLength(2);
      // The header composer is disabled while an edit is open — only one composer at a time.
      expect(composers[0].querySelector('textarea')!.disabled).toBe(true);
      expect(composers[1].querySelector('textarea')!.value).toBe('At Midas');

      const before = api.calls.length;
      Array.from(composers[1].querySelectorAll<HTMLButtonElement>('button'))
        .find(b => b.textContent?.trim() === 'Save')!
        .click();
      await settle();

      expect(api.updateCalls).toEqual([{
        id: 'item1',
        noteId: 'n1',
        body: { subject: 'Oil change', body: 'At Midas', occurredAt: '2026-06-02T12:00:00Z' },
      }]);
      expect(api.calls.length).toBe(before + 1);
      expect(host().querySelectorAll('app-item-note-composer')).toHaveLength(1);
    });

    it('drops the open edit when paging away, so the header composer is not left disabled', async () => {
      api.pages.notes = [noteRow()];
      api.totalPages = 3;
      const component = await create();
      internals<{ startEdit(r: GetItemActivityResponse): void }>(component).startEdit(api.pages.notes[0]);
      await settle();
      expect(host().querySelectorAll('app-item-note-composer')).toHaveLength(2);

      host().querySelector<HTMLButtonElement>('button[aria-label="Next page"]')!.click();
      await settle();

      const composers = host().querySelectorAll('app-item-note-composer');
      expect(composers).toHaveLength(1);
      expect(composers[0].querySelector('textarea')!.disabled).toBe(false);
    });

    it('restores the row on Cancel without calling the API', async () => {
      api.pages.notes = [noteRow()];
      const component = await create();
      internals<{ startEdit(r: GetItemActivityResponse): void }>(component).startEdit(api.pages.notes[0]);
      await settle();

      const editComposer = host().querySelectorAll('app-item-note-composer')[1];
      Array.from(editComposer.querySelectorAll<HTMLButtonElement>('button'))
        .find(b => b.textContent?.trim() === 'Cancel')!
        .click();
      await settle();

      expect(api.updateCalls).toEqual([]);
      expect(host().querySelectorAll('app-item-note-composer')).toHaveLength(1);
      expect(text()).toContain('At Midas');
    });
  });

  describe('delete', () => {
    it('deletes on confirm and re-reads the current page', async () => {
      api.pages.notes = [noteRow(), noteRow({ id: 'n2' })];
      const component = await create();
      const before = api.calls.length;

      internals<{
        onDeleteRequest(r: GetItemActivityResponse): void;
        confirmDelete(ctx: { close: () => void }): void;
      }>(component).onDeleteRequest(api.pages.notes[0]);
      let closed = false;
      internals<{ confirmDelete(ctx: { close: () => void }): void }>(component)
        .confirmDelete({ close: () => { closed = true; } });
      await settle();

      expect(api.deleteCalls).toEqual([{ id: 'item1', noteId: 'n1' }]);
      expect(closed).toBe(true);
      expect(api.calls.length).toBe(before + 1);
      expect(api.calls.at(-1)!.Page).toBe(1);
    });

    it('falls back a page when the deleted note was the last one on a later page', async () => {
      api.pages.notes = [noteRow()];
      api.totalPages = 2;
      const component = await create();
      internals<{ page: WritableSignal<number> }>(component).page.set(2);
      await settle();

      internals<{ onDeleteRequest(r: GetItemActivityResponse): void }>(component)
        .onDeleteRequest(api.pages.notes[0]);
      internals<{ confirmDelete(ctx: { close: () => void }): void }>(component)
        .confirmDelete({ close: () => {} });
      await settle();

      expect(api.calls.at(-1)!.Page).toBe(1);
    });

    it('shows the rejection reason inline and keeps the dialog open on a 400', async () => {
      api.pages.notes = [noteRow()];
      api.deleteFailure = new HttpErrorResponse({
        status: 400,
        error: { detail: 'Nope.' },
      });
      const component = await create();

      internals<{ onDeleteRequest(r: GetItemActivityResponse): void }>(component)
        .onDeleteRequest(api.pages.notes[0]);
      let closed = false;
      internals<{ confirmDelete(ctx: { close: () => void }): void }>(component)
        .confirmDelete({ close: () => { closed = true; } });
      await settle();

      expect(closed).toBe(false);
      expect(internals<{ deleteError(): string }>(component).deleteError()).toBe('Nope.');
      expect(internals<{ deleteState(): string }>(component).deleteState()).toBe('open');
    });
  });

  it('renders the error state (not a false empty) and re-fetches on Retry', async () => {
    api.fail = true;
    await create();

    expect(text()).toContain("Couldn't load activity.");
    expect(text()).not.toContain('No notes yet.');

    api.fail = false;
    api.pages.notes = [noteRow()];
    const before = api.calls.length;
    byText('Retry')!.click();
    await settle();

    expect(api.calls.length).toBe(before + 1);
    expect(text()).toContain('At Midas');
  });

  it('renders the right empty copy for each view', async () => {
    await create();
    // Nothing anywhere: the probe flips to All, whose empty copy is the generic one.
    expect(text()).toContain('No activity yet.');

    byText('Notes')!.click();
    await settle();
    expect(text()).toContain('No notes yet.');
  });

  it('pages with prev/next when there is more than one page', async () => {
    api.pages.notes = [noteRow()];
    api.totalPages = 3;
    await create();

    expect(text()).toContain('Page 1 of 3');
    host().querySelector<HTMLButtonElement>('button[aria-label="Next page"]')!.click();
    await settle();
    expect(api.calls.at(-1)!.Page).toBe(2);
  });

  it('re-fetches the current page when the host bumps refreshTick', async () => {
    api.pages.notes = [noteRow()];
    api.totalPages = 3;
    const component = await create();
    internals<{ page: WritableSignal<number> }>(component).page.set(2);
    await settle();
    const before = api.calls.length;

    fixture.componentRef.setInput('refreshTick', 1);
    await settle();

    expect(api.calls.length).toBe(before + 1);
    expect(api.calls.at(-1)).toMatchObject({ id: 'item1', Page: 2 });
  });

  it('resets page, filter, and probe when the routed item changes', async () => {
    api.pages.notes = [noteRow()];
    api.totalPages = 2;
    const component = await create();
    internals<{ page: WritableSignal<number> }>(component).page.set(2);
    internals<{ onFilterInput(v: string): void }>(component).onFilterInput('oil');
    byText('All')!.click();
    await settle();

    fixture.componentRef.setInput('itemId', 'item2');
    await settle();

    expect(internals<{ page: WritableSignal<number> }>(component).page()).toBe(1);
    expect(internals<{ filterInput: WritableSignal<string> }>(component).filterInput()).toBe('');
    expect(api.calls.at(-1)).toMatchObject({ id: 'item2', Page: 1, filter: undefined });
  });
});
