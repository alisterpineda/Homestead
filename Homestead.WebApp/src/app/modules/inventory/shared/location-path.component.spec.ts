import { TestBed, ComponentFixture } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { provideRouter } from '@angular/router';
import { LocationPathComponent } from './location-path.component';
import type { LocationPathView } from './location-path';

describe('LocationPathComponent', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        // A crumb click is a REAL navigation, so the crumb targets need matching routes — with none,
        // the router rejects asynchronously, after teardown has destroyed the injector (NG0205).
        provideRouter([{ path: '**', children: [] }]),
      ],
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

  function hrefs(fixture: ComponentFixture<LocationPathComponent>): string[] {
    return Array.from(host(fixture).querySelectorAll<HTMLAnchorElement>('a[href]'))
      .map(a => decodeURIComponent(a.getAttribute('href') ?? ''));
  }

  const STORED: LocationPathView = {
    prefix: null,
    crumbs: [
      { label: 'Garage', link: ['/inventory/browse/area', 'garage'], muted: false },
      { label: 'Tool chest', link: ['/inventory/browse/container', 'chest'], muted: false },
      { label: 'Gaming PC', link: ['/inventory/items', 'pc'], muted: false },
    ],
  };

  it('links every crumb to the route its kind resolved to', () => {
    const fixture = create(STORED);

    expect(hrefs(fixture)).toEqual([
      '/inventory/browse/area/garage',
      '/inventory/browse/container/chest',
      '/inventory/items/pc',
    ]);
  });

  it('renders the prefix ahead of the crumbs without linking it', () => {
    const fixture = create({
      prefix: 'Part of',
      crumbs: [{ label: 'Gaming PC', link: ['/inventory/items', 'pc'], muted: false }],
    });

    // The whole string, not a substring: the separator is an &nbsp; in the template, and with
    // preserveWhitespaces off nothing else would keep "Part of" off the assembly's name.
    expect(host(fixture).textContent!.replace(/\u00a0/g, ' ').trim()).toBe('Part of Gaming PC');
    expect(hrefs(fixture)).toEqual(['/inventory/items/pc']);
  });

  it('separates crumbs with an aria-hidden chevron, so the path reads as one label', () => {
    const fixture = create(STORED);

    const separators = Array.from(host(fixture).querySelectorAll('span[aria-hidden="true"]'));
    // One fewer separator than crumbs — none trailing the leaf.
    expect(separators).toHaveLength(2);
    expect(separators.every(s => s.textContent?.includes('›'))).toBe(true);
  });

  it('renders no <nav> landmark — the Browse header stays the only breadcrumb', () => {
    const fixture = create(STORED);

    expect(host(fixture).querySelector('nav')).toBeNull();
    expect(host(fixture).querySelector('[aria-label="breadcrumb"]')).toBeNull();
  });

  it('mutes the Unplaced crumb while keeping it navigable', () => {
    const fixture = create({
      prefix: null,
      crumbs: [{ label: 'Unplaced', link: ['/inventory/browse/unplaced'], muted: true }],
    });

    const anchor = host(fixture).querySelector('a');
    expect(anchor?.classList).toContain('text-muted-foreground');
    expect(hrefs(fixture)).toEqual(['/inventory/browse/unplaced']);
  });

  it('renders a link-less crumb as a plain muted span', () => {
    const fixture = create({
      prefix: null,
      crumbs: [{ label: '(unknown)', link: null, muted: true }],
    });

    expect(host(fixture).querySelector('a')).toBeNull();
    const span = host(fixture).querySelector('span');
    expect(span?.textContent).toBe('(unknown)');
    expect(span?.classList).toContain('text-muted-foreground');
  });

  it('emits navigated on a crumb click, so a host panel can close before the router runs', async () => {
    const fixture = create(STORED);
    let emissions = 0;
    fixture.componentInstance.navigated.subscribe(() => emissions++);

    host(fixture).querySelector('a')!.dispatchEvent(new MouseEvent('click', { bubbles: true }));
    await fixture.whenStable(); // let the router's navigation settle before teardown

    expect(emissions).toBe(1);
  });

  it('stays silent on a modified click — RouterLink leaves this tab, so the host must not close', async () => {
    const fixture = create(STORED);
    let emissions = 0;
    fixture.componentInstance.navigated.subscribe(() => emissions++);

    const anchor = host(fixture).querySelector('a')!;
    for (const modifier of ['ctrlKey', 'metaKey', 'shiftKey', 'altKey'] as const) {
      anchor.dispatchEvent(new MouseEvent('click', { bubbles: true, [modifier]: true }));
    }
    // Middle-click (button 1) is the other open-in-new-tab gesture RouterLink ignores.
    anchor.dispatchEvent(new MouseEvent('click', { bubbles: true, button: 1 }));
    await fixture.whenStable();

    expect(emissions).toBe(0);
  });
});
