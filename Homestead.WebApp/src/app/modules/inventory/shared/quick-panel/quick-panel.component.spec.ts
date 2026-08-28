import { Component, provideZonelessChangeDetection, signal } from '@angular/core';
import { TestBed, type ComponentFixture } from '@angular/core/testing';
import type { BrnDialogState } from '@spartan-ng/brain/dialog';
import { stubMatchMedia } from '@/shared/testing/match-media';
import { MD_BREAKPOINT_QUERY } from '@/shared/utils/viewport';
import { QuickPanelComponent } from './quick-panel.component';

@Component({
  imports: [QuickPanelComponent],
  template: `
    <app-quick-panel title="Widget" [state]="state()" (stateChanged)="last = $event">
      <span panelTitleStart data-testid="start">S</span>
      <span panelTitleEnd data-testid="end">E</span>
      <p data-testid="body">Body</p>
    </app-quick-panel>
  `,
})
class HostComponent {
  state = signal<BrnDialogState>('open');
  last: BrnDialogState | null = null;
}

describe('QuickPanelComponent', () => {
  let fixture: ComponentFixture<HostComponent>;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideZonelessChangeDetection()] });
  });

  afterEach(() => vi.unstubAllGlobals());

  function create() {
    fixture = TestBed.createComponent(HostComponent);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  it('renders a right-side sheet at or above md (48rem)', () => {
    const media = stubMatchMedia(true);
    const host = create();
    expect(media.matchMedia).toHaveBeenCalledWith(MD_BREAKPOINT_QUERY);
    expect(host.querySelector('hlm-sheet')).not.toBeNull();
    expect(host.querySelector('hlm-drawer')).toBeNull();
    expect(document.querySelector('hlm-sheet-content')?.getAttribute('data-side')).toBe('right');
  });

  it('renders a bottom drawer below md', () => {
    stubMatchMedia(false);
    const host = create();
    expect(host.querySelector('hlm-drawer')).not.toBeNull();
    expect(host.querySelector('hlm-sheet')).toBeNull();
    expect(document.querySelector('hlm-drawer-content')?.getAttribute('data-vaul-drawer-direction')).toBe('bottom');
  });

  it('projects the title row and body into whichever tree is rendered', () => {
    const media = stubMatchMedia(true);
    create();
    const sheet = document.querySelector('hlm-sheet-content')!;
    expect(sheet.querySelector('[hlmsheettitle]')?.textContent?.trim()).toBe('Widget');
    expect(sheet.querySelector('[data-testid="start"]')).not.toBeNull();
    expect(sheet.querySelector('[data-testid="end"]')).not.toBeNull();
    expect(sheet.querySelector('[data-testid="body"]')).not.toBeNull();

    media.fireChange(false);
    fixture.detectChanges();
    const drawer = document.querySelector('hlm-drawer-content')!;
    expect(document.querySelector('hlm-sheet-content')).toBeNull();
    expect(drawer.querySelector('[hlmdrawertitle]')?.textContent?.trim()).toBe('Widget');
    expect(drawer.querySelector('[data-testid="start"]')).not.toBeNull();
    expect(drawer.querySelector('[data-testid="end"]')).not.toBeNull();
    expect(drawer.querySelector('[data-testid="body"]')).not.toBeNull();
  });

  it('falls back to the sheet without matchMedia (jsdom/SSR)', () => {
    vi.stubGlobal('matchMedia', undefined);
    const host = create();
    expect(host.querySelector('hlm-sheet')).not.toBeNull();
  });

  it.each([
    ['drawer', false],
    ['sheet', true],
  ])('forwards stateChanged from the active tree (%s)', (_tree, wide) => {
    stubMatchMedia(wide);
    create();
    fixture.componentInstance.state.set('closed');
    fixture.detectChanges();
    expect(fixture.componentInstance.last).toBe('closed');
  });
});
