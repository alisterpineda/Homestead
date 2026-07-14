import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { ApiConfiguration } from '@/api/generated/api-configuration';
import { getCategories } from '@/api/generated/fn/finances/get-categories';
import { createCategory } from '@/api/generated/fn/finances/create-category';
import { patchCategory } from '@/api/generated/fn/finances/patch-category';
import { deleteCategory } from '@/api/generated/fn/finances/delete-category';
import type { CategoryResponse } from '@/api/generated/models/category-response';

export type { GetCategories$Params } from '@/api/generated/fn/finances/get-categories';
export type { CreateCategory$Params } from '@/api/generated/fn/finances/create-category';
export type { PatchCategory$Params } from '@/api/generated/fn/finances/patch-category';
export type { DeleteCategory$Params } from '@/api/generated/fn/finances/delete-category';

@Injectable({ providedIn: 'root' })
export class FinancesCategoryApiService {
  private http = inject(HttpClient);
  private config = inject(ApiConfiguration);

  getCategories(params?: Parameters<typeof getCategories>[2]): Observable<CategoryResponse[]> {
    return getCategories(this.http, this.config.rootUrl, params).pipe(map(r => r.body));
  }

  createCategory(params: Parameters<typeof createCategory>[2]): Observable<CategoryResponse> {
    return createCategory(this.http, this.config.rootUrl, params).pipe(map(r => r.body));
  }

  patchCategory(params: Parameters<typeof patchCategory>[2]): Observable<CategoryResponse> {
    return patchCategory(this.http, this.config.rootUrl, params).pipe(map(r => r.body));
  }

  deleteCategory(params: Parameters<typeof deleteCategory>[2]): Observable<void> {
    return deleteCategory(this.http, this.config.rootUrl, params).pipe(map(r => r.body));
  }
}
