import type AxeBuilder from '@axe-core/playwright';
import { expect } from '@playwright/test';

/** An axe rule a page is known to break, with the reason it's tolerated for now. */
export interface KnownViolation {
  rule: string;
  reason: string;
  /** Limit to one layout when the offending element only renders there. */
  on?: 'desktop' | 'phones';
}

/**
 * Scans the page and fails on every WCAG 2.1 A/AA violation that isn't listed as known. The
 * failure lists rule, impact and offending elements, so a new violation reads straight off the
 * report; a known one that has been fixed is flagged too, so the list can't go stale.
 */
export async function expectNoA11yViolations(
  axe: AxeBuilder,
  { known = [], isMobile }: { known?: KnownViolation[]; isMobile: boolean },
): Promise<void> {
  const layout = isMobile ? 'phones' : 'desktop';
  known = known.filter((k) => !k.on || k.on === layout);
  const { violations } = await axe.analyze();
  const knownRules = new Set(known.map((k) => k.rule));

  const unexpected = violations
    .filter((v) => !knownRules.has(v.id))
    .map((v) => `${v.id} [${v.impact}] ${v.help} — ${v.nodes.map((n) => n.target.join(' ')).join(', ')}`);
  expect(unexpected, 'accessibility violations').toEqual([]);

  const fixed = known.filter((k) => !violations.some((v) => v.id === k.rule)).map((k) => k.rule);
  expect(fixed, 'known violations that no longer occur — remove them from the list').toEqual([]);
}
