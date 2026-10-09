/**
 * Guards icon sizing after the vendored `HlmIcon` directive (`<ng-icon hlm size="sm">`) was retired.
 *
 * Icons are sized the way the official Spartan skill's `rules/icons.md` says: by font-size, e.g.
 * `class="text-[length:--spacing(4)]"`, or contextually by the helm parent. The old habit fails
 * silently rather than at build time: with the directive gone, a stray `hlm` is just an inert
 * attribute, and a `sm` size token lands on NgIcon's own `size` input, which passes it through as
 * `--ng-icon__size: sm`. That invalid length defeats the `1em` fallback, so the icon collapses to
 * 0×0 and disappears. Assert the templates statically instead.
 */
import { readdirSync, readFileSync } from 'node:fs';
import { join, relative } from 'node:path';

import { describe, expect, it } from 'vitest';

import { projectRoot } from '@/shared/testing/project-root';

function templateSources(dir: string): string[] {
  return readdirSync(dir, { withFileTypes: true }).flatMap((entry) => {
    const path = join(dir, entry.name);
    if (entry.isDirectory()) return templateSources(path);
    return /\.(html|ts)$/.test(entry.name) && !entry.name.endsWith('.spec.ts') ? [path] : [];
  });
}

/**
 * Every `<ng-icon …>` opening tag in a template source. Quoted values are consumed whole, so a `>`
 * inside a binding (`"count() > 0"`) or a Tailwind variant (`[&>svg]:…`) doesn't end the tag.
 */
function iconTags(source: string): string[] {
  return [...source.matchAll(/<ng-icon\b(?:[^>"']|"[^"]*"|'[^']*')*>/g)].map((m) => m[0]);
}

/** Attribute names and unquoted values of one tag, whichever quote style and `=` spacing it uses. */
function attributes(tag: string): { name: string; value: string }[] {
  const body = tag.replace(/^<ng-icon\b/, '');
  return [...body.matchAll(/([^\s=/>"']+)(?:\s*=\s*(?:"([^"]*)"|'([^']*)'))?/g)].map((m) => ({
    name: m[1],
    value: m[2] ?? m[3] ?? '',
  }));
}

const SIZE_TOKEN = 'xs|sm|base|lg|xl|none';

function hasHlmAttribute(tag: string): boolean {
  return attributes(tag).some(({ name }) => name === 'hlm' || name === 'hlmIcon');
}

/** `size="sm"`, or a `[size]` binding that can yield a token as a string literal. */
function hasSizeToken(tag: string): boolean {
  return attributes(tag).some(
    ({ name, value }) =>
      (name === 'size' && new RegExp(`^\\s*(${SIZE_TOKEN})\\s*$`).test(value)) ||
      (name === '[size]' && new RegExp(`(['"\`])(${SIZE_TOKEN})\\1`).test(value)),
  );
}

describe('ng-icon template scanner', () => {
  it('reads past a > inside an attribute value', () => {
    const [tag] = iconTags('<ng-icon [class.opacity-50]="count() > 0" hlm size="sm" />');
    expect(hasHlmAttribute(tag)).toBe(true);
    expect(hasSizeToken(tag)).toBe(true);
  });

  it.each([
    ['a plain attribute', '<ng-icon name="lucideX" size="sm" />'],
    ['single quotes', `<ng-icon name="lucideX" size='xs' />`],
    ['spaces around =', '<ng-icon name="lucideX" size = "lg" />'],
    ['a bound string literal', `<ng-icon name="lucideX" [size]="'sm'" />`],
    ['a bound conditional of literals', `<ng-icon name="lucideX" [size]="compact() ? 'xs' : 'base'" />`],
  ])('flags a size token given as %s', (_, source) => {
    const [tag] = iconTags(source);
    expect(hasSizeToken(tag)).toBe(true);
  });

  it.each([
    ['a CSS length', '<ng-icon name="lucideX" size="16px" />'],
    ['a bound expression', '<ng-icon name="lucideX" [size]="iconSize()" />'],
    ['a token inside another attribute', '<ng-icon name="lucideSm" class="text-sm" aria-label="size sm" />'],
  ])('does not flag %s', (_, source) => {
    const [tag] = iconTags(source);
    expect(hasSizeToken(tag)).toBe(false);
    expect(hasHlmAttribute(tag)).toBe(false);
  });
});

const root = projectRoot();
const sources = templateSources(join(root, 'src', 'app')).map((file) => ({
  file: relative(root, file),
  source: readFileSync(file, 'utf8'),
}));
const tags = sources.flatMap(({ file, source }) =>
  iconTags(source).map((tag) => ({ file, tag, flat: tag.replace(/\s+/g, ' ') })),
);

describe('ng-icon sizing', () => {
  it('finds the icon templates it is guarding', () => {
    // Parse sanity: an empty scan would make the checks below pass vacuously.
    expect(tags.length).toBeGreaterThan(100);
  });

  it('parses every <ng-icon> tag in full', () => {
    // A tag the scanner can't close (e.g. an unbalanced quote) would be silently skipped.
    const missed = sources.filter(
      ({ source }) => (source.match(/<ng-icon\b/g) ?? []).length !== iconTags(source).length,
    );
    expect(missed.map(({ file }) => file)).toEqual([]);
  });

  it('has no leftover HlmIcon directive attribute', () => {
    const offenders = tags.filter(({ tag }) => hasHlmAttribute(tag));
    expect(offenders.map(({ file, flat }) => `${file}: ${flat}`)).toEqual([]);
  });

  it('has no HlmIcon size token, which collapses the icon to 0×0', () => {
    const offenders = tags.filter(({ tag }) => hasSizeToken(tag));
    expect(offenders.map(({ file, flat }) => `${file}: ${flat}`)).toEqual([]);
  });
});
