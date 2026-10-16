import { TestBed } from '@angular/core/testing';
import { describe, it, expect, beforeEach, vi } from 'vitest';
import { of, throwError, Subject } from 'rxjs';
import { HttpErrorResponse } from '@angular/common/http';
import { AiStatusComponent } from './ai-status.component';
import { AdminApiService } from '@/api/services/admin-api.service';
import type { AiProviderStatusResponse } from '@/api/generated/models/ai-provider-status-response';
import type { TestAiConnectionResponse } from '@/api/generated/models/test-ai-connection-response';

const LOCAL: AiProviderStatusResponse = {
  key: 'local',
  type: 'OpenAiCompatible',
  isConfigured: true,
  configurationError: null,
  isDefault: true,
  endpoint: 'http://homeserver:11434/v1',
  model: 'gemma-4-e4b',
  hasApiKey: true,
  maxInputChars: 12000,
  temperature: null,
  structuredOutput: true,
};

const BROKEN_FOUNDRY: AiProviderStatusResponse = {
  key: 'foundry-gpt5',
  type: 'AzureFoundry',
  isConfigured: false,
  configurationError: 'ApiKey is required.',
  isDefault: false,
  endpoint: 'https://res.openai.azure.com',
  model: 'gpt-5-mini',
  hasApiKey: false,
  maxInputChars: 12000,
  temperature: null,
  structuredOutput: true,
};

