import { TestBed } from '@angular/core/testing';
import { SwUpdate, UnrecoverableStateEvent, VersionEvent } from '@angular/service-worker';
import { Subject } from 'rxjs';
import { toast } from 'ngx-sonner';
import { AppUpdateService } from './app-update.service';

describe('AppUpdateService', () => {
  let versionUpdates$: Subject<VersionEvent>;
  let unrecoverable$: Subject<UnrecoverableStateEvent>;

  function createService(isEnabled: boolean): AppUpdateService {
    versionUpdates$ = new Subject<VersionEvent>();
    unrecoverable$ = new Subject<UnrecoverableStateEvent>();

    TestBed.configureTestingModule({
      providers: [
        {
          provide: SwUpdate,
          useValue: {
            isEnabled,
            versionUpdates: versionUpdates$.asObservable(),
            unrecoverable: unrecoverable$.asObservable(),
            activateUpdate: vi.fn().mockResolvedValue(true),
            checkForUpdate: vi.fn().mockResolvedValue(true),
          },
        },
      ],
    });

    return TestBed.inject(AppUpdateService);
  }

  beforeEach(() => {
    vi.spyOn(toast, 'info').mockImplementation(() => undefined as never);
    vi.spyOn(toast, 'error').mockImplementation(() => undefined as never);
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('should show info toast when a new version is ready', () => {
    createService(true);

    versionUpdates$.next({
      type: 'VERSION_READY',
      currentVersion: { hash: 'abc' },
      latestVersion: { hash: 'def' },
    });

    expect(toast.info).toHaveBeenCalledWith('A new version is available.', expect.objectContaining({
      description: 'Reload to get the latest features and fixes.',
      duration: Infinity,
    }));
  });

  it('should show error toast on unrecoverable state', () => {
    createService(true);

    unrecoverable$.next({
      type: 'UNRECOVERABLE_STATE',
      reason: 'Cache corrupted',
    });

    expect(toast.error).toHaveBeenCalledWith('The app encountered an error and needs to reload.', expect.objectContaining({
      description: 'Cache corrupted',
      duration: Infinity,
    }));
  });

  it('should not subscribe when service worker is disabled', () => {
    createService(false);

    versionUpdates$.next({
      type: 'VERSION_READY',
      currentVersion: { hash: 'abc' },
      latestVersion: { hash: 'def' },
    });

    expect(toast.info).not.toHaveBeenCalled();
  });

  it('should ignore non-VERSION_READY events', () => {
    createService(true);

    versionUpdates$.next({
      type: 'VERSION_DETECTED',
      version: { hash: 'abc' },
    });

    expect(toast.info).not.toHaveBeenCalled();
  });
});
