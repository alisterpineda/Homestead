import { rxResource } from '@angular/core/rxjs-interop';
import { of, catchError } from 'rxjs';
import { FinancesCommodityApiService } from '@/api/services/finances-commodity-api.service';
import type { CommodityResponse } from '@/api/generated/models/commodity-response';

/**
 * Resource that fetches a `CommodityResponse` by id whenever both the id is set
 * and `isOpen` is true. Falls back to `null` on error or while gated closed —
 * idiomatic for sheet/dialog components that should not fetch while closed.
 */
export function commodityResource(
  api: FinancesCommodityApiService,
  id: () => string | null | undefined,
  isOpen: () => boolean,
) {
  return rxResource<CommodityResponse | null, { id: string | null; open: boolean }>({
    params: () => ({ id: id() ?? null, open: isOpen() }),
    stream: ({ params }) =>
      params.open && params.id
        ? api.getCommodity({ id: params.id }).pipe(catchError(() => of(null)))
        : of(null),
    defaultValue: null,
  });
}
