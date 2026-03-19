import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ThemeDialogComponent } from './theme-dialog.component';
import { ThemeService } from '@/shared/services/theme.service';

describe('ThemeDialogComponent', () => {
  let fixture: ComponentFixture<ThemeDialogComponent>;
  let component: ThemeDialogComponent;
  let themeService: ThemeService;

  beforeEach(() => {
    vi.stubGlobal('localStorage', {
      getItem: vi.fn(() => null),
      setItem: vi.fn(),
      removeItem: vi.fn(),
    });

    vi.stubGlobal('matchMedia', vi.fn(() => ({
      matches: false,
      addEventListener: vi.fn(),
    })));

    TestBed.configureTestingModule({});

    fixture = TestBed.createComponent(ThemeDialogComponent);
    component = fixture.componentInstance;
    themeService = TestBed.inject(ThemeService);
    fixture.detectChanges();
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it.each(['light', 'dark', 'system'] as const)('should call ThemeService.setMode with %s', (mode) => {
    const spy = vi.spyOn(themeService, 'setMode');

    (component as any).onThemeChange(mode);

    expect(spy).toHaveBeenCalledWith(mode);
  });

  it('should not call ThemeService.setMode when value is undefined', () => {
    const spy = vi.spyOn(themeService, 'setMode');

    (component as any).onThemeChange(undefined);

    expect(spy).not.toHaveBeenCalled();
  });
});
