import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideNativeDateAdapter } from '@spartan-ng/brain/date-time';
import { DatePickerComponent } from './date-picker.component';

describe('DatePickerComponent', () => {
  let fixture: ComponentFixture<DatePickerComponent>;
  let component: DatePickerComponent;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideNativeDateAdapter()],
    });
    fixture = TestBed.createComponent(DatePickerComponent);
    component = fixture.componentInstance;
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('should have null date by default', () => {
    expect(component.date()).toBeNull();
  });

  it('should have default placeholder "Select date"', () => {
    expect(component.placeholder()).toBe('Select date');
  });

  it('should start with popover closed', () => {
    expect(component.popoverState()).toBe('closed');
  });

  describe('placeholder', () => {
    it('should display default placeholder when no date is set', () => {
      fixture.detectChanges();
      const trigger = fixture.nativeElement.querySelector('button[hlmPopoverTrigger]');
      expect(trigger.textContent).toContain('Select date');
    });

    it('should display custom placeholder', () => {
      fixture.componentRef.setInput('placeholder', 'Choose a date');
      fixture.detectChanges();
      const trigger = fixture.nativeElement.querySelector('button[hlmPopoverTrigger]');
      expect(trigger.textContent).toContain('Choose a date');
    });
  });

  describe('date display', () => {
    it('should display formatted date when date is set', () => {
      fixture.componentRef.setInput('date', new Date(2026, 2, 4));
      fixture.detectChanges();
      const trigger = fixture.nativeElement.querySelector('button[hlmPopoverTrigger]');
      const text = trigger.textContent;
      expect(text).toContain('2026');
      expect(text).toContain('4');
      expect(text).not.toContain('Select date');
    });

    it('should apply muted foreground class when no date', () => {
      fixture.detectChanges();
      const trigger = fixture.nativeElement.querySelector('button[hlmPopoverTrigger]');
      expect(trigger.classList.contains('text-muted-foreground')).toBe(true);
    });

    it('should not apply muted foreground class when date is set', () => {
      fixture.componentRef.setInput('date', new Date(2026, 0, 1));
      fixture.detectChanges();
      const trigger = fixture.nativeElement.querySelector('button[hlmPopoverTrigger]');
      expect(trigger.classList.contains('text-muted-foreground')).toBe(false);
    });
  });

  describe('displayDate', () => {
    it('should return null when no date is set', () => {
      expect(component.displayDate()).toBeNull();
    });

    it('should format a date with year, short month, and day', () => {
      component.date.set(new Date(2026, 0, 15));
      const result = component.displayDate();
      expect(result).toContain('2026');
      expect(result).toContain('15');
    });
  });

  describe('ariaLabel', () => {
    it('should have no aria-label by default', () => {
      fixture.detectChanges();
      const trigger = fixture.nativeElement.querySelector('button[hlmPopoverTrigger]');
      expect(trigger.getAttribute('aria-label')).toBeNull();
    });

    it('should set aria-label when provided', () => {
      fixture.componentRef.setInput('ariaLabel', 'Document Date');
      fixture.detectChanges();
      const trigger = fixture.nativeElement.querySelector('button[hlmPopoverTrigger]');
      expect(trigger.getAttribute('aria-label')).toBe('Document Date');
    });
  });

  describe('onDateSelected', () => {
    it('should set the date', () => {
      const date = new Date(2026, 5, 15);
      component.onDateSelected(date);
      expect(component.date()).toEqual(date);
    });

    it('should close the popover', () => {
      component.popoverState.set('open');
      component.onDateSelected(new Date(2026, 5, 15));
      expect(component.popoverState()).toBe('closed');
    });
  });

  describe('clearDate', () => {
    it('should set date to null', () => {
      component.date.set(new Date(2026, 5, 15));
      component.clearDate();
      expect(component.date()).toBeNull();
    });

    it('should close the popover', () => {
      component.popoverState.set('open');
      component.clearDate();
      expect(component.popoverState()).toBe('closed');
    });
  });
});
