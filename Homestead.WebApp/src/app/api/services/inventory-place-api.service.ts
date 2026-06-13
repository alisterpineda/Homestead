import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { ApiConfiguration } from '@/api/generated/api-configuration';
import { getPlaceContents, type GetPlaceContents$Params } from '@/api/generated/fn/inventory/get-place-contents';
import type { PlaceContentsResponse } from '@/api/generated/models/place-contents-response';

@Injectable({ providedIn: 'root' })
export class InventoryPlaceApiService {
  private http = inject(HttpClient);
  private config = inject(ApiConfiguration);

  getPlaceContents(params: GetPlaceContents$Params): Observable<PlaceContentsResponse> {
    return getPlaceContents(this.http, this.config.rootUrl, params).pipe(map(r => r.body));
  }
}
