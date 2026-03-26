import { Injectable, inject } from '@angular/core';
import { SwUpdate } from '@angular/service-worker';
import { filter, timer } from 'rxjs';
import { toast } from 'ngx-sonner';

/** Interval between automatic update checks (6 hours). */
const CHECK_INTERVAL_MS = 6 * 60 * 60 * 1000;

@Injectable({ providedIn: 'root' })
export class AppUpdateService {
  private swUpdate = inject(SwUpdate);

  constructor() {
    if (!this.swUpdate.isEnabled) return;

    this.swUpdate.versionUpdates
      .pipe(filter((event) => event.type === 'VERSION_READY'))
      .subscribe(() => {
        toast.info('A new version is available.', {
          description: 'Reload to get the latest features and fixes.',
          duration: Infinity,
          action: {
            label: 'Reload',
            onClick: () => {
              this.swUpdate.activateUpdate().then(
                () => document.location.reload(),
                () => document.location.reload(),
              );
            },
          },
        });
      });

    this.swUpdate.unrecoverable.subscribe((event) => {
      toast.error('The app encountered an error and needs to reload.', {
        description: event.reason,
        duration: Infinity,
        action: {
          label: 'Reload',
          onClick: () => document.location.reload(),
        },
      });
    });

    timer(0, CHECK_INTERVAL_MS).subscribe(() => {
      this.swUpdate.checkForUpdate();
    });
  }
}
