import { TestBed, ComponentFixture } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { ApiConfiguration } from '@/api/generated/api-configuration';
import { CreatePartyDialogComponent } from './create-party-dialog.component';

describe('CreatePartyDialogComponent', () => {
  let fixture: ComponentFixture<CreatePartyDialogComponent>;
  let component: CreatePartyDialogComponent;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: ApiConfiguration, useValue: { rootUrl: '' } },
      ],
    });
    fixture = TestBed.createComponent(CreatePartyDialogComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  describe('nameControl validators', () => {
    it('should be invalid when empty', () => {
      component.nameControl.setValue('');
      expect(component.nameControl.hasError('required')).toBe(true);
      expect(component.nameControl.invalid).toBe(true);
    });

    it('should be valid with a name', () => {
      component.nameControl.setValue('Acme Corp');
      expect(component.nameControl.valid).toBe(true);
    });

    it('should be invalid when name exceeds 256 characters', () => {
      component.nameControl.setValue('a'.repeat(257));
      expect(component.nameControl.hasError('maxlength')).toBe(true);
      expect(component.nameControl.invalid).toBe(true);
    });

    it('should be valid at exactly 256 characters', () => {
      component.nameControl.setValue('a'.repeat(256));
      expect(component.nameControl.valid).toBe(true);
    });
  });

  describe('toSignal bridge', () => {
    it('name signal reflects FormControl value changes', () => {
      component.nameControl.setValue('Test Party');
      expect(component.name()).toBe('Test Party');
    });
  });

  describe('state reset on open', () => {
    it('should reset nameControl and mark pristine/untouched', () => {
      component.nameControl.setValue('some-value');
      component.nameControl.markAsDirty();
      component.nameControl.markAsTouched();

      component.onStateChanged('open');

      expect(component.nameControl.value).toBe('');
      expect(component.nameControl.pristine).toBe(true);
      expect(component.nameControl.untouched).toBe(true);
    });

    it('should reset other signals on open', () => {
      component.description.set('some desc');
      component.creating.set(true);
      component.createError.set(true);

      component.onStateChanged('open');

      expect(component.description()).toBe('');
      expect(component.creating()).toBe(false);
      expect(component.createError()).toBe(false);
    });
  });

  describe('submit guard', () => {
    it('handleSubmit does nothing when name is empty', () => {
      component.nameControl.setValue('');
      const event = { preventDefault: vi.fn() } as unknown as Event;
      component.handleSubmit(event);
      expect(component.creating()).toBe(false);
    });

    it('handleSubmit does nothing when name is whitespace-only', () => {
      component.nameControl.setValue('   ');
      const event = { preventDefault: vi.fn() } as unknown as Event;
      component.handleSubmit(event);
      expect(component.creating()).toBe(false);
    });
  });

  describe('description field', () => {
    it('asInputValue extracts value from event', () => {
      const event = { target: { value: 'hello' } } as unknown as Event;
      expect(component.asInputValue(event)).toBe('hello');
    });
  });
});
