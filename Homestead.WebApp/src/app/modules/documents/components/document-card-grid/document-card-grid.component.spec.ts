import { TestBed, ComponentFixture } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { DocumentCardGridComponent } from './document-card-grid.component';
import type { GetDocumentsResponse } from '@/api/generated/models/get-documents-response';

function createMockDocument(overrides: Partial<GetDocumentsResponse> = {}): GetDocumentsResponse {
  return {
    id: crypto.randomUUID(),
    name: 'Test Document',
    contentType: 'application/pdf',
    fileSize: 1024,
    fileExtension: '.pdf',
    hasThumbnail: true,
    tags: [],
    parties: [],
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
});
