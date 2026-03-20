import { TestBed, ComponentFixture } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Component, signal } from '@angular/core';
import { TagInputComponent } from './tag-input.component';
import { CREATE_OPTION_ID } from '../bulk-edit-shared';

@Component({
  imports: [TagInputComponent],
  template: `<app-tag-input [value]="tags()" (valueChange)="tags.set($event)" />`,
})
class TestHostComponent {
  tags = signal<string[]>([]);
}

describe('TagInputComponent', () => {
  let fixture: ComponentFixture<TestHostComponent>;
  let component: TagInputComponent;
  let httpTesting: HttpTestingController;

  beforeEach(() => {
    vi.useFakeTimers();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
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
    const req = httpTesting.expectOne((r) => r.url.includes('/api/v1/documents/tags'));
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
    const req = httpTesting.expectOne((r) => r.url.includes('/api/v1/documents/tags'));
    req.flush({ items: [{ id: 't1', name: 'finance', path: 'finance', effectiveColor: null }], page: 1, pageSize: 50, totalCount: 1 });
    fixture.detectChanges();

    expect(component.loading()).toBe(false);
    expect(component.allOptions().some((o) => o.id === CREATE_OPTION_ID)).toBe(false);
  });

  it('addTag with Create option adds tag', () => {
    component.onSearchInput({ target: { value: 'newTag' } } as unknown as Event);

    vi.advanceTimersByTime(200);
    const req = httpTesting.expectOne((r) => r.url.includes('/api/v1/documents/tags'));
    req.flush({ items: [], page: 1, pageSize: 50, totalCount: 0 });

    component.addTag('newTag');
    expect(fixture.componentInstance.tags()).toContain('newTag');
  });

  it('addTag with existing suggestion propagates color', () => {
    component.onSearchInput({ target: { value: 'fin' } } as unknown as Event);

    vi.advanceTimersByTime(200);
    const req = httpTesting.expectOne((r) => r.url.includes('/api/v1/documents/tags'));
    req.flush({ items: [{ id: 't1', name: 'finance', path: 'finance', effectiveColor: '#ff0000' }], page: 1, pageSize: 50, totalCount: 1 });
    fixture.detectChanges();

    component.addTag('finance', '#ff0000');

    expect(fixture.componentInstance.tags()).toContain('finance');
    expect(component.colorMap().get('finance')).toBe('#ff0000');
  });
});
