import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideZonelessChangeDetection } from '@angular/core';
import { CommodityPickerComponent } from './commodity-picker.component';

describe('CommodityPickerComponent', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideZonelessChangeDetection(), provideHttpClient()],
    });
  });

  it('should create', () => {
    const fixture = TestBed.createComponent(CommodityPickerComponent);
    expect(fixture.componentInstance).toBeTruthy();
  });

  it('uses a kind-specific search placeholder', () => {
    const fixture = TestBed.createComponent(CommodityPickerComponent);
    fixture.componentRef.setInput('kind', 'Currency');
    expect(fixture.componentInstance.searchPlaceholder()).toBe('Search currencies...');
    fixture.componentRef.setInput('kind', 'Security');
    expect(fixture.componentInstance.searchPlaceholder()).toBe('Search securities...');
    fixture.componentRef.setInput('kind', null);
    expect(fixture.componentInstance.searchPlaceholder()).toBe('Search commodities...');
  });
});
