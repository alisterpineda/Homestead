/**
 * Guards the app's half of the Spartan theme-token contract.
 *
 * The Spartan preset hands ownership of some Tailwind theme tokens to the app by redirecting them at
 * themselves inside `@theme inline` — `--font-mono: var(--font-mono)`. That *replaces* Tailwind's own
 * default rather than falling back to it, so a redirected token this app never defines resolves to
 * nothing: the generated utility's declaration is invalid at computed-value time and the element
 * silently inherits instead. `font-mono` was dead app-wide for exactly that reason until
 * `--font-mono` was added to `:root`, and the asymmetry is easy to reintroduce — Spartan's theme
 * generator scaffolds a `--font-sans` for you and has no equivalent for anything else it redirects.
 *
 * Asserting the rendered result would mean reading computed style, which jsdom won't resolve custom
 * properties for. So assert the contract statically instead, against whatever the preset redirects
 * *today* — a Spartan upgrade that adds a token to that list fails here rather than silently killing
 * another utility.
 */
import { readFileSync } from 'node:fs';
import { join } from 'node:path';

import { describe, expect, it } from 'vitest';

import { projectRoot } from '@/shared/testing/project-root';

/** Both files explain these very tokens in prose, so a commented-out declaration must not count. */
function stripComments(css: string): string {
  return css.replace(/\/\*[\s\S]*?\*\//g, '');
}

/** `--foo: var(--foo)` — the preset saying "the app owns `--foo`". */
function selfRedirectedTokens(css: string): string[] {
  return [...css.matchAll(/(--[\w-]+)\s*:\s*var\(\s*\1\s*\)\s*;/g)].map((m) => m[1]);
}

/**
 * Custom properties declared in the app's base `:root` block. Deliberately not `:root.dark` — that
 * one only overrides colours, and a token defined *only* there would be undefined in light mode.
 */
function baseRootTokens(css: string): Set<string> {
  const block = /(?:^|\n):root\s*\{([^}]*)\}/.exec(css);
  expect(block, 'styles.css should declare an unlayered `:root` block').not.toBeNull();
  return new Set([...block![1].matchAll(/(--[\w-]+)\s*:/g)].map((m) => m[1]));
}

const root = projectRoot();
const presetPath = join(root, 'node_modules', '@spartan-ng', 'brain', 'hlm-tailwind-preset.css');
const presetCss = stripComments(readFileSync(presetPath, 'utf8'));
const stylesCss = stripComments(readFileSync(join(root, 'src', 'styles.css'), 'utf8'));

describe('styles.css theme tokens', () => {
  it('defines every token the Spartan preset redirects back at the app', () => {
    const redirected = [...new Set(selfRedirectedTokens(presetCss))];
    // Parse sanity: if the preset ever stops using this shape, the check must not pass vacuously.
    expect(redirected).toContain('--font-sans');
    expect(redirected).toContain('--font-mono');

    const defined = baseRootTokens(stylesCss);
    expect(redirected.filter((token) => !defined.has(token))).toEqual([]);
  });
});
