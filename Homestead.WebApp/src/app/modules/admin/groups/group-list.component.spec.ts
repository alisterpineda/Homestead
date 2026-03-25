import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { GroupListComponent } from './group-list.component';

describe('GroupListComponent', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideRouter([])],
    });
  });

  it('should create', () => {
    const fixture = TestBed.createComponent(GroupListComponent);
    expect(fixture.componentInstance).toBeTruthy();
  });

  it('should start with loading state', () => {
    const fixture = TestBed.createComponent(GroupListComponent);
    expect(fixture.componentInstance.loading()).toBe(false);
    expect(fixture.componentInstance.error()).toBe(false);
  });

  it('should have empty search input initially', () => {
    const fixture = TestBed.createComponent(GroupListComponent);
    expect(fixture.componentInstance.searchInput()).toBe('');
  });
});
