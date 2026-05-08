import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { ApiConfiguration } from '@/api/generated/api-configuration';
import { getStatus } from '@/api/generated/fn/status/get-status';
import { getUserInfo } from '@/api/generated/fn/identity/get-user-info';
import type { GetStatusResponse } from '@/api/generated/models/get-status-response';
import type { GetUserInfoResponse } from '@/api/generated/models/get-user-info-response';

@Injectable({ providedIn: 'root' })
export class IdentityApiService {
  private http = inject(HttpClient);
  private config = inject(ApiConfiguration);

  getStatus(): Observable<GetStatusResponse> {
    return getStatus(this.http, this.config.rootUrl).pipe(map(r => r.body));
  }

  getUserInfo(): Observable<GetUserInfoResponse> {
    return getUserInfo(this.http, this.config.rootUrl).pipe(map(r => r.body));
  }
}
