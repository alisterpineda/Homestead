import { TestBed } from '@angular/core/testing';
import { HttpClient, HttpErrorResponse, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { toast } from 'ngx-sonner';
import { errorInterceptor } from './error.interceptor';
import { ErrorHandlerService } from '@/shared/services/error-handler.service';

describe('errorInterceptor', () => {
  let http: HttpClient;
  let httpTesting: HttpTestingController;
  let errorHandler: ErrorHandlerService;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([errorInterceptor])),
        provideHttpClientTesting(),
      ],
    });
    http = TestBed.inject(HttpClient);
    httpTesting = TestBed.inject(HttpTestingController);
    errorHandler = TestBed.inject(ErrorHandlerService);
    vi.spyOn(toast, 'error').mockImplementation(() => undefined as never);
    vi.spyOn(errorHandler, 'handleError');
  });

  afterEach(() => {
    httpTesting.verify();
  });

  it('should auto-handle 500 errors', () => {
    http.get('/api/test').subscribe({ error: () => {} });
    httpTesting.expectOne('/api/test').flush(
      { detail: 'Internal Server Error' },
      { status: 500, statusText: 'Internal Server Error' },
    );
    expect(errorHandler.handleError).toHaveBeenCalledWith(
      expect.any(HttpErrorResponse),
      'An unexpected error occurred.',
    );
  });

  it('should auto-handle network errors (status 0)', () => {
    http.get('/api/test').subscribe({ error: () => {} });
    httpTesting.expectOne('/api/test').error(
      new ProgressEvent('error'),
      { status: 0, statusText: 'Unknown Error' },
    );
    expect(errorHandler.handleError).toHaveBeenCalled();
  });

  it('should NOT auto-handle 400 errors', () => {
    http.get('/api/test').subscribe({ error: () => {} });
    httpTesting.expectOne('/api/test').flush(
      { detail: 'Bad Request' },
      { status: 400, statusText: 'Bad Request' },
    );
    expect(errorHandler.handleError).not.toHaveBeenCalled();
  });

  it('should NOT auto-handle 404 errors', () => {
    http.get('/api/test').subscribe({ error: () => {} });
    httpTesting.expectOne('/api/test').flush(
      { detail: 'Not Found' },
      { status: 404, statusText: 'Not Found' },
    );
    expect(errorHandler.handleError).not.toHaveBeenCalled();
  });

  it('should still propagate error to subscriber', () => {
    const errorFn = vi.fn();
    http.get('/api/test').subscribe({ error: errorFn });
    httpTesting.expectOne('/api/test').flush(
      { detail: 'Server Error' },
      { status: 500, statusText: 'Internal Server Error' },
    );
    expect(errorFn).toHaveBeenCalledWith(expect.any(HttpErrorResponse));
  });
});
