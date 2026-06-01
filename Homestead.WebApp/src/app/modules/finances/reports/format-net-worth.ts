import { MASK_PLACEHOLDER, maskDigitPart } from '../shared/format-currency';

// Symbol-only formatter used by the reports headline, chart axis, and tooltip.
// Unlike `formatMaskedCurrency`, the caller already has the resolved currency
// symbol (from the API response) rather than a CommodityResponse.
export function formatNetWorthValue(
  value: number,
  symbol: string | null,
  masked: boolean,
  opts: { minFraction: number; maxFraction: number },
): string {
  if (!masked) {
    return new Intl.NumberFormat(undefined, {
      style: symbol ? 'currency' : 'decimal',
      currency: symbol ?? undefined,
      minimumFractionDigits: opts.minFraction,
      maximumFractionDigits: opts.maxFraction,
    }).format(value);
  }
  // Masked output is a fixed shape independent of the real value: format the
  // constant MASK_PLACEHOLDER padded to the context's full precision (so the
  // headline/tooltip show "$•.••" and the axis shows "$•") and bullet its parts.
  return new Intl.NumberFormat(undefined, {
    style: symbol ? 'currency' : 'decimal',
    currency: symbol ?? undefined,
    minimumFractionDigits: opts.maxFraction,
    maximumFractionDigits: opts.maxFraction,
  })
    .formatToParts(MASK_PLACEHOLDER)
    .map(maskDigitPart)
    .join('');
}
