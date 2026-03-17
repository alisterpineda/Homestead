import { TestBed, ComponentFixture } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { ApiConfiguration } from '@/api/generated/api-configuration';
import { CreateTagDialogComponent } from './create-tag-dialog.component';

describe('CreateTagDialogComponent', () => {
  let fixture: ComponentFixture<CreateTagDialogComponent>;
  let component: CreateTagDialogComponent;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: ApiConfiguration, useValue: { rootUrl: '' } },
      ],
    });
    fixture = TestBed.createComponent(CreateTagDialogComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  describe('pathControl validators', () => {
    it('should be invalid when empty', () => {
      component.pathControl.setValue('');
      expect(component.pathControl.hasError('required')).toBe(true);
      expect(component.pathControl.invalid).toBe(true);
    });

    it('should be valid with a plain path', () => {
      component.pathControl.setValue('finance');
      expect(component.pathControl.valid).toBe(true);
    });

    it('should allow slashes (nested tag paths)', () => {
      component.pathControl.setValue('finance/invoices');
      expect(component.pathControl.valid).toBe(true);
    });
  });

  describe('toSignal bridge', () => {
    it('path signal reflects FormControl value changes', () => {
      component.pathControl.setValue('finance/invoices');
      expect(component.path()).toBe('finance/invoices');
    });
  });

  describe('state reset on open', () => {
    it('should reset pathControl and mark pristine/untouched', () => {
      component.pathControl.setValue('some-value');
      component.pathControl.markAsDirty();
      component.pathControl.markAsTouched();

      component.onStateChanged('open');

      expect(component.pathControl.value).toBe('');
      expect(component.pathControl.pristine).toBe(true);
      expect(component.pathControl.untouched).toBe(true);
    });

    it('should reset other signals on open', () => {
      component.selectedColor.set('#ff0000');
      component.creating.set(true);
      component.createError.set(true);

      component.onStateChanged('open');

      expect(component.selectedColor()).toBeNull();
      expect(component.creating()).toBe(false);
      expect(component.createError()).toBe(false);
    });
  });

  describe('submit guard', () => {
    it('handleSubmit does nothing when path is empty', () => {
      component.pathControl.setValue('');
      const event = { preventDefault: vi.fn() } as unknown as Event;
      component.handleSubmit(event);
      expect(component.creating()).toBe(false);
    });

    it('handleSubmit does nothing when path is whitespace-only', () => {
      component.pathControl.setValue('   ');
      const event = { preventDefault: vi.fn() } as unknown as Event;
      component.handleSubmit(event);
      expect(component.creating()).toBe(false);
    });
  });
});
