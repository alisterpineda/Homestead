import { Component, ChangeDetectionStrategy, input, output } from '@angular/core';
import { RouterLink } from '@angular/router';
import type { LocationPathView } from './location-path';

/**
 * A resolved location path rendered as inline links — one per segment, each targeting the place that
 * segment names ({@link segmentToLink}). Used where the path is the SUBJECT of the line (the durable
 * detail headline, the durable quick-panel Location card, the consumable detail by-location table);
 * surfaces where it is a secondary column keep the plain text from `formatPath`.
 *
 * Deliberately NOT built on `HlmBreadcrumb`: that hard-codes a `role="navigation"` landmark and a
 * `text-sm text-muted-foreground` scale, which would fight both the detail headline and the panel
 * card — and the Browse header stays the only breadcrumb landmark in the module. The host is
 * `display: inline` so the path inherits the caller's type scale.
 */
@Component({
  selector: 'app-location-path',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink],
  styles: `:host { display: inline; min-width: 0; }`,
  template: `
    @if (path().prefix; as prefix) {
      <span>{{ prefix }}&nbsp;</span>
    }
    @for (crumb of path().crumbs; track $index; let last = $last) {
      @if (crumb.link; as link) {
        <a
          [routerLink]="link"
          class="hover:underline"
          [class.text-muted-foreground]="crumb.muted"
          (click)="onCrumbClick($event)"
        >{{ crumb.label }}</a>
      } @else {
        <span [class.text-muted-foreground]="crumb.muted">{{ crumb.label }}</span>
      }
      @if (!last) {
        <span aria-hidden="true" class="text-muted-foreground"> › </span>
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
