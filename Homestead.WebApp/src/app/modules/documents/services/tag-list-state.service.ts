import { Injectable } from '@angular/core';
import type { Params } from '@angular/router';
import type { VisibilityState } from '@tanstack/angular-table';

@Injectable({ providedIn: 'root' })
export class TagListStateService {
  private savedParams: Params | null = null;
  private savedColumnVisibility: VisibilityState | null = null;

  saveParams(params: Params): void {
    this.savedParams = { ...params };
  }

  getSavedParams(): Params | null {
    return this.savedParams;
  }

  saveColumnVisibility(state: VisibilityState): void {
    this.savedColumnVisibility = { ...state };
  }

  getSavedColumnVisibility(): VisibilityState | null {
    return this.savedColumnVisibility;
  }

  get hasParams(): boolean {
    return this.savedParams !== null && Object.keys(this.savedParams).length > 0;
  }
}
