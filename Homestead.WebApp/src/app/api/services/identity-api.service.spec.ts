import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ApiConfiguration } from '@/api/generated/api-configuration';
import { IdentityApiService } from './identity-api.service';

describe('IdentityApiService', () => {
  let service: IdentityApiService;
  let httpTesting: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: ApiConfiguration, useValue: { rootUrl: '' } },
      ],
    });
    service = TestBed.inject(IdentityApiService);
    httpTesting = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpTesting.verify());

  it('getStatus sends GET /api/status and unwraps body', () => {
    const mockBody = { displayName: 'SSO' };
    service.getStatus().subscribe(result => {
      expect(result).toEqual(mockBody);
    });
    const req = httpTesting.expectOne('/api/status');
    expect(req.request.method).toBe('GET');
    req.flush(mockBody);
  });

  it('getUserInfo sends GET /api/auth/user-info and unwraps body', () => {
    const mockBody = {
      email: 'user@test.com',
      displayName: 'Test User',
      isActive: true,
      role: 'Contributor',
    };
    service.getUserInfo().subscribe(result => {
      expect(result).toEqual(mockBody);
    });
    const req = httpTesting.expectOne('/api/auth/user-info');
    expect(req.request.method).toBe('GET');
    req.flush(mockBody);
  });
});
