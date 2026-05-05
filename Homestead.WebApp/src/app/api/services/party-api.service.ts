import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { ApiConfiguration } from '@/api/generated/api-configuration';
import { getParties } from '@/api/generated/fn/documents/get-parties';
import { getPartiesByIds } from '@/api/generated/fn/documents/get-parties-by-ids';
import { createParty } from '@/api/generated/fn/documents/create-party';
import { updateParty } from '@/api/generated/fn/documents/update-party';
import { bulkDeleteParties } from '@/api/generated/fn/documents/bulk-delete-parties';
import type { PaginatedResponseOfGetPartiesResponse } from '@/api/generated/models/paginated-response-of-get-parties-response';
import type { GetPartiesByIdsResponse } from '@/api/generated/models/get-parties-by-ids-response';
import type { CreatePartyResponse } from '@/api/generated/models/create-party-response';
import type { UpdatePartyResponse } from '@/api/generated/models/update-party-response';
import type { BulkDeletePartiesResponse } from '@/api/generated/models/bulk-delete-parties-response';

export type { GetParties$Params } from '@/api/generated/fn/documents/get-parties';
export type { GetPartiesByIds$Params } from '@/api/generated/fn/documents/get-parties-by-ids';
export type { CreateParty$Params } from '@/api/generated/fn/documents/create-party';
export type { UpdateParty$Params } from '@/api/generated/fn/documents/update-party';
export type { BulkDeleteParties$Params } from '@/api/generated/fn/documents/bulk-delete-parties';

@Injectable({ providedIn: 'root' })
export class PartyApiService {
  private http = inject(HttpClient);
  private config = inject(ApiConfiguration);

  getParties(params: Parameters<typeof getParties>[2]): Observable<PaginatedResponseOfGetPartiesResponse> {
    return getParties(this.http, this.config.rootUrl, params).pipe(map(r => r.body));
  }

  getPartiesByIds(params: Parameters<typeof getPartiesByIds>[2]): Observable<Array<GetPartiesByIdsResponse>> {
    return getPartiesByIds(this.http, this.config.rootUrl, params).pipe(map(r => r.body));
  }

  createParty(params: Parameters<typeof createParty>[2]): Observable<CreatePartyResponse> {
    return createParty(this.http, this.config.rootUrl, params).pipe(map(r => r.body));
  }

  updateParty(params: Parameters<typeof updateParty>[2]): Observable<UpdatePartyResponse> {
    return updateParty(this.http, this.config.rootUrl, params).pipe(map(r => r.body));
  }

  bulkDeleteParties(params: Parameters<typeof bulkDeleteParties>[2]): Observable<BulkDeletePartiesResponse> {
    return bulkDeleteParties(this.http, this.config.rootUrl, params).pipe(map(r => r.body));
  }
}
