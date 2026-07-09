import { TestBed, ComponentFixture } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { ColorPickerComponent, PRESET_COLORS } from './color-picker.component';
import { isLightColor } from '@/shared/utils/color-utils';

describe('ColorPickerComponent', () => {
  let fixture: ComponentFixture<ColorPickerComponent>;
  let component: ColorPickerComponent;

  function create(color: string | null = null): void {
    fixture = TestBed.createComponent(ColorPickerComponent);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('color', color);
    fixture.detectChanges();
  }

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideZonelessChangeDetection()],
    });
  });

  describe('PRESET_COLORS', () => {
    it('exposes twelve presets including Red and Blue', () => {
      expect(PRESET_COLORS.length).toBe(12);
      expect(PRESET_COLORS).toContainEqual({ name: 'Red', hex: '#ef4444' });
      expect(PRESET_COLORS).toContainEqual({ name: 'Blue', hex: '#3b82f6' });
    });
  });

  describe('isCustomColor', () => {
    it('is false when no color is set', () => {
      create(null);
      expect(component.isCustomColor()).toBe(false);
    });

    it('is false for a preset colour', () => {
      create('#ef4444');
      expect(component.isCustomColor()).toBe(false);
    });

    it('is true for a non-preset colour', () => {
      create('#123456');
      expect(component.isCustomColor()).toBe(true);
    });
  });

  describe('selectColor', () => {
    it('emits the chosen hex', () => {
      create();
      let emitted: string | null | undefined;
      component.colorChange.subscribe(v => (emitted = v));
      component.selectColor('#22c55e');
      expect(emitted).toBe('#22c55e');
    });

    it('emits null when cleared', () => {
      create('#22c55e');
      let emitted: string | null | undefined = 'unset';
      component.colorChange.subscribe(v => (emitted = v));
      component.selectColor(null);
      expect(emitted).toBeNull();
    });
  });

  describe('onCustomColorInput', () => {
    it('emits the input element value', () => {
      create();
      let emitted: string | null | undefined;
      component.colorChange.subscribe(v => (emitted = v));
      component.onCustomColorInput({ target: { value: '#abcdef' } } as unknown as Event);
      expect(emitted).toBe('#abcdef');
    });
  });

  describe('preset button (DOM)', () => {
    it('emits colorChange with the preset hex when clicked', () => {
      create();
      let emitted: string | null | undefined;
      component.colorChange.subscribe(v => (emitted = v));

      const redBtn = fixture.nativeElement.querySelector('[aria-label="Red"]');
      expect(redBtn).toBeTruthy();
      redBtn.click();
      expect(emitted).toBe('#ef4444');
    });
  });

  describe('clear button (DOM)', () => {
    it('is absent when no color is set', () => {
      create(null);
      expect(fixture.nativeElement.querySelector('[title="Remove color"]')).toBeFalsy();
    });

    it('renders when a color is set and emits null on click', () => {
      create('#ef4444');
      let emitted: string | null | undefined = 'unset';
      component.colorChange.subscribe(v => (emitted = v));

      const clearBtn = fixture.nativeElement.querySelector('[title="Remove color"]');
      expect(clearBtn).toBeTruthy();
      clearBtn.click();
      expect(emitted).toBeNull();
    });
  });

  describe('isLight contrast wiring', () => {
    it('is the imported isLightColor helper', () => {
      create();
      expect(component.isLight).toBe(isLightColor);
      expect(component.isLight('#ffffff')).toBe(true);
      expect(component.isLight('#000000')).toBe(false);
    });

    it('marks the selected preset with a check icon', () => {
      create('#ef4444');
      const redBtn = fixture.nativeElement.querySelector('[aria-label="Red"]');
      expect(redBtn.querySelector('ng-icon')).toBeTruthy();
    });
  });
});
