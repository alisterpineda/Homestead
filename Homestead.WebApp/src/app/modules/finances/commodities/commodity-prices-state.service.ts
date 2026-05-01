import { Injectable } from '@angular/core';
import type { Params } from '@angular/router';

@Injectable()
export class CommodityPricesStateService {
  private savedParams: Params | null = null;

  saveParams(params: Params): void {
    this.savedParams = { ...params };
  }

  getSavedParams(): Params | null {
    return this.savedParams;
  }

  get hasParams(): boolean {
    return this.savedParams !== null && Object.keys(this.savedParams).length > 0;
  }
}
