import { Component, ChangeDetectionStrategy, inject, signal, computed, OnDestroy } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Router, ActivatedRoute } from '@angular/router';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { switchMap, map, catchError, of, combineLatest } from 'rxjs';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideUsers2, lucideEdit, lucideTrash2, lucideShield, lucidePen, lucideEye } from '@ng-icons/lucide';
import { HlmIconImports } from '@/shared/components/spartan/icon';
import { HlmSpinner } from '@/shared/components/spartan/spinner';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmTableImports } from '@/shared/components/spartan/table';
import { HlmAlertDialogImports } from '@/shared/components/spartan/alert-dialog';
import { HlmSelectImports } from '@/shared/components/spartan/select';
import { BrnSelectImports } from '@spartan-ng/brain/select';
import { EditGroupSheetComponent } from '../../components/edit-group-sheet/edit-group-sheet.component';
import { AddMemberDialogComponent } from '../../components/add-member-dialog/add-member-dialog.component';
import { ApiConfiguration } from '@/api/generated/api-configuration';
import { getGroup } from '@/api/generated/fn/admin/get-group';
import { deleteGroup } from '@/api/generated/fn/admin/delete-group';
import { removeGroupMember } from '@/api/generated/fn/admin/remove-group-member';
import { updateGroupMember } from '@/api/generated/fn/admin/update-group-member';
import type { GroupDetailResponse } from '@/api/generated/models/group-detail-response';
import type { GroupAccessLevel } from '@/api/generated/models/group-access-level';
import type { BrnDialogState } from '@spartan-ng/brain/dialog';
import { toast } from 'ngx-sonner';

@Component({
  selector: 'app-group-detail-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    NgIcon, HlmIconImports, HlmSpinner, HlmButton, HlmTableImports,
    HlmAlertDialogImports, HlmSelectImports, BrnSelectImports,
    EditGroupSheetComponent, AddMemberDialogComponent,
  ],
  providers: [provideIcons({ lucideUsers2, lucideEdit, lucideTrash2, lucideShield, lucidePen, lucideEye })],
  template: `
    @if (loading() && !group()) {
      <div class="flex justify-center py-16">
        <hlm-spinner />
      </div>
    }

    @if (group(); as g) {
      <div class="space-y-6">
        <div class="flex items-start justify-between">
          <div>
            <h2 class="text-2xl font-semibold">{{ g.name }}</h2>
            @if (g.description) {
              <p class="text-muted-foreground mt-1">{{ g.description }}</p>
            }
          </div>
          <div class="flex items-center gap-2">
            <button hlmBtn variant="outline" size="sm" (click)="openEditSheet()">
              <ng-icon hlm name="lucideEdit" size="sm" class="mr-1" />
              Edit
            </button>
            <hlm-alert-dialog>
              <button hlmBtn variant="destructive" size="sm" hlmAlertDialogTrigger>
                <ng-icon hlm name="lucideTrash2" size="sm" class="mr-1" />
                Delete
              </button>
              <hlm-alert-dialog-content *hlmAlertDialogPortal="let ctx">
                <hlm-alert-dialog-header>
                  <h3 hlmAlertDialogTitle>Delete Group</h3>
                  <p hlmAlertDialogDescription>
                    This will remove all members. Documents in this group will become global. This action cannot be undone.
                  </p>
                </hlm-alert-dialog-header>
                <hlm-alert-dialog-footer>
                  <button hlmBtn variant="secondary" hlmAlertDialogCancel>Cancel</button>
                  <button hlmBtn variant="destructive" (click)="handleDelete(ctx)" [disabled]="deleting()">
                    {{ deleting() ? 'Deleting...' : 'Delete' }}
                  </button>
                </hlm-alert-dialog-footer>
              </hlm-alert-dialog-content>
            </hlm-alert-dialog>
          </div>
        </div>

        <div class="space-y-4">
          <div class="flex items-center justify-between">
            <h3 class="text-lg font-medium">Members ({{ g.members.length }})</h3>
            <app-add-member-dialog [groupId]="g.id" (added)="onRefresh()" />
          </div>

          @if (g.members.length === 0) {
            <p class="text-muted-foreground text-sm">No members yet. Add members to grant document access.</p>
          } @else {
            <div class="overflow-hidden rounded-md border">
              <div hlmTableContainer>
                <table hlmTable>
                  <thead hlmTHead>
                    <tr hlmTr>
                      <th hlmTh>Name</th>
                      <th hlmTh class="hidden md:table-cell">Email</th>
                      <th hlmTh class="w-40">Access Level</th>
                      <th hlmTh class="w-20"></th>
                    </tr>
                  </thead>
                  <tbody hlmTBody>
                    @for (member of g.members; track member.userId) {
                      <tr hlmTr>
                        <td hlmTd class="font-medium">{{ member.displayName }}</td>
                        <td hlmTd class="hidden md:table-cell text-muted-foreground">{{ member.email }}</td>
                        <td hlmTd>
                          <brn-select [value]="member.accessLevel"
                            (valueChange)="onAccessLevelChange(g.id, member.userId, $any($event))">
                            <hlm-select-trigger class="w-full h-8 text-xs">
                              <hlm-select-value />
                            </hlm-select-trigger>
                            <hlm-select-content>
                              <hlm-option value="Owner">Owner</hlm-option>
                              <hlm-option value="Editor">Editor</hlm-option>
                              <hlm-option value="Viewer">Viewer</hlm-option>
                            </hlm-select-content>
                          </brn-select>
                        </td>
                        <td hlmTd>
                          <button hlmBtn variant="ghost" size="icon"
                            (click)="onRemoveMember(g.id, member.userId, member.displayName)"
                            [attr.aria-label]="'Remove ' + member.displayName">
                            <ng-icon hlm name="lucideTrash2" size="sm" class="text-destructive" />
                          </button>
                        </td>
                      </tr>
                    }
                  </tbody>
                </table>
              </div>
            </div>
          }
        </div>
      </div>
    }

    <app-edit-group-sheet
      [group]="group()"
      [state]="sheetState()"
      (saved)="onSheetSaved()"
      (closed)="onSheetClosed()" />
  `,
})
export class GroupDetailPageComponent implements OnDestroy {
  private http = inject(HttpClient);
  private apiConfig = inject(ApiConfiguration);
  private router = inject(Router);
  private route = inject(ActivatedRoute);

