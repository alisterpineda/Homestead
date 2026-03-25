import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { CreateGroupDialogComponent } from './create-group-dialog.component';

describe('CreateGroupDialogComponent', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient()],
    });
  });

  it('should create', () => {
    const fixture = TestBed.createComponent(CreateGroupDialogComponent);
    expect(fixture.componentInstance).toBeTruthy();
  });

  it('should not be submittable with empty name', () => {
    const fixture = TestBed.createComponent(CreateGroupDialogComponent);
    expect(fixture.componentInstance.canSubmit()).toBe(false);
  });

  it('should be submittable with non-empty name', () => {
    const fixture = TestBed.createComponent(CreateGroupDialogComponent);
    fixture.componentInstance.name.set('My Group');
    expect(fixture.componentInstance.canSubmit()).toBe(true);
  });

  it('should not be submittable with whitespace-only name', () => {
    const fixture = TestBed.createComponent(CreateGroupDialogComponent);
    fixture.componentInstance.name.set('   ');
    expect(fixture.componentInstance.canSubmit()).toBe(false);
  });
});
