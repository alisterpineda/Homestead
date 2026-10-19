import { TestBed } from '@angular/core/testing';
import { describe, it, expect, beforeEach, vi } from 'vitest';
import { of, throwError, Subject } from 'rxjs';
import { HttpErrorResponse } from '@angular/common/http';
import { AiStatusComponent } from './ai-status.component';
import { AdminApiService } from '@/api/services/admin-api.service';
import type { AiStatusResponse } from '@/api/generated/models/ai-status-response';
import type { TestAiConnectionResponse } from '@/api/generated/models/test-ai-connection-response';

const READY: AiStatusResponse = {
  isConfigured: true,
  isPresent: true,
  configurationError: null,
  model: 'claude-opus-5-5',
  endpoint: null,
  hasApiKey: true,
  effort: 'medium',
  refusalFallback: true,
  maxInputChars: 12000,
  statementMaxInputChars: 120000,
  imageInput: true,
  imageRenderMaxEdge: 1024,
  pdfInput: true,
  pdfUpload: true,
  pdfInputMaxPages: 20,
  pdfInputMaxBytes: 20971520,
};

const ABSENT: AiStatusResponse = {
  ...READY,
  isConfigured: false,
  isPresent: false,
  model: null,
  hasApiKey: false,
};

const LEGACY_REASON =
  'Legacy AI config found (Ai:Providers / Ai:DefaultProvider are no longer read) — set Ai:ApiKey and Ai:Model.';

const LEGACY: AiStatusResponse = {
  ...ABSENT,
  isPresent: true,
  configurationError: LEGACY_REASON,
};

// The value cell beside a detail label (e.g. "Effort"), so assertions pin the value to its row rather than
// matching a bare "On" anywhere on the page.
function detailValue(root: HTMLElement, label: string): string | undefined {
  const labelEl = Array.from(root.querySelectorAll('span')).find(el => el.textContent?.trim() === label);
  return labelEl?.nextElementSibling?.textContent?.trim();
}

