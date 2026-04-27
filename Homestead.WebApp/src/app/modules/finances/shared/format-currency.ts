import type { CommodityResponse } from '@/api/generated/models/commodity-response';
import { toNumberOrNull } from './sign';

// Format a numeric value using a commodity as the unit. For ISO currencies we use
// Intl currency formatting with the symbol; for securities we fall back to a plain
// decimal with the commodity symbol appended. Precision is taken from the commodity's
// displayPrecision unless the caller overrides it.
export function formatCurrency(
  value: number,
  commodity: CommodityResponse,
  locale?: string,
): string {
  const precision = toNumberOrNull(commodity.displayPrecision) ?? 2;
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
