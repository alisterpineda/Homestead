import { provideZonelessChangeDetection, type WritableSignal } from '@angular/core';
import { TestBed, type ComponentFixture } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { HttpErrorResponse } from '@angular/common/http';
import { ItemActivityFeedComponent } from './item-activity-feed.component';
import { InventoryItemApiService } from '@/api/services/inventory-item-api.service';
import type { GetItemActivityResponse } from '@/api/generated/models/get-item-activity-response';
import type { NotePayload } from './item-note-composer.component';

interface ActivityCall {
  id: string;
  Page: number;
  PageSize: number;
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
    body: 'At Midas',
    isEdited: false,
    userDisplayName: 'Bob',
    userEmail: null,
    ...over,
  };
}

class StubItemApi {
  calls: ActivityCall[] = [];
  items: GetItemActivityResponse[] = [];
  totalPages = 1;
  fail = false;

  createCalls: { id: string; body: NotePayload }[] = [];
  updateCalls: { id: string; noteId: string; body: unknown }[] = [];
  deleteCalls: { id: string; noteId: string }[] = [];
  deleteFailure: unknown = null;

  getItemActivity = (p: ActivityCall) => {
    this.calls.push(p);
    if (this.fail) return throwError(() => new Error('boom'));
    const items = this.items;
    return of({ items, page: p.Page, pageSize: p.PageSize, totalCount: items.length, totalPages: this.totalPages });
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

  beforeEach(() => {
    api = new StubItemApi();
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        { provide: InventoryItemApiService, useValue: api },
      ],
    });
  });

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

  describe('rows', () => {
    it('renders audit and note rows interleaved from a single unfiltered request', async () => {
      api.items = [noteRow(), auditRow()];
      await create();

      expect(api.calls).toEqual([{ id: 'item1', Page: 1, PageSize: 20 }]);
      const content = text();
      expect(content).toContain('Alice updated location'); // audit summary
      expect(content).toContain('Bob'); // note author
      expect(content).toContain('At Midas'); // note body
    });

    it('marks an edited note and names the updater in the tooltip', async () => {
      api.items = [noteRow({
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

  });

  describe('create', () => {
    it('posts the note, clears the composer text, and reloads page 1', async () => {
      api.items = [noteRow()];
      api.totalPages = 2;
      const component = await create();
      internals<{ page: WritableSignal<number> }>(component).page.set(2);
      await settle();

      const composer = host().querySelector('app-item-note-composer')!;
      const input = composer.querySelector<HTMLInputElement>('input[hlmInputGroupInput]')!;
      input.value = 'New note';
      input.dispatchEvent(new Event('input'));
      await settle();
      composer.querySelector<HTMLButtonElement>('button[aria-label="Add note"]')!.click();
      await settle();

      expect(api.createCalls).toEqual([
        { id: 'item1', body: { body: 'New note', occurredAt: null } },
      ]);
      expect(input.value).toBe('');
      expect(api.calls.at(-1)).toMatchObject({ Page: 1 });
    });
  });

  describe('edit', () => {
    it('swaps the row for a composer, disables the header composer, saves, then reloads', async () => {
      api.items = [noteRow()];
      const component = await create();

      internals<{ startEdit(r: GetItemActivityResponse): void }>(component).startEdit(api.items[0]);
      await settle();

      const composers = host().querySelectorAll('app-item-note-composer');
      expect(composers).toHaveLength(2);
      // The header composer is disabled while an edit is open — only one composer at a time.
      expect(composers[0].querySelector('input[hlmInputGroupInput]')!.hasAttribute('disabled')).toBe(true);
      expect(composers[1].querySelector<HTMLInputElement>('input[hlmInputGroupInput]')!.value).toBe('At Midas');

      const before = api.calls.length;
      Array.from(composers[1].querySelectorAll<HTMLButtonElement>('button'))
        .find(b => b.textContent?.trim() === 'Save')!
        .click();
      await settle();

      expect(api.updateCalls).toEqual([{
        id: 'item1',
        noteId: 'n1',
        body: { body: 'At Midas', occurredAt: '2026-06-02T12:00:00Z' },
      }]);
      expect(api.calls.length).toBe(before + 1);
      expect(host().querySelectorAll('app-item-note-composer')).toHaveLength(1);
    });

    it('drops the open edit when paging away, so the header composer is not left disabled', async () => {
      api.items = [noteRow()];
      api.totalPages = 3;
      const component = await create();
      internals<{ startEdit(r: GetItemActivityResponse): void }>(component).startEdit(api.items[0]);
      await settle();
      expect(host().querySelectorAll('app-item-note-composer')).toHaveLength(2);

      host().querySelector<HTMLButtonElement>('button[aria-label="Next page"]')!.click();
      await settle();

      const composers = host().querySelectorAll('app-item-note-composer');
      expect(composers).toHaveLength(1);
      expect(composers[0].querySelector('input[hlmInputGroupInput]')!.hasAttribute('disabled')).toBe(false);
    });

    it('restores the row on Cancel without calling the API', async () => {
      api.items = [noteRow()];
      const component = await create();
      internals<{ startEdit(r: GetItemActivityResponse): void }>(component).startEdit(api.items[0]);
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
      api.items = [noteRow(), noteRow({ id: 'n2' })];
      const component = await create();
      const before = api.calls.length;

      internals<{
        onDeleteRequest(r: GetItemActivityResponse): void;
        confirmDelete(ctx: { close: () => void }): void;
      }>(component).onDeleteRequest(api.items[0]);
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
      api.items = [noteRow()];
      api.totalPages = 2;
      const component = await create();
      internals<{ page: WritableSignal<number> }>(component).page.set(2);
      await settle();

      internals<{ onDeleteRequest(r: GetItemActivityResponse): void }>(component)
        .onDeleteRequest(api.items[0]);
      internals<{ confirmDelete(ctx: { close: () => void }): void }>(component)
        .confirmDelete({ close: () => {} });
      await settle();

      expect(api.calls.at(-1)!.Page).toBe(1);
    });

    it('shows the rejection reason inline and keeps the dialog open on a 400', async () => {
      api.items = [noteRow()];
      api.deleteFailure = new HttpErrorResponse({
        status: 400,
        error: { detail: 'Nope.' },
      });
      const component = await create();

      internals<{ onDeleteRequest(r: GetItemActivityResponse): void }>(component)
        .onDeleteRequest(api.items[0]);
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
    expect(text()).not.toContain('No activity yet.');

    api.fail = false;
    api.items = [noteRow()];
    const before = api.calls.length;
    byText('Retry')!.click();
    await settle();

    expect(api.calls.length).toBe(before + 1);
    expect(text()).toContain('At Midas');
  });

  it('renders the empty copy when the feed has no rows', async () => {
    await create();
    expect(text()).toContain('No activity yet.');
  });

  it('pages with prev/next when there is more than one page', async () => {
    api.items = [noteRow()];
    api.totalPages = 3;
    await create();

    expect(text()).toContain('Page 1 of 3');
    host().querySelector<HTMLButtonElement>('button[aria-label="Next page"]')!.click();
    await settle();
    expect(api.calls.at(-1)!.Page).toBe(2);
  });

  it('re-fetches the current page when the host bumps refreshTick', async () => {
    api.items = [noteRow()];
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

  it('resets the page when the routed item changes', async () => {
    api.items = [noteRow()];
    api.totalPages = 2;
    const component = await create();
    internals<{ page: WritableSignal<number> }>(component).page.set(2);
    await settle();

    fixture.componentRef.setInput('itemId', 'item2');
    await settle();

    expect(internals<{ page: WritableSignal<number> }>(component).page()).toBe(1);
    expect(api.calls.at(-1)).toEqual({ id: 'item2', Page: 1, PageSize: 20 });
  });
});
