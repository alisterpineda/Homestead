import { Component, ChangeDetectionStrategy, inject, signal, computed, input, output, viewChild, DestroyRef } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideUserPlus } from '@ng-icons/lucide';
import { HlmDialogImports } from '@/shared/components/spartan/dialog';
import { HlmButton } from '@/shared/components/spartan/button';
import { SearchInputComponent } from '@/shared/components/search-input/search-input.component';
import { HlmLabel } from '@/shared/components/spartan/label';
import { HlmSelectImports } from '@/shared/components/spartan/select';
import { BrnDialog } from '@spartan-ng/brain/dialog';

import { AdminApiService } from '@/api/services/admin-api.service';
import type { UserResponse } from '@/api/generated/models/user-response';
import type { GroupAccessLevel } from '@/api/generated/models/group-access-level';
import { ErrorHandlerService } from '@/shared/services/error-handler.service';
import { toast } from '@spartan-ng/brain/sonner';

@Component({
  selector: 'app-add-member-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [NgIcon, HlmDialogImports, HlmButton, SearchInputComponent, HlmLabel, HlmSelectImports, ],
  providers: [provideIcons({ lucideUserPlus })],
  template: `
    <hlm-dialog (stateChanged)="onStateChanged($event)">
      <button hlmBtn size="sm" hlmDialogTrigger>
        <ng-icon name="lucideUserPlus" class="text-[length:--spacing(4)] mr-1" />
        Add Member
      </button>

      <hlm-dialog-content *hlmDialogPortal="let ctx">
        <div hlmDialogHeader>
          <h2 hlmDialogTitle>Add Member</h2>
          <p hlmDialogDescription>Add a user to this group.</p>
        </div>

        <form (submit)="handleSubmit($event)" class="flex flex-col gap-4">
          <div class="flex flex-col gap-2">
            <label hlmLabel for="member-search">Search Users</label>
            <app-search-input
              inputId="member-search"
              ariaLabel="Search Users"
              placeholder="Search by name or email"
              [value]="userSearch()"
              (valueChange)="onSearchInput($event)"
            />
            @if (loadingUsers()) {
              <p class="text-xs text-muted-foreground">Loading...</p>
            }
            @if (userResults().length > 0) {
              <div class="max-h-40 overflow-y-auto rounded-md border">
                @for (user of userResults(); track user.id) {
                  <button type="button"
                    class="flex w-full items-center gap-2 px-3 py-2 text-left text-sm hover:bg-muted"
                    [class.bg-accent]="selectedUserId() === user.id"
                    (click)="selectUser(user)">
                    <span class="font-medium">{{ user.displayName }}</span>
                    <span class="text-muted-foreground">{{ user.email }}</span>
                  </button>
                }
              </div>
            }
            @if (selectedUser(); as u) {
              <p class="text-sm">Selected: <strong>{{ u.displayName }}</strong> ({{ u.email }})</p>
            }
          </div>

          <div class="flex flex-col gap-2">
            <label hlmLabel>Access Level</label>
            <hlm-select [value]="accessLevel()" (valueChange)="accessLevel.set($any($event))">
              <hlm-select-trigger class="w-full">
                <hlm-select-value />
              </hlm-select-trigger>
              <hlm-select-content *hlmSelectPortal>
<hlm-select-group>
                <hlm-select-item value="Owner">Owner</hlm-select-item>
                <hlm-select-item value="Editor">Editor</hlm-select-item>
                <hlm-select-item value="Viewer">Viewer</hlm-select-item>
              </hlm-select-group>
</hlm-select-content>
            </hlm-select>
          </div>

          @if (addError()) {
            <p class="text-sm text-destructive">{{ addError() }}</p>
          }

          <div hlmDialogFooter>
            <button hlmBtn variant="secondary" type="button" hlmDialogClose>Cancel</button>
            <button hlmBtn type="submit" [disabled]="!canSubmit() || adding()">
              {{ adding() ? 'Adding...' : 'Add' }}
            </button>
          </div>
        </form>
      </hlm-dialog-content>
    </hlm-dialog>
  `,
})
export class AddMemberDialogComponent {
  groupId = input.required<string>();
  added = output();

  private adminApi = inject(AdminApiService);
  private errorHandler = inject(ErrorHandlerService);
  private destroyRef = inject(DestroyRef);
  private dialog = viewChild(BrnDialog);

  userSearch = signal('');
  userResults = signal<UserResponse[]>([]);
  loadingUsers = signal(false);
  selectedUser = signal<UserResponse | null>(null);
  selectedUserId = computed(() => this.selectedUser()?.id ?? null);
  accessLevel = signal<GroupAccessLevel>('Editor');
  adding = signal(false);
  addError = signal('');

  canSubmit = computed(() => this.selectedUserId() !== null);

  private searchTimeout: ReturnType<typeof setTimeout> | null = null;

  constructor() {
    this.destroyRef.onDestroy(() => {
      if (this.searchTimeout) clearTimeout(this.searchTimeout);
    });
  }

  onSearchInput(value: string): void {
    this.userSearch.set(value);
    if (this.searchTimeout) clearTimeout(this.searchTimeout);
    if (value.trim().length < 2) {
      this.userResults.set([]);
      return;
    }
    this.searchTimeout = setTimeout(() => this.searchUsers(value), 300);
  }

  private searchUsers(search: string): void {
    this.loadingUsers.set(true);
    this.adminApi.getUsers({
      Page: 1,
      PageSize: 10,
      Search: search,
    }).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (r) => {
        this.loadingUsers.set(false);
        const body = r as { items: UserResponse[] };
        this.userResults.set(body.items?.filter((u: UserResponse) => u.isActive) ?? []);
      },
      error: () => {
        this.loadingUsers.set(false);
        this.userResults.set([]);
      },
    });
  }

  selectUser(user: UserResponse): void {
    this.selectedUser.set(user);
    this.userResults.set([]);
    this.userSearch.set(user.displayName);
  }

  onStateChanged(state: 'open' | 'closed'): void {
    if (state === 'open') {
      this.userSearch.set('');
      this.userResults.set([]);
      this.selectedUser.set(null);
      this.accessLevel.set('Editor');
      this.adding.set(false);
      this.addError.set('');
    }
  }

  handleSubmit(event: Event): void {
    event.preventDefault();
    const userId = this.selectedUserId();
    if (!userId) return;

    this.adding.set(true);
    this.addError.set('');

    this.adminApi.addGroupMember({
      id: this.groupId(),
      body: {
        userId,
        accessLevel: this.accessLevel(),
      },
    }).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => {
        this.adding.set(false);
        toast.success('Member added.');
        this.dialog()?.close();
        this.added.emit();
      },
      error: (err: unknown) => {
        this.adding.set(false);
        this.addError.set(this.errorHandler.getErrorMessage(err, 'Failed to add member.'));
      },
    });
  }
}
