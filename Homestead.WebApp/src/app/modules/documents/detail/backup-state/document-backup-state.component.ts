import { ChangeDetectionStrategy, Component, inject, input } from '@angular/core';
import { DatePipe } from '@angular/common';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideExternalLink, lucideTriangleAlert } from '@ng-icons/lucide';

import { HlmBadge } from '@/shared/components/spartan/badge';
import { formatBytes } from '@/shared/utils';
import { formatRelativeAge } from '@/shared/utils/relative-age';
import {
  backupKindLabel,
  backupLastSuccessLabel,
  backupStatusLabel,
  backupStatusVariant,
  isBackupStale,
} from '@/shared/utils/backup-labels';
import type { DocumentBackupSourceState } from '@/api/generated/models/document-backup-source-state';
import type { DocumentBackupVersionState } from '@/api/generated/models/document-backup-version-state';

/**
 * Where this document stands in every mirror configured to carry it.
 *
 * <p>
 * Ordered liveness-first on purpose. The mirror verifies nothing — it is written one way and never read
 * back — so a copy record is evidence of what <i>we wrote</i>, never that the file is still there. A
 * green word above a stale or unreachable pairing would claim exactly the thing the system cannot
 * support, which is why availability leads, the last successful run comes second, and the copy record is
 * subordinate to both.
 * </p>
 *
 * <p>
 * Purely presentational: read-only, no refresh of its own, and no "back up now" — per-document selection
 * is deliberately out of scope for the mirror.
 * </p>
 */
@Component({
  selector: 'app-document-backup-state',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DatePipe, NgIcon, HlmBadge],
  providers: [DatePipe, provideIcons({ lucideExternalLink, lucideTriangleAlert })],
  template: `
    <div class="flex flex-col gap-3" data-testid="document-backup-state">
      @for (source of sources(); track source.backupSourceId) {
        <div
          class="flex flex-col gap-2 rounded-md border p-3"
          [attr.data-testid]="'backup-source-' + source.backupSourceId"
        >
          <!-- Availability first: disconnecting a target keeps its state rows, so without this the
               copy record below would read as reassuring against a destination nobody can reach. -->
          <div class="flex flex-wrap items-center gap-2">
            <span class="text-sm font-medium">{{ kindLabel(source.targetKind) }}</span>
            <span class="font-mono text-xs text-muted-foreground">{{ source.targetKey }}</span>

            @if (!source.isAvailable) {
              <span
                hlmBadge
                variant="outline"
                class="!text-muted-foreground"
                [attr.data-testid]="'availability-badge-' + source.backupSourceId"
              >
                Unavailable
              </span>
            } @else if (!source.targetEnabled || !source.isEnabled) {
              <span
                hlmBadge
                variant="outline"
                class="!text-muted-foreground"
                [attr.data-testid]="'availability-badge-' + source.backupSourceId"
              >
                Paused
              </span>
            }

            @if (source.lastRun; as run) {
              <span
                hlmBadge
                [variant]="statusVariant(run.status)"
                [attr.data-testid]="'last-run-badge-' + source.backupSourceId"
              >
                {{ statusLabel(run) }}
              </span>
            }
          </div>

          @if (source.unavailableReason; as reason) {
            <p
              class="text-sm text-destructive"
              [attr.data-testid]="'unavailable-reason-' + source.backupSourceId"
            >
              {{ reason }}
            </p>
          }

          <!-- The catch-all for every way a backup dies silently. A skipped or dry run does not refresh
               it, so a paused backup cannot keep this line looking fresh. -->
          <p
            class="text-xs"
            [class.text-muted-foreground]="!isStale(source)"
            [class.text-destructive]="isStale(source)"
            [attr.data-testid]="'last-success-' + source.backupSourceId"
          >
            Last successful run: {{ lastSuccessLabel(source) }}
          </p>

          <!-- This version's own record. -->
          @if (versionFor(source); as version) {
            @if (version.lastWrittenAt) {
              <div
                class="flex flex-col gap-1 text-sm"
                [attr.data-testid]="'version-written-' + source.backupSourceId"
              >
                <p>
                  Version {{ version.versionNumber }} last written
                  <span class="font-medium">{{ relativeAge(version.lastWrittenAt) }}</span>
                  @if (version.fileSize != null) {
                    · {{ bytes(version.fileSize) }}
                  }
                </p>

                @if (version.remoteRelativePath; as path) {
                  <p class="font-mono text-xs break-all text-muted-foreground">{{ path }}</p>
                }

                <!-- Emitted by the server only while the pairing is usable, so a link here always
                     points at something the reader can actually open. -->
                @if (version.remoteUrl; as url) {
                  <a
                    [href]="url"
                    target="_blank"
                    rel="noopener noreferrer"
                    class="inline-flex w-fit items-center gap-1 text-xs underline underline-offset-2 hover:no-underline"
                    [attr.data-testid]="'remote-link-' + source.backupSourceId"
                  >
                    Open in {{ kindLabel(source.targetKind) }}{{ accountSuffix(source) }}
                    <ng-icon name="lucideExternalLink" class="text-[length:--spacing(3)] shrink-0" />
                  </a>
                }
              </div>
            }

            <!-- Rendered ALONGSIDE the written record, never instead of it. A version copied by an
                 earlier run and then failed by the newest one (a rename whose move failed is the
                 common shape) carries both, and hiding the failure behind the older, cheerful
                 timestamp is precisely the "absence of failure reads as success" trap. -->
            @if (version.lastFailureMessage; as message) {
              <div
                class="flex items-start gap-2 rounded-md border border-destructive/50 p-3 text-sm text-destructive"
                [attr.data-testid]="'version-failure-' + source.backupSourceId"
              >
                <ng-icon name="lucideTriangleAlert" class="text-[length:--spacing(4)] mt-0.5 shrink-0" />
                <span class="break-words">
                  @if (version.lastWrittenAt) {
                    The last run failed to update the copy of version
                    {{ version.versionNumber }}, so the record above is from an earlier run:
                    {{ message }}
                  } @else {
                    The copy of version {{ version.versionNumber }} failed: {{ message }}
                  }
                  @if (version.lastFailureAt) {
                    <span class="block text-xs">
                      Recorded {{ version.lastFailureAt | date: 'medium' }}.
                    </span>
                  }
                </span>
              </div>
            } @else if (!version.lastWrittenAt) {
              <!-- Never a bare "not backed up": the reason is what tells a reader whether to wait,
                   fix something, or start a run. -->
              <p
                class="text-sm text-muted-foreground"
                [attr.data-testid]="'version-not-written-' + source.backupSourceId"
              >
                Version {{ version.versionNumber }} is not in this mirror — {{ notWrittenReason(source) }}
              </p>
            }
          }

          <!-- The document-level roll-up, under this pairing's own version line. -->
          <p
            class="text-xs text-muted-foreground"
            [attr.data-testid]="'version-rollup-' + source.backupSourceId"
          >
            {{ source.copiedVersionCount }} of {{ (source.versions ?? []).length }} version(s) recorded here.
            @if (source.pendingRemovalCount > 0) {
              {{ source.pendingRemovalCount }} file(s) from deleted versions are still in the mirror
              until a run removes them.
            }
          </p>
        </div>
      }

      <!-- Said once, plainly, because every value above is a record of what was written rather than
           evidence of what is still there. -->
      <p class="text-xs text-muted-foreground">
        “Last written” is when the mirror last wrote the file — a rename updates it without re-copying.
        The mirror is never read back, so nothing here is proof the copy is still in place.
      </p>
    </div>
  `,
})
export class DocumentBackupStateComponent {
  private datePipe = inject(DatePipe);

