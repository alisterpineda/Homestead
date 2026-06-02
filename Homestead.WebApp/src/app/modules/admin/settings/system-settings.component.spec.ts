import { TestBed } from '@angular/core/testing';
import { describe, it, expect, beforeEach, vi } from 'vitest';
import { of, throwError } from 'rxjs';
import { HttpErrorResponse } from '@angular/common/http';
import { SystemSettingsComponent } from './system-settings.component';
import { AdminApiService } from '@/api/services/admin-api.service';
import { ErrorHandlerService } from '@/shared/services/error-handler.service';
import { toast } from '@spartan-ng/brain/sonner';
import type { LogLevel } from '@/api/generated/models/log-level';

describe('SystemSettingsComponent', () => {
  const api = {
    getSystemSettings: vi.fn(),
    patchSystemSettings: vi.fn(),
  };

  function create() {
    const fixture = TestBed.createComponent(SystemSettingsComponent);
    const component = fixture.componentInstance;
    component.ngOnInit();
    return component;
  }

  beforeEach(() => {
    api.getSystemSettings.mockReset();
    api.patchSystemSettings.mockReset();
    api.getSystemSettings.mockReturnValue(of({ logLevelOverrides: null }));
    api.patchSystemSettings.mockImplementation((p: { body: { logLevelOverrides: Record<string, LogLevel> } }) =>
      of({ logLevelOverrides: p.body.logLevelOverrides }));

    TestBed.configureTestingModule({
      providers: [{ provide: AdminApiService, useValue: api }],
    });
  });

  it('should create', () => {
    const component = create();
    expect(component).toBeTruthy();
  });

  it('should render no rows when no overrides are stored', () => {
    const component = create();
    expect(component.rows().length).toBe(0);
    expect(component.loading()).toBe(false);
  });

  it('should seed stored overrides with Default first', () => {
    api.getSystemSettings.mockReturnValue(of({
      logLevelOverrides: { Homestead: 'Debug', Default: 'Warning' } as Record<string, LogLevel>,
    }));

    const component = create();
    const rows = component.rows();

    expect(rows[0].category).toBe('Default');
    expect(rows[0].level).toBe('Warning');
    const homestead = rows.find(r => r.category === 'Homestead');
    expect(homestead?.level).toBe('Debug');
  });

  it('should surface a load error', () => {
    api.getSystemSettings.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 500 })));

    const component = create();

    expect(component.error()).toBe(true);
    expect(component.loading()).toBe(false);
  });

  it('should update a row level via setLevel', () => {
    api.getSystemSettings.mockReturnValue(of({
      logLevelOverrides: { Default: 'Information' } as Record<string, LogLevel>,
    }));
    const component = create();
    component.setLevel('Default', 'Error');
    expect(component.rows()[0].level).toBe('Error');
  });

  it('should add a category row and ignore case-insensitive duplicates', () => {
    const component = create();
    component.addCategory('Homestead');
    expect(component.rows().some(r => r.category === 'Homestead')).toBe(true);

    component.addCategory('homestead');
    expect(component.rows().filter(r => r.category.toLowerCase() === 'homestead').length).toBe(1);
  });

  it('should remove any row, including Default', () => {
    api.getSystemSettings.mockReturnValue(of({
      logLevelOverrides: { Default: 'Information', Homestead: 'Debug' } as Record<string, LogLevel>,
    }));
    const component = create();

    component.removeRow('Homestead');
    expect(component.rows().some(r => r.category === 'Homestead')).toBe(false);

    component.removeRow('Default');
    expect(component.rows().some(r => r.category === 'Default')).toBe(false);
    expect(component.rows().length).toBe(0);
  });

  it('should offer Default as a quick-add when unset and add it back', () => {
    const component = create();
    expect(component.availableWellKnown()).toContain('Default');

    component.addCategory('Default');
    expect(component.rows().some(r => r.category === 'Default')).toBe(true);
    expect(component.availableWellKnown()).not.toContain('Default');
  });

  it('should exclude already-present categories from quick-add suggestions', () => {
    const component = create();
    expect(component.availableWellKnown()).toContain('Microsoft.EntityFrameworkCore');

    component.addCategory('Microsoft.EntityFrameworkCore');
    expect(component.availableWellKnown()).not.toContain('Microsoft.EntityFrameworkCore');
  });

  it('should gate canAddCustom on non-empty, non-duplicate input', () => {
    api.getSystemSettings.mockReturnValue(of({
      logLevelOverrides: { Homestead: 'Debug' } as Record<string, LogLevel>,
    }));
    const component = create();
    expect(component.canAddCustom()).toBe(false);

    component.newCategory.set('My.Custom.Namespace');
    expect(component.canAddCustom()).toBe(true);

    component.newCategory.set('homestead');
    expect(component.canAddCustom()).toBe(false);
  });

  it('should append a custom category and clear the input', () => {
    const component = create();
    component.newCategory.set('My.Custom.Namespace');
    component.addCustomCategory();

    expect(component.rows().some(r => r.category === 'My.Custom.Namespace')).toBe(true);
    expect(component.newCategory()).toBe('');
  });

  it('should save the built overrides map and toast success', () => {
    const successSpy = vi.spyOn(toast, 'success').mockImplementation(() => '' as never);
    const component = create();

    component.addCategory('Default');
    component.setLevel('Default', 'Warning');
    component.addCategory('Homestead');
    component.setLevel('Homestead', 'Debug');

    component.save();

    expect(api.patchSystemSettings).toHaveBeenCalledWith({
      body: { logLevelOverrides: { Default: 'Warning', Homestead: 'Debug' } },
    });
    expect(component.saving()).toBe(false);
    expect(successSpy).toHaveBeenCalledWith('Logging settings saved.');

    successSpy.mockRestore();
  });

  it('should persist remaining categories without Default once Default is removed', () => {
    api.getSystemSettings.mockReturnValue(of({
      logLevelOverrides: { Default: 'Information', Homestead: 'Debug' } as Record<string, LogLevel>,
    }));
    const component = create();

    component.removeRow('Default');
    component.save();

    expect(api.patchSystemSettings).toHaveBeenCalledWith({
      body: { logLevelOverrides: { Homestead: 'Debug' } },
    });
    const body = api.patchSystemSettings.mock.calls[0][0].body;
    expect(Object.keys(body.logLevelOverrides)).not.toContain('Default');
  });

  it('should save an empty overrides map when all rows are removed', () => {
    const successSpy = vi.spyOn(toast, 'success').mockImplementation(() => '' as never);
    api.getSystemSettings.mockReturnValue(of({
      logLevelOverrides: { Default: 'Information' } as Record<string, LogLevel>,
    }));
    const component = create();

    component.removeRow('Default');
    component.save();

    expect(api.patchSystemSettings).toHaveBeenCalledWith({
      body: { logLevelOverrides: {} },
    });
    expect(component.rows().length).toBe(0);

    successSpy.mockRestore();
  });

  it('should handle a 4xx save error without crashing', () => {
    const handleSpy = vi.spyOn(ErrorHandlerService.prototype, 'handleError').mockImplementation(() => {});
    api.patchSystemSettings.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 400 })));

    const component = create();
    component.save();

    expect(component.saving()).toBe(false);
    expect(handleSpy).toHaveBeenCalled();

    handleSpy.mockRestore();
  });
});
