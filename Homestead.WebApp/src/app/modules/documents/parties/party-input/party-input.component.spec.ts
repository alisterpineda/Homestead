import { TestBed, ComponentFixture } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Component, signal } from '@angular/core';
import { PartyInputComponent, type PartyOption } from './party-input.component';
import { CREATE_OPTION_ID } from '../../bulk-edit-shared';

@Component({
  imports: [PartyInputComponent],
  template: `<app-party-input
    [value]="parties()"
    [aiPendingParties]="aiPendingParties()"
    [aiTransientParties]="aiTransientParties()"
    (valueChange)="parties.set($event)"
    (partyApproved)="lastApproved.set($event)"
    (partyDeclined)="lastDeclined.set($event)"
  />`,
})
class TestHostComponent {
  parties = signal<PartyOption[]>([]);
  aiPendingParties = signal<Set<string>>(new Set());
  aiTransientParties = signal<Set<string>>(new Set());
  lastApproved = signal<string | null>(null);
  lastDeclined = signal<string | null>(null);
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

  it('selectOption with create option hides suggestions without adding party', () => {
    component.showSuggestions.set(true);

    component.selectOption({ id: CREATE_OPTION_ID, name: 'New Corp' });

    expect(component.showSuggestions()).toBe(false);
    expect(fixture.componentInstance.parties()).toEqual([]);
  });

  it('selectOption with existing option adds party directly', () => {
    component.selectOption({ id: 'p1', name: 'Acme Corp' });

    expect(fixture.componentInstance.parties()).toEqual([{ id: 'p1', name: 'Acme Corp' }]);
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

  it('renders AI-pending party with approve/decline buttons', () => {
    fixture.componentInstance.parties.set([{ id: 'p1', name: 'Acme Corp' }]);
    fixture.componentInstance.aiPendingParties.set(new Set(['p1']));
    fixture.detectChanges();

    const badge = fixture.nativeElement.querySelector('app-party-badge');
    expect(badge).toBeTruthy();
    expect(badge.querySelector('[aria-label="Approve suggestion"]')).toBeTruthy();
    expect(badge.querySelector('[aria-label="Decline suggestion"]')).toBeTruthy();
    expect(badge.querySelector('[aria-label="Remove party"]')).toBeFalsy();
  });

  it('renders AI-transient party with remove button only', () => {
    fixture.componentInstance.parties.set([{ id: 'p1', name: 'Acme Corp' }]);
    fixture.componentInstance.aiTransientParties.set(new Set(['p1']));
    fixture.detectChanges();

    const badge = fixture.nativeElement.querySelector('app-party-badge');
    expect(badge).toBeTruthy();
    expect(badge.querySelector('[aria-label="Approve suggestion"]')).toBeFalsy();
    expect(badge.querySelector('[aria-label="Remove party"]')).toBeTruthy();
  });

  it('emits partyApproved when approve is clicked on a pending party', () => {
    fixture.componentInstance.parties.set([{ id: 'p1', name: 'Acme Corp' }]);
    fixture.componentInstance.aiPendingParties.set(new Set(['p1']));
    fixture.detectChanges();

    fixture.nativeElement.querySelector('[aria-label="Approve suggestion"]').click();
    fixture.detectChanges();

    expect(fixture.componentInstance.lastApproved()).toBe('p1');
  });

  it('emits partyDeclined when decline is clicked on a pending party', () => {
    fixture.componentInstance.parties.set([{ id: 'p1', name: 'Acme Corp' }]);
    fixture.componentInstance.aiPendingParties.set(new Set(['p1']));
    fixture.detectChanges();

    fixture.nativeElement.querySelector('[aria-label="Decline suggestion"]').click();
    fixture.detectChanges();

    expect(fixture.componentInstance.lastDeclined()).toBe('p1');
  });

  it('pending parties are not removable via backspace', () => {
    fixture.componentInstance.parties.set([{ id: 'p1', name: 'Acme Corp' }]);
    fixture.componentInstance.aiPendingParties.set(new Set(['p1']));
    fixture.detectChanges();

    component.onKeyDown(new KeyboardEvent('keydown', { key: 'Backspace' }));
    expect(fixture.componentInstance.parties().some((p) => p.id === 'p1')).toBe(true);
  });
});
