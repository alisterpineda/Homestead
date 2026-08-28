import { Component, ChangeDetectionStrategy, computed, input } from '@angular/core';
import {
  CRUMB_CLASSES,
  PATH_HOST_CLASSES,
  PATH_PREFIX_CLASSES,
  PATH_SEPARATOR,
  PATH_SEPARATOR_CLASSES,
  locationPathText,
  renderCrumbs,
  type LocationPathView,
  type RenderedCrumb,
} from './location-path';

/**
 * A resolved location path as plain text on ONE line, always. On narrow-by-construction surfaces
 * (the quick-panel sheet, the list column) four or more segments collapse to `root › … › leaf`
 * ({@link renderCrumbs}); full-width surfaces pass `[collapse]="false"` and show every segment.
 * Whatever is shown has to fit a width the caller chose, so the host is a flex row of
 * per-segment truncating boxes rather than one
 * end-truncated string: width is given up in priority order **leaf > root > middle** (shrink
 * weights ~100× apart), because the leaf is the thing you're looking for and the root says where.
 * The root keeps a 5ch floor so a long one degrades to `QA N…` rather than vanishing. Two ellipses
 * can therefore sit next to each other (`QA Nor… › … › Shelf`) — that is the design, not a bug —
 * and the full, uncollapsed path is always the native `title` tooltip, so nothing is unreachable.
 *
 * The ellipsis is a literal `…` crumb rather than `HlmBreadcrumbEllipsis`: that one is a
 * `flex size-5` box wrapping an `ng-icon`, which neither sits in an inline truncating flow nor
 * escapes the icon-baseline problem this component used to carry CSS for.
 *
 * No `<nav>` and no links. Per-segment router links were removed deliberately — one link per
 * segment is what forced deep paths to wrap onto several lines — and navigation to a place goes
 * through Browse instead. The host carries NO colour or size of its own, so the leaf inherits the
 * caller's (the durable detail headline mutes the whole line when Unplaced); ancestors and the
 * ellipsis are muted here.
 */
@Component({
  selector: 'app-location-path',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {
    // One line, always: a flex row whose segments truncate individually; min-w-0 so it can shrink
    // inside a flex row of its own. Bound rather than static so the literal is the shared one.
    '[class]': 'hostClasses',
    '[title]': 'fullText()',
  },
  template: `
    @if (path().prefix; as prefix) {
      <span [class]="prefixClasses">{{ prefix }}&nbsp;</span>
    }
    @for (crumb of visible(); track $index; let last = $last) {
      <span [class]="classes(crumb)">{{ crumb.label }}</span>
      @if (!last) {
        <span [class]="separatorClasses" aria-hidden="true">{{ separator }}</span>
      }
    }
  `,
})
export class LocationPathComponent {
  path = input.required<LocationPathView>();
  /** Off on full-width surfaces (detail pages), where hiding the middle would hide it for nothing. */
  collapse = input(true);
  protected visible = computed(() => renderCrumbs(this.path().crumbs, this.collapse()));
  protected fullText = computed(() => locationPathText(this.path()));
  protected readonly hostClasses = PATH_HOST_CLASSES;
  protected readonly prefixClasses = PATH_PREFIX_CLASSES;
  protected readonly separatorClasses = PATH_SEPARATOR_CLASSES;
  protected readonly separator = PATH_SEPARATOR;

  protected classes(crumb: RenderedCrumb): string {
    return CRUMB_CLASSES[crumb.role] + (crumb.muted ? ' text-muted-foreground' : '');
  }
}
