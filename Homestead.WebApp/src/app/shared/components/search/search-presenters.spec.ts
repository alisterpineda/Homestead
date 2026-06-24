import { TestBed } from '@angular/core/testing';
import { SearchPresenterRegistry } from './search-presenters';
import type { SearchHit } from '@/api/generated/models/search-hit';

describe('SearchPresenterRegistry — documents', () => {
  let registry: SearchPresenterRegistry;

  const hit = (over: Partial<SearchHit> = {}): SearchHit => ({
    source: 'documents',
    type: 'document',
    id: 'doc-1',
    title: 'Tax Invoice 2024.pdf',
    subtitle: null,
    badge: null,
    ...over,
  });

  beforeEach(() => {
    TestBed.configureTestingModule({});
    registry = TestBed.inject(SearchPresenterRegistry);
  });

  it('registers a presenter for the documents source', () => {
    expect(registry.get('documents')).toBeDefined();
  });

  it('uses the image icon for image hits and the file icon otherwise', () => {
    const presenter = registry.get('documents')!;
    expect(presenter.icon(hit({ type: 'image' }))).toBe('lucideImage');
    expect(presenter.icon(hit({ type: 'document' }))).toBe('lucideFileText');
  });

  it('navigates to the document detail route by id', () => {
    const presenter = registry.get('documents')!;
    expect(presenter.navigate(hit({ id: 'abc-123' }))).toEqual(['/documents', 'abc-123']);
  });

  it('deep-links "view all" to the document list with the search query', () => {
    const presenter = registry.get('documents')!;
    expect(presenter.viewAll('tax invoice')).toEqual({
      commands: ['/documents'],
      queryParams: { search: 'tax invoice' },
    });
  });
});
