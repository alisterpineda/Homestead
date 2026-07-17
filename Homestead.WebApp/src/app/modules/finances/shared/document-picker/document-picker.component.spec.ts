import { TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { of } from 'rxjs';
import { DocumentPickerComponent } from './document-picker.component';
import { DocumentApiService } from '@/api/services/document-api.service';
import type { GetDocumentsResponse } from '@/api/generated/models/get-documents-response';

function makeDoc(id: string, name: string): GetDocumentsResponse {
  return {
    id,
    name,
    contentType: 'application/pdf',
    fileExtension: '.pdf',
    fileSize: 100,
    parties: [],
    tags: [],
    updatedAt: '2026-01-01T00:00:00Z',
  };
}

describe('DocumentPickerComponent', () => {
  let getDocuments: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    getDocuments = vi.fn(() => of({ items: [] as GetDocumentsResponse[] }));
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(),
        { provide: DocumentApiService, useValue: { getDocuments } },
      ],
    });
  });

  function setup() {
    const fixture = TestBed.createComponent(DocumentPickerComponent);
    fixture.detectChanges();
    return fixture;
  }

  it('searches documents with the exact params after the 300ms debounce', async () => {
    vi.useFakeTimers();
    try {
      getDocuments.mockReturnValue(of({ items: [makeDoc('doc-1', 'Hydro invoice')] }));
      const fixture = setup();
      const c = fixture.componentInstance;

      c.search.set('invoice');
      // Not yet — the term is debounced.
      await vi.advanceTimersByTimeAsync(100);
      expect(getDocuments).not.toHaveBeenCalled();

      await vi.advanceTimersByTimeAsync(250);
      expect(getDocuments).toHaveBeenCalledTimes(1);
      expect(getDocuments).toHaveBeenCalledWith({ Search: 'invoice', PageSize: 10, IncludeChildTags: false });
      expect(c.results().map(d => d.id)).toEqual(['doc-1']);
    } finally {
      vi.useRealTimers();
    }
  });

  it('does not call the API for an empty or whitespace term and keeps results empty', async () => {
    vi.useFakeTimers();
    try {
      const fixture = setup();
      const c = fixture.componentInstance;

      // Initial empty term settles without a request.
      await vi.advanceTimersByTimeAsync(350);
      expect(getDocuments).not.toHaveBeenCalled();

      c.search.set('   ');
      await vi.advanceTimersByTimeAsync(350);

      expect(getDocuments).not.toHaveBeenCalled();
      expect(c.results()).toEqual([]);
    } finally {
      vi.useRealTimers();
    }
  });

  it('clicking a result emits documentSelected with the row', async () => {
    vi.useFakeTimers();
    try {
      const doc = makeDoc('doc-2', 'Receipt');
      getDocuments.mockReturnValue(of({ items: [doc] }));
      const fixture = setup();
      const c = fixture.componentInstance;
      const emitted: GetDocumentsResponse[] = [];
      c.documentSelected.subscribe(d => emitted.push(d));

      c.search.set('rec');
      await vi.advanceTimersByTimeAsync(350);
      fixture.detectChanges();

      const button = fixture.nativeElement.querySelector('ul button') as HTMLButtonElement;
      expect(button).not.toBeNull();
      expect(button.textContent).toContain('Receipt');
      button.click();

      expect(emitted).toEqual([doc]);
    } finally {
      vi.useRealTimers();
    }
  });
});