  sources = input.required<DocumentBackupSourceState[]>();

  /** The version the page is currently showing. Falls back to the newest when unset. */
  selectedVersionId = input<string | null>(null);

  kindLabel(kind: DocumentBackupSourceState['targetKind']): string {
    return backupKindLabel(kind);
  }

  statusVariant(status: NonNullable<DocumentBackupSourceState['lastRun']>['status']) {
    return backupStatusVariant(status);
  }

  statusLabel(run: NonNullable<DocumentBackupSourceState['lastRun']>): string {
    return backupStatusLabel(run);
  }

  lastSuccessLabel(source: DocumentBackupSourceState): string {
    return backupLastSuccessLabel(source);
  }

  isStale(source: DocumentBackupSourceState): boolean {
    return isBackupStale(source);
  }

  relativeAge(timestamp: string): string {
    return formatRelativeAge(timestamp);
  }

  bytes(size: number): string {
    return size <= 0 ? '0 B' : formatBytes(size);
  }

  /** Names the account so "which Drive is this in?" is answerable without opening the link. */
  accountSuffix(source: DocumentBackupSourceState): string {
    return source.targetAccountEmail ? ` (${source.targetAccountEmail})` : '';
  }

  /**
   * The version this panel reports on. The page's own selection wins; with none, the newest — the
   * server returns versions newest-first.
   */
  versionFor(source: DocumentBackupSourceState): DocumentBackupVersionState | undefined {
    const versions = source.versions ?? [];
    const selected = this.selectedVersionId();

    return (
      (selected ? versions.find(v => v.documentVersionId === selected) : undefined) ?? versions[0]
    );
  }

  /**
   * Why nothing is recorded, resolved to something actionable.
   *
   * <p>
   * Only ever asked when this version has NO recorded failure of its own — the template renders that
   * case itself, with the message. So the ordering that matters here is the next one down: a failed run
   * that recorded nothing must say so rather than fall through to a schedule sentence that would read as
   * "all fine, just waiting". Absence of a recorded failure is never success.
   * </p>
   */
  notWrittenReason(source: DocumentBackupSourceState): string {
    const run = source.lastRun;

    if (run?.status === 'Pending' || run?.status === 'InProgress') return 'a run is in progress.';

    if (run?.status === 'Failed' || run?.status === 'PartiallyCompleted') {
      return "the last run failed, and this file's outcome was not recorded.";
    }

    if (!source.isAvailable) return 'this backup cannot run until the destination is fixed.';
    if (!source.targetEnabled) return 'the destination is switched off.';
    if (!source.isEnabled) return 'this backup is paused.';

    const schedule = source.schedule;
    if (schedule?.isEnabled) {
      return `the next run is ${this.datePipe.transform(schedule.nextRunAt, 'short')}.`;
    }
    if (schedule) return 'its schedule is disabled, so no run is due.';

    return source.lastRun
      ? 'there is no schedule, so it is written only when a run is started by hand.'
      : 'there is no schedule and this backup has never run.';
  }
}
