import { TestBed, ComponentFixture } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { provideRouter } from '@angular/router';
import { PaginationComponent } from './pagination.component';

describe('PaginationComponent', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideZonelessChangeDetection(), provideRouter([])],
    });
  });

  function create(
    inputs: { page: number; totalPages: number; totalCount: number; pageSize: number },
  ): { fixture: ComponentFixture<PaginationComponent>; pageChanges: number[] } {
    const fixture = TestBed.createComponent(PaginationComponent);
    for (const [k, v] of Object.entries(inputs)) {
      fixture.componentRef.setInput(k, v);
    }
    const pageChanges: number[] = [];
    fixture.componentInstance.pageChange.subscribe((p) => pageChanges.push(p));
    fixture.detectChanges();
    return { fixture, pageChanges };
  }

  function button(fixture: ComponentFixture<PaginationComponent>, label: string): HTMLButtonElement {
    return (fixture.nativeElement as HTMLElement).querySelector(`button[aria-label="${label}"]`)!;
  }

  it('renders nothing when there are no results', () => {
    const { fixture } = create({ page: 1, totalPages: 0, totalCount: 0, pageSize: 20 });
    expect((fixture.nativeElement as HTMLElement).textContent?.trim()).toBe('');
  });

  it('hides the page nav when everything fits on one page', () => {
    const { fixture } = create({ page: 1, totalPages: 1, totalCount: 5, pageSize: 20 });
    expect((fixture.nativeElement as HTMLElement).querySelector('nav')).toBeNull();
  });

  it('disables and dims Previous on the first page, leaving Next enabled', () => {
    const { fixture } = create({ page: 1, totalPages: 3, totalCount: 50, pageSize: 20 });
    const prev = button(fixture, 'Go to previous page');
    const next = button(fixture, 'Go to next page');
    expect(prev.disabled).toBe(true);
    expect(prev.hasAttribute('data-disabled')).toBe(true);
    expect(next.disabled).toBe(false);
    expect(next.hasAttribute('data-disabled')).toBe(false);
  });

  it('disables and dims Next on the last page, leaving Previous enabled', () => {
    const { fixture } = create({ page: 3, totalPages: 3, totalCount: 50, pageSize: 20 });
    const prev = button(fixture, 'Go to previous page');
    const next = button(fixture, 'Go to next page');
    expect(next.disabled).toBe(true);
    expect(next.hasAttribute('data-disabled')).toBe(true);
    expect(prev.disabled).toBe(false);
    expect(prev.hasAttribute('data-disabled')).toBe(false);
  });

  it('emits the adjacent page from Previous / Next and the chosen page from a page button', () => {
    const { fixture, pageChanges } = create({ page: 2, totalPages: 3, totalCount: 50, pageSize: 20 });
    button(fixture, 'Go to previous page').click();
    button(fixture, 'Go to next page').click();
    button(fixture, 'Page 3').click();
    expect(pageChanges).toEqual([1, 3, 3]);
  });

  it('marks the current page button as the active page', () => {
    const { fixture } = create({ page: 2, totalPages: 3, totalCount: 50, pageSize: 20 });
    expect(button(fixture, 'Page 2').getAttribute('aria-current')).toBe('page');
    expect(button(fixture, 'Page 1').getAttribute('aria-current')).toBeNull();
  });

  it('shows the visible result range', () => {
    const { fixture } = create({ page: 3, totalPages: 3, totalCount: 50, pageSize: 20 });
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Showing 41–50 of 50');
  });

  it('labels the page-size trigger via a unique id', () => {
    const a = create({ page: 1, totalPages: 1, totalCount: 5, pageSize: 20 }).fixture.nativeElement as HTMLElement;
    const b = create({ page: 1, totalPages: 1, totalCount: 5, pageSize: 20 }).fixture.nativeElement as HTMLElement;
    const labelA = a.querySelector('label')!;
    const labelB = b.querySelector('label')!;
    expect(labelA.textContent).toBe('Rows per page');
    expect(a.querySelector(`#${labelA.htmlFor}`)).not.toBeNull();
    expect(labelA.htmlFor).not.toBe(labelB.htmlFor);
  });
});