describe('AiStatusComponent', () => {
  const api = {
    getAiStatus: vi.fn(),
    testAiConnection: vi.fn(),
  };

  function create() {
    const fixture = TestBed.createComponent(AiStatusComponent);
    const component = fixture.componentInstance;
    component.ngOnInit();
    return { fixture, component };
  }

  beforeEach(() => {
    api.getAiStatus.mockReset();
    api.testAiConnection.mockReset();
    api.getAiStatus.mockReturnValue(of(READY));
    api.testAiConnection.mockReturnValue(of<TestAiConnectionResponse>({
      success: true, message: 'Connection successful.', latencyMs: 42,
    }));

    TestBed.configureTestingModule({
      providers: [{ provide: AdminApiService, useValue: api }],
    });
  });

  it('should create', () => {
    const { component } = create();
    expect(component).toBeTruthy();
  });

  it('should load the status', () => {
    const { component } = create();

    expect(component.loading()).toBe(false);
    expect(component.error()).toBe(false);
    expect(component.status()?.model).toBe('claude-opus-5-5');
  });

  it('should render the configured card with every setting', () => {
    const { fixture } = create();
    fixture.detectChanges();
    const root: HTMLElement = fixture.nativeElement;

    expect(root.outerHTML).toContain('Configured');
    expect(detailValue(root, 'Model')).toBe('claude-opus-5-5');
    expect(detailValue(root, 'Endpoint')).toBe('Claude API default');
    expect(detailValue(root, 'API key')).toBe('Set');
    expect(detailValue(root, 'Effort')).toBe('medium');
    expect(detailValue(root, 'Refusal fallback')).toBe('On');
    expect(detailValue(root, 'Page image')).toBe('On (1024 px)');
    expect(detailValue(root, 'PDF input')).toBe('On (up to 20 pages, 20 MB, uploaded)');
  });

  it('should show an endpoint override and the knobs turned off', () => {
    api.getAiStatus.mockReturnValue(of<AiStatusResponse>({
      ...READY, endpoint: 'https://proxy.example.com', refusalFallback: false, imageInput: false, effort: 'low',
      pdfInput: false,
    }));

    const { fixture } = create();
    fixture.detectChanges();
    const root: HTMLElement = fixture.nativeElement;

    expect(detailValue(root, 'Endpoint')).toBe('https://proxy.example.com');
    expect(detailValue(root, 'Refusal fallback')).toBe('Off');
    expect(detailValue(root, 'Page image')).toBe('Off');
    expect(detailValue(root, 'Effort')).toBe('low');
    expect(detailValue(root, 'PDF input')).toBe('Off');
  });

  it('should show a fractional PDF size cap', () => {
    api.getAiStatus.mockReturnValue(of<AiStatusResponse>({ ...READY, pdfInputMaxPages: 5, pdfInputMaxBytes: 1572864 }));

    const { fixture } = create();
    fixture.detectChanges();

    expect(detailValue(fixture.nativeElement, 'PDF input')).toBe('On (up to 5 pages, 1.5 MB, uploaded)');
  });

  it('should say when PDFs are sent inline rather than uploaded', () => {
    api.getAiStatus.mockReturnValue(of<AiStatusResponse>({ ...READY, pdfUpload: false, pdfInputMaxBytes: 4194304 }));

    const { fixture } = create();
    fixture.detectChanges();

    expect(detailValue(fixture.nativeElement, 'PDF input')).toBe('On (up to 20 pages, 4 MB, inline)');
  });

  it('should render the empty state naming the config keys when AI is absent', () => {
    api.getAiStatus.mockReturnValue(of(ABSENT));

    const { fixture } = create();
    fixture.detectChanges();
    const html: string = fixture.nativeElement.outerHTML;

    expect(html).toContain('AI is not configured');
    expect(html).toContain('Ai:ApiKey');
    expect(html).toContain('Ai:Model');
    expect(html).not.toContain('Test connection');
  });

  it('should show the legacy-config reason and disable the test', () => {
    api.getAiStatus.mockReturnValue(of(LEGACY));

    const { fixture } = create();
    fixture.detectChanges();
    const root: HTMLElement = fixture.nativeElement;

    expect(root.outerHTML).toContain('Unavailable');
    expect(root.outerHTML).toContain(LEGACY_REASON);
    const button = Array.from(root.querySelectorAll('button')).find(b => b.textContent?.includes('Test connection'));
    expect(button?.disabled).toBe(true);
  });

  it('should surface a load error', () => {
    api.getAiStatus.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 500 })));

    const { component } = create();

    expect(component.error()).toBe(true);
    expect(component.loading()).toBe(false);
    expect(component.status()).toBeNull();
  });

  it('should test the connection and record the result', () => {
    const { component } = create();

    component.test();

    expect(api.testAiConnection).toHaveBeenCalledWith();
    expect(component.testResult()?.success).toBe(true);
    expect(component.testResult()?.latencyMs).toBe(42);
    expect(component.testing()).toBe(false);
  });

  it('should record a failed test result returned in the body (HTTP 200)', () => {
    api.testAiConnection.mockReturnValue(of<TestAiConnectionResponse>({
      success: false, message: 'AI is unavailable: Ai:ApiKey is required.', latencyMs: null,
    }));

    const { component } = create();
    component.test();

    expect(component.testResult()?.success).toBe(false);
    expect(component.testResult()?.message).toBe('AI is unavailable: Ai:ApiKey is required.');
    expect(component.testing()).toBe(false);
  });

  it('should clear the prior result when a re-test starts, and not restore it on error', () => {
    const { component } = create();
    component.test();
    expect(component.testResult()?.success).toBe(true);

    // Re-test that fails at the HTTP level: the stale success must not survive.
    api.testAiConnection.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 500 })));
    component.test();

    expect(component.testResult()).toBeNull();
    expect(component.testing()).toBe(false);
  });

  it('should track the in-flight test across the call', () => {
    const subject = new Subject<TestAiConnectionResponse>();
    api.testAiConnection.mockReturnValue(subject.asObservable());

    const { component } = create();
    component.test();
    expect(component.testing()).toBe(true);
    expect(component.testResult()).toBeNull();

    subject.next({ success: true, message: 'Connection successful.', latencyMs: 10 });
    subject.complete();
    expect(component.testing()).toBe(false);
    expect(component.testResult()?.latencyMs).toBe(10);
  });
});
