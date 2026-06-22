import { TestBed } from '@angular/core/testing';
import { describe, it, expect, beforeEach, vi } from 'vitest';
import { of, throwError, Subject } from 'rxjs';
import { HttpErrorResponse } from '@angular/common/http';
import { AiStatusComponent } from './ai-status.component';
import { AdminApiService } from '@/api/services/admin-api.service';
import type { AiStatusResponse } from '@/api/generated/models/ai-status-response';
import type { TestAiConnectionResponse } from '@/api/generated/models/test-ai-connection-response';

const CONFIGURED: AiStatusResponse = {
  configured: true,
  endpoint: 'http://homeserver:11434/v1',
  model: 'gemma-4-e4b',
  hasApiKey: true,
  maxInputChars: 12000,
};

const NOT_CONFIGURED: AiStatusResponse = {
  configured: false,
  endpoint: null,
  model: null,
  hasApiKey: false,
  maxInputChars: 12000,
};

describe('AiStatusComponent', () => {
  const api = {
    getAiStatus: vi.fn(),
    testAiConnection: vi.fn(),
  };

  function create() {
    const fixture = TestBed.createComponent(AiStatusComponent);
    const component = fixture.componentInstance;
    component.ngOnInit();
    return component;
  }

  beforeEach(() => {
    api.getAiStatus.mockReset();
    api.testAiConnection.mockReset();
    api.getAiStatus.mockReturnValue(of(CONFIGURED));
    api.testAiConnection.mockReturnValue(of<TestAiConnectionResponse>({
      success: true, message: 'Connection successful.', latencyMs: 42,
    }));

    TestBed.configureTestingModule({
      providers: [{ provide: AdminApiService, useValue: api }],
    });
  });

  it('should create', () => {
    const component = create();
    expect(component).toBeTruthy();
  });

  it('should render a configured status', () => {
    const component = create();

    expect(component.loading()).toBe(false);
    expect(component.error()).toBe(false);
    expect(component.status()?.configured).toBe(true);
    expect(component.status()?.endpoint).toBe('http://homeserver:11434/v1');
    expect(component.status()?.model).toBe('gemma-4-e4b');
    expect(component.status()?.configured).toBe(true);
  });

  it('should render a not-configured status', () => {
    api.getAiStatus.mockReturnValue(of(NOT_CONFIGURED));

    const component = create();

    expect(component.status()?.configured).toBe(false);
  });

  it('should surface a load error', () => {
    api.getAiStatus.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 500 })));

    const component = create();

    expect(component.error()).toBe(true);
    expect(component.loading()).toBe(false);
    expect(component.status()).toBeNull();
  });

  it('should record a successful test connection result', () => {
    const component = create();

    component.test();

    expect(api.testAiConnection).toHaveBeenCalled();
    expect(component.testResult()?.success).toBe(true);
    expect(component.testResult()?.latencyMs).toBe(42);
    expect(component.testing()).toBe(false);
  });

  it('should record a failed test result returned in the body (HTTP 200)', () => {
    api.testAiConnection.mockReturnValue(of<TestAiConnectionResponse>({
      success: false, message: 'Connection timed out.', latencyMs: null,
    }));

    const component = create();
    component.test();

    expect(component.testResult()?.success).toBe(false);
    expect(component.testResult()?.message).toBe('Connection timed out.');
    expect(component.testing()).toBe(false);
  });

  it('should toggle testing true then false across the call', () => {
    const subject = new Subject<TestAiConnectionResponse>();
    api.testAiConnection.mockReturnValue(subject.asObservable());

    const component = create();
    component.test();
    expect(component.testing()).toBe(true);

    subject.next({ success: true, message: 'Connection successful.', latencyMs: 10 });
    subject.complete();
    expect(component.testing()).toBe(false);
  });

  it('should reset testing on a 5xx test error', () => {
    api.testAiConnection.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 500 })));

    const component = create();
    component.test();

    expect(component.testing()).toBe(false);
    expect(component.testResult()).toBeNull();
  });
});
