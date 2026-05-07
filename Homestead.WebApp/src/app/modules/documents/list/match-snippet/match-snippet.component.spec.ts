import { TestBed, ComponentFixture } from '@angular/core/testing';
import { MatchSnippetComponent } from './match-snippet.component';

describe('MatchSnippetComponent', () => {
  let fixture: ComponentFixture<MatchSnippetComponent>;
  let host: HTMLElement;

  beforeEach(() => {
    fixture = TestBed.createComponent(MatchSnippetComponent);
    host = fixture.nativeElement as HTMLElement;
  });

  function setHtml(html: string | null | undefined) {
    fixture.componentRef.setInput('html', html);
    fixture.detectChanges();
  }

  it('renders nothing when html is null or empty', () => {
    setHtml(null);
    expect(host.querySelector('mark')).toBeNull();

    setHtml('');
    expect(host.querySelector('mark')).toBeNull();
  });

  it('renders nothing when html has no <mark>', () => {
    setHtml('plain text without a match');
    expect(host.querySelector('mark')).toBeNull();
    expect(host.querySelector('.before')).toBeNull();
    expect(host.querySelector('.after')).toBeNull();
  });

  it('splits before/term/after around the first <mark>', () => {
    setHtml('annual <mark>tax</mark> payment summary');

    const before = host.querySelector('.before-inner');
    const mark = host.querySelector('mark');
    const after = host.querySelector('.after');

    expect(before?.textContent).toBe('annual ');
    expect(mark?.textContent).toBe('tax');
    expect(after?.textContent).toBe(' payment summary');
  });

  it('omits the leading span when the mark is at the start', () => {
    setHtml('<mark>tax</mark> payment summary');
    expect(host.querySelector('.before')).toBeNull();
    expect(host.querySelector('mark')?.textContent).toBe('tax');
    expect(host.querySelector('.after')?.textContent).toBe(' payment summary');
  });

  it('omits the trailing span when the mark is at the end', () => {
    setHtml('annual <mark>tax</mark>');
    expect(host.querySelector('.before-inner')?.textContent).toBe('annual ');
    expect(host.querySelector('mark')?.textContent).toBe('tax');
    expect(host.querySelector('.after')).toBeNull();
  });

  it('keeps subsequent marks intact inside the trailing region', () => {
    setHtml('foo <mark>bar</mark> baz <mark>bar</mark> qux');

    const marks = host.querySelectorAll('mark');
    expect(marks.length).toBe(2);
    expect(host.querySelector('.before-inner')?.textContent).toBe('foo ');
    expect(host.querySelector('.after')?.textContent).toBe(' baz bar qux');
    expect(host.querySelector('.after mark')?.textContent).toBe('bar');
  });

  it('decodes HTML entities produced by the backend sanitizer', () => {
    setHtml('Acme &amp; Co. <mark>invoice</mark> total');

    expect(host.querySelector('.before-inner')?.textContent).toBe('Acme & Co. ');
    expect(host.querySelector('mark')?.textContent).toBe('invoice');
  });

  it('returns nothing when an opening <mark> has no closing tag', () => {
    setHtml('foo <mark>bar without close');
    expect(host.querySelector('mark')).toBeNull();
  });
});