  refreshTrigger = signal(0);
  loading = signal(false);
  deleting = signal(false);
  sheetState = signal<BrnDialogState>('closed');

  private groupId = toSignal(
    this.route.paramMap.pipe(map(p => p.get('id') ?? '')),
    { initialValue: '' },
  );

  group = toSignal(
    combineLatest([
      toObservable(this.groupId),
      toObservable(this.refreshTrigger),
    ]).pipe(
      switchMap(([id]) => {
        if (!id) return of(null);
        this.loading.set(true);
        return getGroup(this.http, this.apiConfig.rootUrl, { id }).pipe(
          map(r => {
            this.loading.set(false);
            return r.body as GroupDetailResponse;
          }),
          catchError(() => {
            this.loading.set(false);
            return of(null);
          }),
        );
      }),
    ),
    { initialValue: null },
  );

  ngOnDestroy(): void {}

  openEditSheet(): void {
    this.sheetState.set('open');
  }

  onSheetSaved(): void {
    this.sheetState.set('closed');
    this.onRefresh();
  }

  onSheetClosed(): void {
    this.sheetState.set('closed');
  }

  handleDelete(ctx: { close: () => void }): void {
    const g = this.group();
    if (!g) return;
    this.deleting.set(true);
    deleteGroup(this.http, this.apiConfig.rootUrl, { id: g.id }).subscribe({
      next: () => {
        this.deleting.set(false);
        ctx.close();
        toast.success('Group deleted.');
        this.router.navigate(['/admin/groups']);
      },
      error: () => {
        this.deleting.set(false);
        toast.error('Failed to delete group.');
      },
    });
  }

  onAccessLevelChange(groupId: string, userId: string, level: GroupAccessLevel): void {
    updateGroupMember(this.http, this.apiConfig.rootUrl, {
      id: groupId,
      userId,
      body: { accessLevel: level },
    }).subscribe({
      next: () => {
        toast.success('Access level updated.');
        this.onRefresh();
      },
      error: () => {
        toast.error('Failed to update access level.');
      },
    });
  }

  onRemoveMember(groupId: string, userId: string, name: string): void {
    removeGroupMember(this.http, this.apiConfig.rootUrl, { id: groupId, userId }).subscribe({
      next: () => {
        toast.success(`${name} removed from group.`);
        this.onRefresh();
      },
      error: () => {
        toast.error('Failed to remove member.');
      },
    });
  }

  onRefresh(): void {
    this.refreshTrigger.update(v => v + 1);
  }
}
