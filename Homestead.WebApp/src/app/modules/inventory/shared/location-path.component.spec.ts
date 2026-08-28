import { TestBed, ComponentFixture } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { LocationPathComponent } from './location-path.component';
import type { LocationPathView } from './location-path';

describe('LocationPathComponent', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideZonelessChangeDetection()],
    });
  });

  function create(path: LocationPathView): ComponentFixture<LocationPathComponent> {
    const fixture = TestBed.createComponent(LocationPathComponent);
    fixture.componentRef.setInput('path', path);
    fixture.detectChanges();
    return fixture;
  }

  function host(fixture: ComponentFixture<LocationPathComponent>): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  /** The separator is an &nbsp;-padded glyph, so normalise before comparing to a plain string. */
  function text(fixture: ComponentFixture<LocationPathComponent>): string {
    return host(fixture).textContent!.replace(/ /g, ' ').replace(/\s+/g, ' ').trim();
  }

  /** The crumb spans only — the separators are aria-hidden and carry no label. */
  function crumbSpans(fixture: ComponentFixture<LocationPathComponent>): HTMLElement[] {
    return Array.from(host(fixture).querySelectorAll<HTMLElement>('span')).filter(
      s => !s.hasAttribute('aria-hidden'),
    );
  }

  const STORED: LocationPathView = {
    prefix: null,
    crumbs: [
      { label: 'Garage', muted: false },
      { label: 'Tool chest', muted: false },
      { label: 'Gaming PC', muted: false },
    ],
  };

  const DEEP: LocationPathView = {
    prefix: null,
    crumbs: [
      { label: 'Root', muted: false },
      { label: 'B', muted: false },
      { label: 'C', muted: false },
      { label: 'Leaf', muted: false },
    ],
  };

  it('renders every crumb as plain text joined by the separator glyph', () => {
    const fixture = create(STORED);

    expect(text(fixture)).toBe('Garage › Tool chest › Gaming PC');
    // Per-segment links were removed on purpose — one link per segment forced deep paths to wrap.
    expect(host(fixture).querySelector('a')).toBeNull();
  });

  it('collapses four or more segments on screen while the tooltip keeps the full path', () => {
    const fixture = create(DEEP);

    expect(text(fixture)).toBe('Root › … › Leaf');
    // The middle is never lost — it moves into the native tooltip.
    expect(host(fixture).getAttribute('title')).toBe('Root › B › C › Leaf');
  });

  it('shows the whole path on a full-width surface that opts out of the collapse', () => {
    const fixture = TestBed.createComponent(LocationPathComponent);
    fixture.componentRef.setInput('path', DEEP);
    fixture.componentRef.setInput('collapse', false);
    fixture.detectChanges();

    expect(text(fixture)).toBe('Root › B › C › Leaf');
    // Width is still guarded segment by segment; only the depth rule is off.
    expect(crumbSpans(fixture).every(s => s.classList.contains('truncate'))).toBe(true);
    expect(host(fixture).getAttribute('title')).toBe('Root › B › C › Leaf');
  });

  it('renders the prefix ahead of the crumbs, muted and not as a crumb', () => {
    const fixture = create({
      prefix: 'Part of',
      crumbs: [{ label: 'Gaming PC', muted: false }],
    });

    // The whole string, not a substring: the gap after the literal is an &nbsp; in the template, and
    // with preserveWhitespaces off nothing else would keep "Part of" off the assembly's name.
    expect(text(fixture)).toBe('Part of Gaming PC');
    // The literal subordinates itself to the assembly name it introduces — it is not a crumb, so
    // the ancestor-muting rule in the @for loop can never cover it.
    expect(crumbSpans(fixture)[0].classList).toContain('text-muted-foreground');
    expect(crumbSpans(fixture).at(-1)!.classList).not.toContain('text-muted-foreground');
  });

  it('mutes every ancestor and leaves the leaf at the caller\'s colour', () => {
    const fixture = create(STORED);

    expect(crumbSpans(fixture).map(s => s.classList.contains('text-muted-foreground'))).toEqual([
      true,
      true,
      false,
    ]);
    // No colour of its own on the leaf: the detail headline mutes the whole line when Unplaced, and
    // a hard-coded text-foreground here would fight that.
    expect(crumbSpans(fixture).at(-1)!.classList).not.toContain('text-foreground');
  });

  it('mutes the collapse ellipsis along with the ancestors', () => {
    const fixture = create(DEEP);

    expect(crumbSpans(fixture).map(s => s.textContent)).toEqual(['Root', '…', 'Leaf']);
    expect(crumbSpans(fixture)[1].classList).toContain('text-muted-foreground');
  });

  it('mutes the Unplaced crumb even as the leaf', () => {
    const fixture = create({
      prefix: null,
      crumbs: [{ label: 'Unplaced', muted: true }],
    });

    expect(crumbSpans(fixture)[0].classList).toContain('text-muted-foreground');
  });

  it('renders an unresolved crumb as a plain muted span', () => {
    const fixture = create({
      prefix: null,
      crumbs: [{ label: '(unknown)', muted: true }],
    });

    const span = crumbSpans(fixture)[0];
    expect(span.textContent).toBe('(unknown)');
    expect(span.classList).toContain('text-muted-foreground');
  });

  it('hides the separators from assistive tech, so the path reads as one label', () => {
    const fixture = create(STORED);

    const separators = Array.from(host(fixture).querySelectorAll('span[aria-hidden="true"]'));
    // One fewer separator than visible crumbs — none trailing the leaf.
    expect(separators).toHaveLength(2);
    expect(separators.every(s => s.textContent!.includes('›'))).toBe(true);
  });

  it('truncates each segment on its own, yielding width leaf last', () => {
    const fixture = create(STORED);

    // One flex line of independently-truncating boxes, not one end-truncated string: a long root
    // must not be able to clip the leaf — the thing you're actually looking for — off the end.
    expect(host(fixture).classList).toContain('flex');
    expect(host(fixture).classList).toContain('whitespace-nowrap');
    const [root, middle, leaf] = crumbSpans(fixture);
    expect(root.classList).toContain('shrink-[100]');
    expect(middle.classList).toContain('shrink-[10000]');
    expect(leaf.classList).toContain('shrink');
    expect(crumbSpans(fixture).every(s => s.classList.contains('truncate'))).toBe(true);
    expect(host(fixture).getAttribute('title')).toBe('Garage › Tool chest › Gaming PC');
  });

  it('never shrinks the collapse ellipsis — it is already the shortest thing on the line', () => {
    const fixture = create(DEEP);

    const ellipsis = crumbSpans(fixture)[1];
    expect(ellipsis.classList).toContain('shrink-0');
    expect(ellipsis.classList).not.toContain('truncate');
  });

  it('renders no <nav> landmark — the Browse header stays the only breadcrumb', () => {
    const fixture = create(STORED);

    expect(host(fixture).querySelector('nav')).toBeNull();
    expect(host(fixture).querySelector('[aria-label="breadcrumb"]')).toBeNull();
  });
});
