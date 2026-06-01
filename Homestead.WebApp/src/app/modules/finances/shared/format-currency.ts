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

// HTML <input type="number"> step matching a commodity's display precision.
// CAD (precision 2) → "0.01"; VEQT (precision 4) → "0.0001"; integer → "1".
export function commodityStep(commodity: CommodityResponse | null): string {
  if (!commodity) return '0.01';
  const precision = Math.max(0, commodity.displayPrecision ?? 2);
  return precision === 0 ? '1' : `0.${'0'.repeat(precision - 1)}1`;
}
