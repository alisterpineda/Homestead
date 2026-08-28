import { Component, ChangeDetectionStrategy, input, output } from '@angular/core';
import { RouterLink } from '@angular/router';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideChevronRight } from '@ng-icons/lucide';
import type { LocationPathView } from './location-path';

/**
 * A resolved location path rendered as inline links — one per segment, each targeting the place that
 * segment names ({@link segmentToLink}). Used where the path is the SUBJECT of the line (the durable
 * detail headline, the durable quick-panel Location card, the consumable detail by-location table);
 * surfaces where it is a secondary column keep the plain text from `formatPath`.
 *
 * Styled to READ like `HlmBreadcrumb` — muted ancestors that go foreground on hover, a leaf left at
 * the caller's own colour, lucide chevrons between — but deliberately NOT built on it, for two
 * reasons visible in the helm source:
 *   - `HlmBreadcrumb` hard-codes `role="navigation"` with an `aria-label` defaulting to "breadcrumb",
 *     so reusing it would put two identically-labelled landmarks on a detail page. The Browse header
 *     (and each page header) stays the only breadcrumb landmark.
 *   - `HlmBreadcrumbList` hard-codes `text-muted-foreground` as well as `text-sm`, so it would paint
 *     the leaf too and defeat the leaf-keeps-the-caller's-colour rule. The host is `display: inline`
 *     with NO colour or size of its own, so the path inherits the caller's type scale and its
 *     muted/not state (the detail headline mutes the whole line when Unplaced). The three call sites
 *     do all sit at `text-sm` today, but none of them is pinned there by this component.
 *
 * Two deliberate departures from helm: `hover:underline` is kept, because the leaf inherits the
 * caller's colour and so `hover:text-foreground` alone would give the most-clickable segment no
 * hover feedback at all; and the chevrons are `aria-hidden`, so the path reads as one label rather
 * than being announced segment-by-segment.
 */
@Component({
  selector: 'app-location-path',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, NgIcon],
  providers: [provideIcons({ lucideChevronRight })],
  styles: `
    :host { display: inline; min-width: 0; }
    /* NgIcon's own :host sets \`vertical-align: initial\`. Emulated encapsulation compiles that to
       [_nghost-…] — specificity 0,1,0, the same as a bare \`align-*\` utility — and this app imports
       Tailwind's utilities UNLAYERED, so the tie breaks on source order, which component styles win
       (Angular injects them after the global sheet). Hence the chevron renders ~1.5px high on its
       baseline. Scoping to the element, ng-icon[_ngcontent-…] (0,1,1), outranks it without
       !important. */
    ng-icon { vertical-align: -0.1em; }
  `,
  template: `
    @if (path().prefix; as prefix) {
      <span class="text-muted-foreground">{{ prefix }}&nbsp;</span>
    }
    @for (crumb of path().crumbs; track $index; let last = $last) {
      @if (crumb.link; as link) {
        <a
          [routerLink]="link"
          class="transition-colors hover:text-foreground hover:underline"
          [class.text-muted-foreground]="crumb.muted || !last"
          (click)="onCrumbClick($event)"
        >{{ crumb.label }}</a>
      } @else {
        <span [class.text-muted-foreground]="crumb.muted || !last">{{ crumb.label }}</span>
      }
      @if (!last) {
        <!-- Same glyph HlmBreadcrumbSeparator draws. Two adaptations for an inline flow: helm's
             flex gap becomes explicit margins (mx-1.5 == its gap-1.5), and helm's fixed 14px sizing
             is left at NgIcon's default 1em, so the chevron tracks whatever scale the caller sets —
             identical at the text-sm helm itself is pinned to. -->
        <ng-icon name="lucideChevronRight" aria-hidden="true" class="mx-1.5 text-muted-foreground" />
      }
    }
  `,
})
export class LocationPathComponent {
  path = input.required<LocationPathView>();
  /**
   * Fired on a crumb click that will actually navigate this tab, before the router runs — a host
   * sheet/panel listens so it can close itself first (quick panels don't close on navigation, and
   * Browse reuses one page instance across same-kind drills, so a still-open panel would strand
   * over the new place).
   */
  navigated = output<void>();

  /**
   * Mirrors `RouterLink.onClick`'s own bail-out: a modified or non-primary click is the browser's
   * open-in-new-tab/window gesture, which leaves this tab where it is. Emitting there would close
   * the host panel over an unchanged page — and discard any inline draft it was holding.
   */
  protected onCrumbClick(event: MouseEvent): void {
    if (event.button !== 0 || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) {
      return;
    }
    this.navigated.emit();
  }
}
