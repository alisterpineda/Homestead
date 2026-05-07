import { Component, ChangeDetectionStrategy, computed, input } from '@angular/core';

interface ParsedSnippet {
  beforeHtml: string;
  termHtml: string;
  afterHtml: string;
}

const MARK_OPEN = '<mark>';
const MARK_CLOSE = '</mark>';

function parseSnippet(html: string): ParsedSnippet | null {
  const start = html.indexOf(MARK_OPEN);
  if (start < 0) return null;
  const end = html.indexOf(MARK_CLOSE, start + MARK_OPEN.length);
  if (end < 0) return null;

  return {
    beforeHtml: html.slice(0, start),
    termHtml: html.slice(start + MARK_OPEN.length, end),
    afterHtml: html.slice(end + MARK_CLOSE.length),
  };
}

/**
 * Renders an FTS snippet so the first <mark> is always visible. Leading context
 * shrinks from the start (RTL ellipsis trick); trailing context shrinks from
 * the end. Subsequent marks render in-flow inside the trailing region.
 */
@Component({
  selector: 'app-match-snippet',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'match-snippet' },
  styles: `
    :host {
      display: flex;
      align-items: baseline;
      min-width: 0;
      max-width: 100%;
      overflow: hidden;
      white-space: nowrap;
    }
    .before {
      flex: 0 1 auto;
      min-width: 0;
      overflow: hidden;
      text-overflow: ellipsis;
      direction: rtl;
      text-align: left;
    }
    .before-inner {
      direction: ltr;
      unicode-bidi: bidi-override;
    }
    .after {
      flex: 0 1 auto;
      min-width: 0;
      overflow: hidden;
      text-overflow: ellipsis;
    }
    mark {
      flex: 0 0 auto;
      padding: 0 1px;
      border-radius: 2px;
      font-weight: 600;
      background-color: oklch(var(--primary) / 0.18);
      color: inherit;
    }
  `,
  template: `
    @if (parsed(); as p) {
      @if (p.beforeHtml) {
        <span class="before"><span class="before-inner" [innerHTML]="p.beforeHtml"></span></span>
      }
      <mark [innerHTML]="p.termHtml"></mark>
      @if (p.afterHtml) {
        <span class="after" [innerHTML]="p.afterHtml"></span>
      }
    }
  `,
})
export class MatchSnippetComponent {
  html = input.required<string | null | undefined>();

  protected parsed = computed<ParsedSnippet | null>(() => {
    const raw = this.html();
    return raw ? parseSnippet(raw) : null;
  });
}
