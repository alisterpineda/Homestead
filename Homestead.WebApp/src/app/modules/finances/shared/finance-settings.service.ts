import { Injectable, computed, inject, signal } from '@angular/core';
import { Observable, catchError, finalize, of, switchMap, tap } from 'rxjs';
import { FinancesSettingsApiService } from '@/api/services/finances-settings-api.service';
import { FinancesCommodityApiService } from '@/api/services/finances-commodity-api.service';
import { replace } from '@/shared/json-patch';
import type { CommodityResponse } from '@/api/generated/models/commodity-response';

@Injectable({ providedIn: 'root' })
export class FinanceSettingsService {
  private settingsApi = inject(FinancesSettingsApiService);
  private commodityApi = inject(FinancesCommodityApiService);

  private readonly _displayCurrencyId = signal<string | null>(null);
  private readonly _displayCurrency = signal<CommodityResponse | null>(null);
  private readonly _loaded = signal(false);
  private readonly _loading = signal(false);

  readonly displayCurrencyId = this._displayCurrencyId.asReadonly();
  readonly displayCurrency = this._displayCurrency.asReadonly();
  readonly loaded = this._loaded.asReadonly();
  readonly hasDisplayCurrency = computed(() => this._displayCurrencyId() !== null);

  load(): void {
    if (this._loaded() || this._loading()) return;
    this._loading.set(true);
    this.settingsApi.getFinanceSettings().pipe(
      switchMap(settings => this.resolveCurrency(settings.displayCurrencyId ?? null).pipe(
        tap(commodity => {
          this._displayCurrencyId.set(settings.displayCurrencyId ?? null);
          this._displayCurrency.set(commodity);
          this._loaded.set(true);
        }),
      )),
      catchError(() => {
        this._loaded.set(true);
        return of(null);
      }),
      finalize(() => this._loading.set(false)),
    ).subscribe();
  }

  setDisplayCurrencyId(id: string): Observable<CommodityResponse> {
    return this.settingsApi.patchFinanceSettings({
      body: [replace('/displayCurrencyId', id)],
    }).pipe(
      switchMap(() => this.commodityApi.getCommodity({ id })),
      tap(commodity => {
        this._displayCurrencyId.set(id);
        this._displayCurrency.set(commodity);
        this._loaded.set(true);
      }),
    );
  }

  private resolveCurrency(id: string | null): Observable<CommodityResponse | null> {
    if (id === null) return of(null);
    return this.commodityApi.getCommodity({ id }).pipe(catchError(() => of(null)));
  }
}
