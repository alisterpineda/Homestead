import { TestBed, ComponentFixture } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { SearchInputComponent } from './search-input.component';

describe('SearchInputComponent', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideZonelessChangeDetection()],
    });
  });

  function create(
    inputs: Partial<{ value: string; placeholder: string; ariaLabel: string | null; inputId: string | null }> = {},
  ): ComponentFixture<SearchInputComponent> {
    const fixture = TestBed.createComponent(SearchInputComponent);
    for (const [k, v] of Object.entries(inputs)) {
      fixture.componentRef.setInput(k, v);
    }
    fixture.detectChanges();
    return fixture;
  }

  function input(fixture: ComponentFixture<SearchInputComponent>): HTMLInputElement {
    return (fixture.nativeElement as HTMLElement).querySelector('input')!;
  }

  it('renders the search icon and a single input', () => {
    const fixture = create();
    const host = fixture.nativeElement as HTMLElement;
    expect(host.querySelectorAll('input').length).toBe(1);
    expect(host.querySelector('ng-icon')).not.toBeNull();
  });

  it('defaults the placeholder to "Search..." and uses it as the accessible name', () => {
    const fixture = create();
    expect(input(fixture).getAttribute('placeholder')).toBe('Search...');
    expect(input(fixture).getAttribute('aria-label')).toBe('Search...');
  });

  it('applies a custom placeholder and falls back to it for the accessible name', () => {
    const fixture = create({ placeholder: 'Search documents...' });
    expect(input(fixture).getAttribute('placeholder')).toBe('Search documents...');
    expect(input(fixture).getAttribute('aria-label')).toBe('Search documents...');
  });

  it('prefers an explicit ariaLabel over the placeholder', () => {
    const fixture = create({ placeholder: 'Search documents...', ariaLabel: 'Search all documents' });
    expect(input(fixture).getAttribute('aria-label')).toBe('Search all documents');
  });

  it('round-trips the value model into the rendered input', () => {
    const fixture = create({ value: 'invoices' });
    expect(input(fixture).value).toBe('invoices');
  });

  it('emits valueChange / updates the model as the user types', () => {
    const fixture = create();
    const el = input(fixture);
    el.value = 'receipts';
    el.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    expect(fixture.componentInstance.value()).toBe('receipts');
  });

  it('applies inputId as the input DOM id so an external label[for] can target it', () => {
    const fixture = create({ inputId: 'member-search' });
    expect(input(fixture).id).toBe('member-search');
  });

  it('inherits zoom-safe sizing from helm and adds no height/font override of its own', () => {
    const cls = input(create()).className;
    // Inherited from HlmInput: base 16px font below md (no iOS focus zoom), h-8 height.
    expect(cls).toContain('text-base');
    expect(cls).toContain('md:text-sm');
    expect(cls).toContain('h-8');
    // The component itself never hard-codes a taller height or a sub-16px-only font.
    expect(cls).not.toContain('h-9');
    expect(cls).not.toContain('h-11');
  });
});