// The value cell beside a provider-detail label (e.g. "Temperature"), so assertions pin the value to its row
// rather than matching a bare "0" or "On" anywhere on the page.
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
    api.getAiStatus.mockReturnValue(of([LOCAL, BROKEN_FOUNDRY]));
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

  it('should load the provider list', () => {
    const { component } = create();

    expect(component.loading()).toBe(false);
    expect(component.error()).toBe(false);
    expect(component.providers()?.map(p => p.key)).toEqual(['local', 'foundry-gpt5']);
  });

  it('should render every provider with default badge and unavailable reason', () => {
    const { fixture } = create();
    fixture.detectChanges();
    const html: string = fixture.nativeElement.outerHTML;

    expect(html).toContain('local');
    expect(html).toContain('foundry-gpt5');
    expect(html).toContain('Default');
    expect(html).toContain('OpenAI-compatible');
    expect(html).toContain('Azure AI Foundry');
    // The broken provider surfaces its reason on the row, not a bare "Not configured".
    expect(html).toContain('ApiKey is required.');
  });

  it('should label an Anthropic provider and show the SDK default when no endpoint is set', () => {
    api.getAiStatus.mockReturnValue(of<AiProviderStatusResponse[]>([{
      key: 'claude',
      type: 'Anthropic',
      isConfigured: true,
      configurationError: null,
      isDefault: true,
      endpoint: null,
      model: 'claude-opus-5-5',
      hasApiKey: true,
      maxInputChars: 12000,
      temperature: null,
      structuredOutput: true,
    }]));

    const { fixture } = create();
    fixture.detectChanges();
    const html: string = fixture.nativeElement.outerHTML;

    expect(html).toContain('Anthropic');
    expect(html).toContain('SDK default');
    expect(html).toContain('claude-opus-5-5');
  });

  it('should show the configured temperature and structured output off', () => {
    // 0 is the common deterministic setting and falsy — it must render as "0", not "model default".
    api.getAiStatus.mockReturnValue(of([{ ...LOCAL, temperature: 0, structuredOutput: false }]));

    const { fixture } = create();
    fixture.detectChanges();
    const root: HTMLElement = fixture.nativeElement;

    expect(detailValue(root, 'Temperature')).toBe('0');
    expect(detailValue(root, 'Structured output')).toBe('Off');
  });

  it('should show "model default" for an unset temperature and structured output on', () => {
    api.getAiStatus.mockReturnValue(of([LOCAL]));

    const { fixture } = create();
    fixture.detectChanges();
    const root: HTMLElement = fixture.nativeElement;

    expect(detailValue(root, 'Temperature')).toBe('model default');
    expect(detailValue(root, 'Structured output')).toBe('On');
  });

  it('should render the empty state naming the new config keys', () => {
    api.getAiStatus.mockReturnValue(of([]));

    const { fixture, component } = create();
    fixture.detectChanges();
    const html: string = fixture.nativeElement.outerHTML;

    expect(component.providers()).toEqual([]);
    expect(html).toContain('No AI providers configured');
    expect(html).toContain('Ai:Providers');
    expect(html).toContain('Ai:DefaultProvider');
  });

  it('should warn when multiple providers exist but none is default (AI silently off)', () => {
    api.getAiStatus.mockReturnValue(of([
      { ...LOCAL, isDefault: false },
      { ...BROKEN_FOUNDRY, isConfigured: true, configurationError: null },
    ]));

    const { fixture, component } = create();
    fixture.detectChanges();

    expect(component.noDefault()).toBe(true);
    expect(fixture.nativeElement.outerHTML).toContain('Ai:DefaultProvider');
  });

  it('should not warn when a default is marked or only one provider exists', () => {
    const { fixture: withDefault, component: c1 } = create();
    withDefault.detectChanges();
    expect(c1.noDefault()).toBe(false);
    expect(withDefault.nativeElement.outerHTML).not.toContain('No default provider is selected');

    api.getAiStatus.mockReturnValue(of([{ ...LOCAL, isDefault: false }]));
    const { component: c2 } = create();
    // A sole provider is auto-resolved by the backend chain even unmarked — no warning.
    expect(c2.noDefault()).toBe(false);
  });

  it('should clear a provider prior result when a re-test starts, and not restore it on error', () => {
    const { component } = create();
    component.test('local');
    expect(component.testResults()['local']?.success).toBe(true);

    // Re-test that fails at the HTTP level: the stale success must not survive.
    api.testAiConnection.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 500 })));
    component.test('local');

    expect(component.testResults()['local']).toBeUndefined();
    expect(component.testingKey()).toBeNull();
  });

  it('should clear only the re-tested provider result while a probe is in flight', () => {
    const { component } = create();
    component.test('local');
    component.test('foundry-gpt5');

    const subject = new Subject<TestAiConnectionResponse>();
    api.testAiConnection.mockReturnValue(subject.asObservable());
    component.test('local');

    // Stale 'local' result gone during the probe; the other provider's result untouched.
    expect(component.testResults()['local']).toBeUndefined();
    expect(component.testResults()['foundry-gpt5']?.success).toBe(true);
    subject.next({ success: true, message: 'Connection successful.', latencyMs: 9 });
    subject.complete();
    expect(component.testResults()['local']?.latencyMs).toBe(9);
  });

  it('should surface a load error', () => {
    api.getAiStatus.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 500 })));

    const { component } = create();

    expect(component.error()).toBe(true);
    expect(component.loading()).toBe(false);
    expect(component.providers()).toBeNull();
  });

  it('should test a specific provider and record its result under its key', () => {
    const { component } = create();

    component.test('local');

    expect(api.testAiConnection).toHaveBeenCalledWith('local');
    expect(component.testResults()['local']?.success).toBe(true);
    expect(component.testResults()['local']?.latencyMs).toBe(42);
    expect(component.testResults()['foundry-gpt5']).toBeUndefined();
    expect(component.testingKey()).toBeNull();
  });

  it('should keep results per provider across successive tests', () => {
    const { component } = create();

    component.test('local');
    api.testAiConnection.mockReturnValue(of<TestAiConnectionResponse>({
      success: false, message: "AI provider 'foundry-gpt5' is unavailable: ApiKey is required.", latencyMs: null,
    }));
    component.test('foundry-gpt5');

    expect(component.testResults()['local']?.success).toBe(true);
    expect(component.testResults()['foundry-gpt5']?.success).toBe(false);
  });

  it('should record a failed test result returned in the body (HTTP 200)', () => {
    api.testAiConnection.mockReturnValue(of<TestAiConnectionResponse>({
      success: false, message: 'Connection timed out.', latencyMs: null,
    }));

    const { component } = create();
    component.test('local');

    expect(component.testResults()['local']?.success).toBe(false);
    expect(component.testResults()['local']?.message).toBe('Connection timed out.');
    expect(component.testingKey()).toBeNull();
  });

  it('should track the in-flight provider key across the call', () => {
    const subject = new Subject<TestAiConnectionResponse>();
    api.testAiConnection.mockReturnValue(subject.asObservable());

    const { component } = create();
    component.test('local');
    expect(component.testingKey()).toBe('local');

    subject.next({ success: true, message: 'Connection successful.', latencyMs: 10 });
    subject.complete();
    expect(component.testingKey()).toBeNull();
  });

  it('should reset the in-flight key on a 5xx test error', () => {
    api.testAiConnection.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 500 })));

    const { component } = create();
    component.test('local');

    expect(component.testingKey()).toBeNull();
    expect(component.testResults()['local']).toBeUndefined();
  });
});
