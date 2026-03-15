import { TestBed, ComponentFixture } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Component, signal } from '@angular/core';
import { PartyInputComponent, type PartyOption } from './party-input.component';
import { CREATE_OPTION_ID } from '../bulk-edit-shared';

@Component({
  imports: [PartyInputComponent],
  template: `<app-party-input [value]="parties()" (valueChange)="parties.set($event)" />`,
})
class TestHostComponent {
  parties = signal<PartyOption[]>([]);
}

describe('PartyInputComponent', () => {
  let fixture: ComponentFixture<TestHostComponent>;
  let component: PartyInputComponent;
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
    component.onSearchInput({ target: { value: 'acme' } } as unknown as Event);
    expect(component.loading()).toBe(true);
  });

  it('Create option hidden while loading', () => {
    component.onSearchInput({ target: { value: 'New Corp' } } as unknown as Event);
    expect(component.loading()).toBe(true);
    expect(component.allOptions().some((o) => o.id === CREATE_OPTION_ID)).toBe(false);
  });

  it('Create option shown after results arrive with no exact match', () => {
    component.onSearchInput({ target: { value: 'New Corp' } } as unknown as Event);
    component.showSuggestions.set(true);

    vi.advanceTimersByTime(200);
    const req = httpTesting.expectOne((r) => r.url.includes('/api/v1/documents/parties'));
    req.flush({ items: [], totalCount: 0 });
    fixture.detectChanges();

    expect(component.loading()).toBe(false);
    expect(component.allOptions().some((o) => o.id === CREATE_OPTION_ID)).toBe(true);
    expect(component.allOptions().find((o) => o.id === CREATE_OPTION_ID)!.name).toBe('New Corp');
  });

  it('Create option hidden when exact match found', () => {
    component.onSearchInput({ target: { value: 'Acme Corp' } } as unknown as Event);
    component.showSuggestions.set(true);

    vi.advanceTimersByTime(200);
    const req = httpTesting.expectOne((r) => r.url.includes('/api/v1/documents/parties'));
    req.flush({ items: [{ id: 'p1', name: 'Acme Corp' }], totalCount: 1 });
    fixture.detectChanges();

    expect(component.loading()).toBe(false);
    expect(component.allOptions().some((o) => o.id === CREATE_OPTION_ID)).toBe(false);
  });
});
