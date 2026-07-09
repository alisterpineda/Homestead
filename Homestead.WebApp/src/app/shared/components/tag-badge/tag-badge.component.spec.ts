import { TestBed, ComponentFixture } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { TagBadgeComponent } from './tag-badge.component';

describe('TagBadgeComponent', () => {
  let fixture: ComponentFixture<TagBadgeComponent>;
  let component: TagBadgeComponent;

  /**
   * Create the component with the required `path` input (plus any extra inputs) seeded before the
   * first change detection. Seeding up-front mirrors real usage and avoids the `hlmBadge` directive
   * clobbering the template's `[class.tag-badge-colored]` binding when `color` changes post-render.
   */
  function create(path: string, inputs: Record<string, unknown> = {}): void {
    fixture = TestBed.createComponent(TagBadgeComponent);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('path', path);
    for (const [key, value] of Object.entries(inputs)) {
      fixture.componentRef.setInput(key, value);
    }
    fixture.detectChanges();
  }

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideZonelessChangeDetection()],
    });
  });

  describe('compactPath', () => {
    it('returns a single-segment path unchanged', () => {
      create('a');
      expect(component.compactPath()).toBe('a');
    });

    it('returns a two-segment path in full', () => {
      create('a/b');
      expect(component.compactPath()).toBe('a/b');
    });

    it('collapses the middle of a deeper path to first/…/last', () => {
      create('a/b/c/d');
      expect(component.compactPath()).toBe('a/…/d');
    });
  });

  describe('rendering', () => {
    it('renders the full path by default', () => {
      create('a/b/c/d');
      expect((fixture.nativeElement.textContent ?? '')).toContain('a/b/c/d');
    });

    it('renders the compact path when compact is set', () => {
      create('a/b/c/d', { compact: true });
      expect((fixture.nativeElement.textContent ?? '')).toContain('a/…/d');
    });
  });

  describe('colored branch', () => {
    it('applies the colored class when a color is set and not AI-suggested', () => {
      create('finance', { color: '#ff0000' });
      expect(fixture.nativeElement.querySelector('.tag-badge-colored')).toBeTruthy();
    });

    it('does not apply the colored class when no color is set', () => {
      create('finance');
      expect(fixture.nativeElement.querySelector('.tag-badge-colored')).toBeFalsy();
    });
  });

  describe('removable branch', () => {
    it('renders a Remove button that emits removed on click', () => {
      create('finance', { removable: true });

      let removed = false;
      component.removed.subscribe(() => (removed = true));

      const btn = fixture.nativeElement.querySelector('[aria-label="Remove tag"]');
      expect(btn).toBeTruthy();
      btn.click();
      expect(removed).toBe(true);
    });

    it('renders no action button by default', () => {
      create('finance');
      expect(fixture.nativeElement.querySelector('button')).toBeFalsy();
    });
  });

  describe('AI-suggested (non-transient) branch', () => {
    it('renders approve/decline buttons (no remove) that emit on click', () => {
      create('finance', { aiSuggested: true });

      let approved = false;
      let declined = false;
      component.approved.subscribe(() => (approved = true));
      component.declined.subscribe(() => (declined = true));

      const approveBtn = fixture.nativeElement.querySelector('[aria-label="Approve suggestion"]');
      const declineBtn = fixture.nativeElement.querySelector('[aria-label="Decline suggestion"]');
      expect(approveBtn).toBeTruthy();
      expect(declineBtn).toBeTruthy();
      expect(fixture.nativeElement.querySelector('[aria-label="Remove tag"]')).toBeFalsy();

      approveBtn.click();
      declineBtn.click();
      expect(approved).toBe(true);
      expect(declined).toBe(true);
    });
  });

  describe('AI-transient branch', () => {
    it('renders only a Remove button', () => {
      create('finance', { aiSuggested: true, aiTransient: true });

      expect(fixture.nativeElement.querySelector('[aria-label="Remove tag"]')).toBeTruthy();
      expect(fixture.nativeElement.querySelector('[aria-label="Approve suggestion"]')).toBeFalsy();
      expect(fixture.nativeElement.querySelector('[aria-label="Decline suggestion"]')).toBeFalsy();
    });
  });
});
