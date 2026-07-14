import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { lastModuleRedirect } from './last-module-redirect';
import { UserPreferencesService } from '@/shared/services/user-preferences.service';

describe('lastModuleRedirect', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideRouter([])],
    });
  });

  it('returns the recorded last module path', () => {
    TestBed.inject(UserPreferencesService).lastModulePath.set('/finances');

    const result = TestBed.runInInjectionContext(() => lastModuleRedirect());

    expect(result).toBe('/finances');
  });

  it('falls back to /documents when nothing is recorded', () => {
    const result = TestBed.runInInjectionContext(() => lastModuleRedirect());

    expect(result).toBe('/documents');
  });
});
