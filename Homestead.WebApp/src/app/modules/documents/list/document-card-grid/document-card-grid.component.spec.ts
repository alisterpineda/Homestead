import { TestBed, ComponentFixture } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { DocumentCardGridComponent } from './document-card-grid.component';
import type { GetDocumentsResponse } from '@/api/generated/models/get-documents-response';
import type { GetDocumentsTagDto } from '@/api/generated/models/get-documents-tag-dto';

function createMockDocument(overrides: Partial<GetDocumentsResponse> = {}): GetDocumentsResponse {
  return {
    id: crypto.randomUUID(),
    name: 'Test Document',
    contentType: 'application/pdf',
    fileSize: 1024,
    fileExtension: '.pdf',
    createdAt: new Date().toISOString(),
    updatedAt: new Date().toISOString(),
    hasThumbnail: true,
    versionCount: 1,
    tags: [],
    parties: [],
    ...overrides,
  };
}

function createMockTag(overrides: Partial<GetDocumentsTagDto> = {}): GetDocumentsTagDto {
  return {
    id: crypto.randomUUID(),
    name: 'Tag',
    path: 'Category / Tag',
    color: '#3b82f6',
    ...overrides,
  };
}

describe('DocumentCardGridComponent', () => {
  let fixture: ComponentFixture<DocumentCardGridComponent>;
  let component: DocumentCardGridComponent;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient()],
    });
    fixture = TestBed.createComponent(DocumentCardGridComponent);
    component = fixture.componentInstance;
  });

  it('should create', () => {
    fixture.componentRef.setInput('documents', []);
    fixture.detectChanges();
    expect(component).toBeTruthy();
  });

  it('should render cards for provided documents', () => {
    const docs = [createMockDocument(), createMockDocument(), createMockDocument()];
    fixture.componentRef.setInput('documents', docs);
    fixture.detectChanges();

    const cards = fixture.nativeElement.querySelectorAll('[hlmCard]');
    expect(cards.length).toBe(3);
  });

  it('should emit documentSelect when card is clicked', () => {
    const doc = createMockDocument();
    fixture.componentRef.setInput('documents', [doc]);
    fixture.detectChanges();

    let emittedId: string | undefined;
    component.documentSelect.subscribe((id: string) => { emittedId = id; });

    const card = fixture.nativeElement.querySelector('[hlmCard]');
    card.click();

    expect(emittedId).toBe(doc.id);
  });

  it('should show file icon when hasThumbnail is false', () => {
    const doc = createMockDocument({ hasThumbnail: false, contentType: 'text/plain' });
    fixture.componentRef.setInput('documents', [doc]);
    fixture.detectChanges();

    const icon = fixture.nativeElement.querySelector('ng-icon');
    expect(icon).toBeTruthy();
  });

  it('should track thumbnail error and show fallback', () => {
    const doc = createMockDocument({ hasThumbnail: true });
    fixture.componentRef.setInput('documents', [doc]);
    fixture.detectChanges();

    // Trigger image error
    component.onImageError(doc.id);
    fixture.detectChanges();

    expect(component.hasError(doc.id)).toBe(true);
  });

  it('should toggle selection instead of navigating when in selection mode', () => {
    const doc = createMockDocument();
    fixture.componentRef.setInput('documents', [doc]);
    fixture.componentRef.setInput('selectedIds', new Set<string>(['other-id']));
    fixture.detectChanges();

    let emittedSelect: string | undefined;
    let emittedSelection: Set<string> | undefined;
    component.documentSelect.subscribe((id: string) => { emittedSelect = id; });
    component.selectionChange.subscribe((s: Set<string>) => { emittedSelection = s; });

    component.onCardClick(doc.id);

    expect(emittedSelect).toBeUndefined();
    expect(emittedSelection).toBeDefined();
    expect(emittedSelection!.has(doc.id)).toBe(true);
  });

  it('should deselect when clicking an already-selected card in selection mode', () => {
    const doc = createMockDocument();
    fixture.componentRef.setInput('documents', [doc]);
    fixture.componentRef.setInput('selectedIds', new Set<string>([doc.id]));
    fixture.detectChanges();

    let emittedSelection: Set<string> | undefined;
    component.selectionChange.subscribe((s: Set<string>) => { emittedSelection = s; });

    component.onCardClick(doc.id);

    expect(emittedSelection).toBeDefined();
    expect(emittedSelection!.has(doc.id)).toBe(false);
  });

  it('should emit selectionChange when checkbox toggled', () => {
    const doc = createMockDocument();
    fixture.componentRef.setInput('documents', [doc]);
    fixture.componentRef.setInput('selectedIds', new Set<string>());
    fixture.detectChanges();

    let emittedSet: Set<string> | undefined;
    component.selectionChange.subscribe((s: Set<string>) => { emittedSet = s; });

    component.onCheckboxChange(doc.id, true);

    expect(emittedSet).toBeDefined();
    expect(emittedSet!.has(doc.id)).toBe(true);
  });

  it('should render tag badges when document has tags', () => {
    const tags = [createMockTag({ path: 'Financial / Tax' }), createMockTag({ path: 'Legal' })];
    const doc = createMockDocument({ tags });
    fixture.componentRef.setInput('documents', [doc]);
    fixture.detectChanges();

    const tagBadges = fixture.nativeElement.querySelectorAll('app-tag-badge');
    expect(tagBadges.length).toBe(2);
  });

  it('should not render tag container when document has no tags', () => {
    const doc = createMockDocument({ tags: [] });
    fixture.componentRef.setInput('documents', [doc]);
    fixture.detectChanges();

    const tagBadges = fixture.nativeElement.querySelectorAll('app-tag-badge');
    expect(tagBadges.length).toBe(0);
  });

  it('should show +X badge when tags exceed maxVisibleTags', () => {
    const tags = Array.from({ length: 5 }, (_, i) => createMockTag({ path: `Tag ${i + 1}` }));
    const doc = createMockDocument({ tags });
    fixture.componentRef.setInput('documents', [doc]);
    fixture.detectChanges();

    const tagBadges = fixture.nativeElement.querySelectorAll('app-tag-badge');
    expect(tagBadges.length).toBe(component.maxVisibleTags);

    const overflowBadge = fixture.nativeElement.querySelector('[aria-label="and 2 more tags"]');
    expect(overflowBadge).toBeTruthy();
    expect(overflowBadge.textContent.trim()).toBe('+2');
  });

  it('should not show +X badge when tags equal maxVisibleTags', () => {
    const tags = Array.from({ length: 3 }, (_, i) => createMockTag({ path: `Tag ${i + 1}` }));
    const doc = createMockDocument({ tags });
    fixture.componentRef.setInput('documents', [doc]);
    fixture.detectChanges();

    const tagBadges = fixture.nativeElement.querySelectorAll('app-tag-badge');
    expect(tagBadges.length).toBe(3);

    const overflowBadge = fixture.nativeElement.querySelector('[hlmBadge][aria-label]');
    expect(overflowBadge).toBeNull();
  });

  it('should render match snippet when matchSnippet is set', () => {
    const doc = createMockDocument({
      matchSnippet: 'annual <mark>tax</mark> payment summary',
    });
    fixture.componentRef.setInput('documents', [doc]);
    fixture.detectChanges();

    const snippet = fixture.nativeElement.querySelector('app-match-snippet');
    expect(snippet).toBeTruthy();
    expect(snippet.textContent).toContain('annual tax payment summary');
    const mark = snippet.querySelector('mark');
    expect(mark).toBeTruthy();
    expect(mark.textContent).toBe('tax');
  });

  it('should not render match snippet when matchSnippet is null', () => {
    const doc = createMockDocument({ matchSnippet: null });
    fixture.componentRef.setInput('documents', [doc]);
    fixture.detectChanges();

    const snippet = fixture.nativeElement.querySelector('app-match-snippet');
    expect(snippet).toBeNull();
  });
});
