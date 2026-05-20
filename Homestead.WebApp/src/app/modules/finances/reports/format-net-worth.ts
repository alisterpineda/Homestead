import { maskDigitPart } from '../shared/format-currency';

// Symbol-only formatter used by the reports headline, chart axis, and tooltip.
// Unlike `formatMaskedCurrency`, the caller already has the resolved currency
// symbol (from the API response) rather than a CommodityResponse.
export function formatNetWorthValue(
  value: number,
  symbol: string | null,
  masked: boolean,
  opts: { minFraction: number; maxFraction: number },
): string {
  const fmt = new Intl.NumberFormat(undefined, {
    style: symbol ? 'currency' : 'decimal',
    currency: symbol ?? undefined,
    minimumFractionDigits: opts.minFraction,
    maximumFractionDigits: opts.maxFraction,
  });
  if (!masked) return fmt.format(value);
  return fmt.formatToParts(value).map(maskDigitPart).join('');
}
