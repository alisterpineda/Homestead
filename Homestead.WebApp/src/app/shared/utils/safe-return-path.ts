/**
 * Returns true iff `path` is a safe same-origin return path —
 * i.e. it cannot be coerced into a cross-origin redirect when concatenated
 * onto `window.location.origin`. Mirrors the server-side
 * `OidcLoginEndpoint.IsSafeReturnUrl` path check, plus a backslash guard.
 */
export function isSafeReturnPath(path: string | null | undefined): path is string {
  if (!path) return false;
  if (!path.startsWith('/')) return false;
  if (path.startsWith('//')) return false;
  if (path.startsWith('/\\')) return false;
  return true;
}
