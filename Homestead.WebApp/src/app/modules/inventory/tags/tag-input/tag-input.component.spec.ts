import { TestBed, ComponentFixture } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Component, provideZonelessChangeDetection, signal } from '@angular/core';
import { TagInputComponent } from './tag-input.component';
import { CREATE_OPTION_ID } from '../tag-input-shared';

@Component({
  imports: [TagInputComponent],
  template: `<app-tag-input
    [value]="tags()"
    [aiPendingTags]="aiPendingTags()"
    [aiTransientTags]="aiTransientTags()"
    (valueChange)="tags.set($event)"
    (tagApproved)="lastApproved.set($event)"
    (tagDeclined)="lastDeclined.set($event)"
  />`,
})
class TestHostComponent {
  tags = signal<string[]>([]);
  aiPendingTags = signal<Set<string>>(new Set());
  aiTransientTags = signal<Set<string>>(new Set());
  lastApproved = signal<string | null>(null);
  lastDeclined = signal<string | null>(null);
}

describe('TagInputComponent', () => {
  let fixture: ComponentFixture<TestHostComponent>;
  let component: TagInputComponent;
  let httpTesting: HttpTestingController;

  beforeEach(() => {
    vi.useFakeTimers();
    TestBed.configureTestingModule({
      providers: [provideZonelessChangeDetection(), provideHttpClient(), provideHttpClientTesting()],
    });
    fixture = TestBed.createComponent(TestHostComponent);
    fixture.detectChanges();
    component = fixture.debugElement.children[0].componentInstance;
    httpTesting = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpTesting.match(() => true);
    vi.useRealTimers();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('loading starts false', () => {
    expect(component.loading()).toBe(false);
  });

  it('loading becomes true on search input', () => {
    component.onSearchInput({ target: { value: 'fin' } } as unknown as Event);
    expect(component.loading()).toBe(true);
  });

  it('Create option hidden while loading', () => {
    component.onSearchInput({ target: { value: 'newTag' } } as unknown as Event);
    expect(component.loading()).toBe(true);
    expect(component.allOptions().some((o) => o.id === CREATE_OPTION_ID)).toBe(false);
  });

  it('Create option shown after results arrive with no exact match', () => {
    component.onSearchInput({ target: { value: 'newTag' } } as unknown as Event);
    component.showSuggestions.set(true);

    vi.advanceTimersByTime(200);
    const req = httpTesting.expectOne((r) => r.url.includes('/api/v1/inventory/tags'));
    req.flush({ items: [], page: 1, pageSize: 50, totalCount: 0 });
    fixture.detectChanges();

    expect(component.loading()).toBe(false);
    expect(component.allOptions().some((o) => o.id === CREATE_OPTION_ID)).toBe(true);
    expect(component.allOptions().find((o) => o.id === CREATE_OPTION_ID)!.path).toBe('newTag');
  });

  it('Create option hidden when exact match found', () => {
    component.onSearchInput({ target: { value: 'finance' } } as unknown as Event);
    component.showSuggestions.set(true);

    vi.advanceTimersByTime(200);
    const req = httpTesting.expectOne((r) => r.url.includes('/api/v1/inventory/tags'));
    req.flush({ items: [{ id: 't1', name: 'finance', path: 'finance', effectiveColor: null }], page: 1, pageSize: 50, totalCount: 1 });
    fixture.detectChanges();

    expect(component.loading()).toBe(false);
    expect(component.allOptions().some((o) => o.id === CREATE_OPTION_ID)).toBe(false);
  });

  it('addTag with Create option adds tag', () => {
    component.onSearchInput({ target: { value: 'newTag' } } as unknown as Event);

    vi.advanceTimersByTime(200);
    const req = httpTesting.expectOne((r) => r.url.includes('/api/v1/inventory/tags'));
    req.flush({ items: [], page: 1, pageSize: 50, totalCount: 0 });

    component.addTag('newTag');
    expect(fixture.componentInstance.tags()).toContain('newTag');
  });

  it('selectOption with create option hides suggestions without adding tag', () => {
    component.showSuggestions.set(true);

    component.selectOption({ id: CREATE_OPTION_ID, path: 'new-tag', effectiveColor: null });

    expect(component.showSuggestions()).toBe(false);
    expect(fixture.componentInstance.tags()).not.toContain('new-tag');
  });

  it('selectOption with existing option adds tag directly', () => {
    component.selectOption({ id: 't1', path: 'finance', effectiveColor: '#ff0000' });

    expect(fixture.componentInstance.tags()).toContain('finance');
    expect(component.colorMap().get('finance')).toBe('#ff0000');
  });

  it('addTag with existing suggestion propagates color', () => {
    component.onSearchInput({ target: { value: 'fin' } } as unknown as Event);

    vi.advanceTimersByTime(200);
    const req = httpTesting.expectOne((r) => r.url.includes('/api/v1/inventory/tags'));
    req.flush({ items: [{ id: 't1', name: 'finance', path: 'finance', effectiveColor: '#ff0000' }], page: 1, pageSize: 50, totalCount: 1 });
    fixture.detectChanges();

    component.addTag('finance', '#ff0000');

    expect(fixture.componentInstance.tags()).toContain('finance');
    expect(component.colorMap().get('finance')).toBe('#ff0000');
  });

  it('renders AI-pending tag with approve/decline buttons', () => {
    fixture.componentInstance.tags.set(['finance']);
    fixture.componentInstance.aiPendingTags.set(new Set(['finance']));
    fixture.detectChanges();

    const badge = fixture.nativeElement.querySelector('app-tag-badge');
    expect(badge).toBeTruthy();
    const approveBtn = badge.querySelector('[aria-label="Approve suggestion"]');
    const declineBtn = badge.querySelector('[aria-label="Decline suggestion"]');
    expect(approveBtn).toBeTruthy();
    expect(declineBtn).toBeTruthy();
    const removeBtn = badge.querySelector('[aria-label="Remove tag"]');
    expect(removeBtn).toBeFalsy();
  });

  it('renders AI-transient tag with remove button only', () => {
    fixture.componentInstance.tags.set(['taxes']);
    fixture.componentInstance.aiTransientTags.set(new Set(['taxes']));
    fixture.detectChanges();

    const badge = fixture.nativeElement.querySelector('app-tag-badge');
    expect(badge).toBeTruthy();
    const approveBtn = badge.querySelector('[aria-label="Approve suggestion"]');
    expect(approveBtn).toBeFalsy();
    const removeBtn = badge.querySelector('[aria-label="Remove tag"]');
    expect(removeBtn).toBeTruthy();
  });

  it('emits tagApproved when approve is clicked on a pending tag', () => {
    fixture.componentInstance.tags.set(['finance']);
    fixture.componentInstance.aiPendingTags.set(new Set(['finance']));
    fixture.detectChanges();

    const approveBtn = fixture.nativeElement.querySelector('[aria-label="Approve suggestion"]');
    approveBtn.click();
    fixture.detectChanges();

    expect(fixture.componentInstance.lastApproved()).toBe('finance');
  });

  it('emits tagDeclined when decline is clicked on a pending tag', () => {
    fixture.componentInstance.tags.set(['finance']);
    fixture.componentInstance.aiPendingTags.set(new Set(['finance']));
    fixture.detectChanges();

    const declineBtn = fixture.nativeElement.querySelector('[aria-label="Decline suggestion"]');
    declineBtn.click();
    fixture.detectChanges();

    expect(fixture.componentInstance.lastDeclined()).toBe('finance');
  });

  it('pending tags are not removable via backspace', () => {
    fixture.componentInstance.tags.set(['finance']);
    fixture.componentInstance.aiPendingTags.set(new Set(['finance']));
    fixture.detectChanges();

    component.onKeyDown(new KeyboardEvent('keydown', { key: 'Backspace' }));
    expect(fixture.componentInstance.tags()).toContain('finance');
  });
});
