import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { GroupSelectComponent } from './group-select.component';

describe('GroupSelectComponent', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient()],
    });
  });

  it('should create', () => {
    const fixture = TestBed.createComponent(GroupSelectComponent);
    expect(fixture.componentInstance).toBeTruthy();
  });

  it('should start with empty groups', () => {
    const fixture = TestBed.createComponent(GroupSelectComponent);
    expect(fixture.componentInstance.groups()).toEqual([]);
  });

  it('should have default placeholder', () => {
    const fixture = TestBed.createComponent(GroupSelectComponent);
    expect(fixture.componentInstance.placeholder()).toBe('Filter by group');
  });
});
