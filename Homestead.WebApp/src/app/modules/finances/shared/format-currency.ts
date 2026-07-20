import type { CommodityResponse } from '@/api/generated/models/commodity-response';

// Format a numeric value using a commodity as the unit. For ISO currencies we use
// Intl currency formatting with the symbol; for securities we fall back to a plain
// decimal with the commodity symbol appended. Precision is taken from the commodity's
// displayPrecision unless the caller overrides it.
export function formatCurrency(
  value: number,
  commodity: CommodityResponse,
  locale?: string,
): string {
  const precision = commodity.displayPrecision ?? 2;
  if (commodity.kind === 'Currency') {
    try {
      return new Intl.NumberFormat(locale, {
        style: 'currency',
        currency: commodity.symbol,
        minimumFractionDigits: precision,
        maximumFractionDigits: precision,
      }).format(value);
    } catch {
      // Unknown currency code (shouldn't happen for ISO 4217, but defensive).
    }
  }
  const formatted = new Intl.NumberFormat(locale, {
    minimumFractionDigits: precision,
    maximumFractionDigits: precision,
  }).format(value);
  return `${formatted} ${commodity.symbol}`;
}

// Constant placeholder formatted in place of the real value when masking. Zero
// has exactly one integer digit and never triggers grouping separators, so the
// masked output is a fixed shape (one integer bullet + `precision` fraction
// bullets) that is independent of the real value's magnitude or sign — an
// onlooker can't count bullets or separators to infer the order of magnitude.
export const MASK_PLACEHOLDER = 0;

// Render a fixed-shape masked value: the real value's digits are NOT shown.
// Instead we format MASK_PLACEHOLDER at the commodity's precision and bullet its
// parts, preserving the currency symbol and decimal mark so the result still
// reads as currency at a glance (e.g. "CA$•.••", "¥•", "•.•••• VEQT").
export function formatMaskedCurrency(
  value: number,
  commodity: CommodityResponse,
  masked: boolean,
  locale?: string,
): string {
  if (!masked) return formatCurrency(value, commodity, locale);
  // Masked output is intentionally value-independent — `value` is unused below.
  const precision = commodity.displayPrecision ?? 2;
  if (commodity.kind === 'Currency') {
    try {
      const parts = new Intl.NumberFormat(locale, {
        style: 'currency',
        currency: commodity.symbol,
        minimumFractionDigits: precision,
        maximumFractionDigits: precision,
      }).formatToParts(MASK_PLACEHOLDER);
      return parts.map(maskDigitPart).join('');
    } catch {
      // Unknown currency code (shouldn't happen for ISO 4217, but defensive).
    }
  }
  const parts = new Intl.NumberFormat(locale, {
    minimumFractionDigits: precision,
    maximumFractionDigits: precision,
  }).formatToParts(MASK_PLACEHOLDER);
  return `${parts.map(maskDigitPart).join('')} ${commodity.symbol}`;
}

// Sign chars are stripped so the masked output is constant-width and doesn't
// reveal whether the value is negative.
export function maskDigitPart(part: Intl.NumberFormatPart): string {
  if (part.type === 'integer' || part.type === 'fraction') {
    return '•'.repeat(part.value.length);
  }
  if (part.type === 'minusSign' || part.type === 'plusSign') return '';
  return part.value;
}

// Decimal places a money input should settle on when it loses focus (see the appMoneyInput
// directive) — the commodity's displayPrecision, but ONLY for ISO currencies.
//
// Securities deliberately return null (= don't touch the digits). displayPrecision is a *display*
// concern: it's how many places we render in a table, not how many the value is allowed to carry.
// Rounding a rendered figure is lossless; rounding what the user typed is not. A currency amount is
// safe because the minor unit really is the smallest denomination — a CAD figure cannot have a third
// decimal. A holding of 0.123456789 units of an ETF can, so forcing it to the security's 4-place
// display precision would silently discard what the reviewer entered.
//
// Per-unit prices and FX rates get no precision at all at their call sites, for the same reason —
// even though their unit IS a currency, a security priced in CAD can legitimately quote more than
// two decimals.
export function currencyInputPrecision(commodity: CommodityResponse | null | undefined): number | null {
  if (!commodity || commodity.kind !== 'Currency') return null;
  return Math.max(0, commodity.displayPrecision ?? 2);
}

// Settle a raw money-input string at `precision` decimal places: "1.2" → "1.20" for CAD, "1" for JPY
// (precision 0), "1.2000" at precision 4.
//
// Two deliberate pass-throughs. An empty string stays empty — an untouched optional field must not
// materialize a "0.00" the user never entered. Anything that doesn't parse as a finite number is
// returned VERBATIM rather than blanked or zeroed: the typo stays on screen where its owner can see
// and fix it, and the surrounding validity checks (which already treat a non-numeric amount as
// incomplete) keep it out of a commit.
export function formatAmountInput(raw: string, precision: number | null | undefined): string {
  if (precision == null) return raw;
  const trimmed = raw.trim();
  if (trimmed === '') return '';
  const parsed = Number(trimmed);
  if (!Number.isFinite(parsed)) return raw;
  const fixed = parsed.toFixed(precision);
  // Test the ROUNDED result, not the input: "-0.001" at precision 2 is not === 0, but toFixed renders
  // it "-0.00". Collapse any such negative zero to plain "0.00" — a signed zero is nonsense in a money
  // field and would read as a negative amount at a glance.
  return Number(fixed) === 0 ? (0).toFixed(precision) : fixed;
}
