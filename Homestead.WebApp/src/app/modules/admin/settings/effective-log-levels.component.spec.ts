import { TestBed } from '@angular/core/testing';
import { describe, it, expect, beforeEach, vi } from 'vitest';
import { of, throwError } from 'rxjs';
import { HttpErrorResponse } from '@angular/common/http';
import { EffectiveLogLevelsComponent } from './effective-log-levels.component';
import { AdminApiService } from '@/api/services/admin-api.service';
import type { LogLevelsResponse } from '@/api/generated/models/log-levels-response';

describe('EffectiveLogLevelsComponent', () => {
  const api = {
    getLogLevels: vi.fn(),
  };

  const table: LogLevelsResponse = {
    minLevel: 'Information',
    rules: [
      { category: 'Default', level: 'Information', source: 'AppSettings' },
      { category: 'Microsoft.AspNetCore', level: 'Warning', source: 'AppSettings' },
    ],
  };

  function create() {
    const fixture = TestBed.createComponent(EffectiveLogLevelsComponent);
    const component = fixture.componentInstance;
    component.ngOnInit();
    return component;
  }

  beforeEach(() => {
    api.getLogLevels.mockReset();
    api.getLogLevels.mockReturnValue(of(table));

    TestBed.configureTestingModule({
      providers: [{ provide: AdminApiService, useValue: api }],
    });
  });

  it('should create', () => {
    expect(create()).toBeTruthy();
  });

  it('should load the rule table on init', () => {
    const component = create();
    expect(api.getLogLevels).toHaveBeenCalledWith();
    expect(component.loading()).toBe(false);
    expect(component.rules().map(r => r.category)).toEqual(['Default', 'Microsoft.AspNetCore']);
    expect(component.minLevel()).toBe('Information');
  });

  it('should surface a load error', () => {
    api.getLogLevels.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 500 })));
    const component = create();
    expect(component.error()).toBe(true);
    expect(component.loading()).toBe(false);
  });

  it('should resolve the typed category and render the resolved block', () => {
    const component = create();

    const resolvedResponse: LogLevelsResponse = {
      ...table,
      resolved: {
        category: 'Homestead.WebApi.Modules.Documents.Foo',
        effectiveLevel: 'Debug',
        matchedCategory: 'Homestead',
        source: 'Override',
      },
    };
    api.getLogLevels.mockReturnValue(of(resolvedResponse));

    component.query.set('Homestead.WebApi.Modules.Documents.Foo');
    component.resolve();

    expect(api.getLogLevels).toHaveBeenLastCalledWith('Homestead.WebApi.Modules.Documents.Foo');
    expect(component.resolved()?.effectiveLevel).toBe('Debug');
    expect(component.resolved()?.matchedCategory).toBe('Homestead');
    expect(component.resolving()).toBe(false);
  });

  it('should ignore a blank resolve request', () => {
    const component = create();
    api.getLogLevels.mockClear();

    component.query.set('   ');
    component.resolve();

    expect(api.getLogLevels).not.toHaveBeenCalled();
  });

  it('should re-resolve the last category on refresh', () => {
    const component = create();

    const resolvedResponse: LogLevelsResponse = {
      ...table,
      resolved: {
        category: 'Homestead',
        effectiveLevel: 'Debug',
        matchedCategory: 'Homestead',
        source: 'Override',
      },
    };
    api.getLogLevels.mockReturnValue(of(resolvedResponse));
    component.query.set('Homestead');
    component.resolve();

    api.getLogLevels.mockClear();
    component.refresh();

    expect(api.getLogLevels).toHaveBeenCalledWith('Homestead');
  });

  it('should reload the table on refresh when nothing is resolved', () => {
    const component = create();
    api.getLogLevels.mockClear();

    component.refresh();

    expect(api.getLogLevels).toHaveBeenCalledWith();
  });

  it('should not clobber an in-progress resolve input when refreshing', () => {
    const resolved: LogLevelsResponse = {
      ...table,
      resolved: { category: 'Homestead', effectiveLevel: 'Debug', matchedCategory: 'Homestead', source: 'Override' },
    };
    api.getLogLevels.mockReturnValue(of(resolved));
    const component = create();
    component.query.set('Homestead');
    component.resolve();

    // User starts typing a different namespace but hasn't pressed Check.
    component.query.set('Microsoft.AspNetCore');
    api.getLogLevels.mockClear();

    component.refresh();

    // Refresh re-probes the last-resolved category, leaving the draft input intact.
    expect(api.getLogLevels).toHaveBeenCalledWith('Homestead');
    expect(component.query()).toBe('Microsoft.AspNetCore');
  });

  it('should clear a stale resolve error on a plain table reload', () => {
    const component = create();
    api.getLogLevels.mockReturnValueOnce(throwError(() => new HttpErrorResponse({ status: 500 })));
    component.query.set('Bad.Namespace');
    component.resolve();
    expect(component.resolveError()).toBe(true);

    api.getLogLevels.mockReturnValue(of(table));
    component.load();

    expect(component.resolveError()).toBe(false);
  });

  it('should map sources to badge variants', () => {
    const component = create();
    expect(component.badgeVariant('Override')).toBe('default');
    expect(component.badgeVariant('AppSettings')).toBe('secondary');
    expect(component.badgeVariant('Environment')).toBe('secondary');
    expect(component.badgeVariant('Code')).toBe('outline');
    expect(component.badgeVariant('Default')).toBe('outline');
    expect(component.badgeClass('Default')).toContain('text-muted-foreground');
    expect(component.badgeClass('Override')).toBe('');
  });

  describe('refreshAfterSave (convergence poll)', () => {
    const withHomesteadOverride: LogLevelsResponse = {
      minLevel: 'Information',
      rules: [
        { category: 'Default', level: 'Information', source: 'AppSettings' },
        { category: 'Homestead', level: 'Debug', source: 'Override' },
      ],
    };

    it('polls until the table Override rows match the saved map, then stops', () => {
      const component = create();
      api.getLogLevels.mockClear();
      vi.useFakeTimers();
      try {
        // First poll: override not yet applied (no Override row); from the second on: applied.
        api.getLogLevels.mockReturnValueOnce(of(table)).mockReturnValue(of(withHomesteadOverride));

        component.refreshAfterSave({ Homestead: 'Debug' });
        vi.advanceTimersByTime(3000);

        expect(component.rules().some(r => r.category === 'Homestead' && r.source === 'Override')).toBe(true);
        // Exactly two polls: the first didn't converge, the second did — then it stopped.
        expect(api.getLogLevels).toHaveBeenCalledTimes(2);
      } finally {
        vi.useRealTimers();
      }
    });

    it('converges on an emptied override set once no Override rows remain', () => {
      api.getLogLevels.mockReturnValue(of(withHomesteadOverride));
      const component = create();
      api.getLogLevels.mockClear();
      vi.useFakeTimers();
      try {
        // First poll still shows the stale override; then it clears to the appsettings-only table.
        api.getLogLevels.mockReturnValueOnce(of(withHomesteadOverride)).mockReturnValue(of(table));

        component.refreshAfterSave({});
        vi.advanceTimersByTime(3000);

        expect(component.rules().some(r => r.source === 'Override')).toBe(false);
        expect(api.getLogLevels).toHaveBeenCalledTimes(2);
      } finally {
        vi.useRealTimers();
      }
    });

    it('stops after the bounded schedule when the save never converges', () => {
      const component = create();
      api.getLogLevels.mockClear();
      vi.useFakeTimers();
      try {
        api.getLogLevels.mockReturnValue(of(table)); // Override never appears

        component.refreshAfterSave({ Homestead: 'Debug' });
        vi.advanceTimersByTime(10000);

        // CONVERGENCE_DELAYS_MS has 4 entries → at most 4 attempts, then it gives up.
        expect(api.getLogLevels).toHaveBeenCalledTimes(4);
        expect(component.rules().some(r => r.source === 'Override')).toBe(false);
      } finally {
        vi.useRealTimers();
      }
    });

    it('cancels an in-flight save poll on destroy', () => {
      const component = create();
      api.getLogLevels.mockClear();
      vi.useFakeTimers();
      try {
        api.getLogLevels.mockReturnValue(of(table));

        component.refreshAfterSave({ Homestead: 'Debug' });
        component.ngOnDestroy();
        vi.advanceTimersByTime(10000);

        expect(api.getLogLevels).not.toHaveBeenCalled();
      } finally {
        vi.useRealTimers();
      }
    });
  });
});
