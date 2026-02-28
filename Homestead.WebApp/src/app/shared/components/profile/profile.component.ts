import { ChangeDetectionStrategy, Component, computed, inject, viewChild } from '@angular/core';
import { HlmDialogImports } from '@/shared/components/spartan/dialog';
import { HlmSeparator } from '@/shared/components/spartan/separator';
import { AuthService } from '@/shared/services/auth.service';
import { BrnDialog } from '@spartan-ng/brain/dialog';

@Component({
  selector: 'app-profile-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [HlmDialogImports, HlmSeparator],
  template: `
    <hlm-dialog>
      <button class="hidden" hlmDialogTrigger>Open</button>

      <hlm-dialog-content *hlmDialogPortal="let ctx">
        <div hlmDialogHeader>
          <h2 hlmDialogTitle>Profile</h2>
          <p hlmDialogDescription>Your account information.</p>
        </div>

        <div class="flex flex-col items-center gap-4 py-2">
          <div
            class="flex h-20 w-20 items-center justify-center rounded-full bg-accent text-2xl font-semibold text-accent-foreground"
            aria-hidden="true"
          >
            {{ initials() }}
          </div>

          <hlm-separator />

          <dl class="w-full space-y-4">
            <div>
              <dt class="text-sm font-medium text-muted-foreground">Display Name</dt>
              <dd class="mt-1 text-sm">{{ auth.userName() }}</dd>
            </div>
            <div>
              <dt class="text-sm font-medium text-muted-foreground">Email</dt>
              <dd class="mt-1 text-sm">{{ auth.userEmail() }}</dd>
            </div>
          </dl>
        </div>
      </hlm-dialog-content>
    </hlm-dialog>
  `,
})
export class ProfileDialogComponent {
  protected auth = inject(AuthService);

  private dialog = viewChild(BrnDialog);

  open(): void {
    this.dialog()?.open();
  }

  protected initials = computed(() => {
    const name = this.auth.userName();
    if (!name) return '?';
    return name
      .split(/\s+/)
      .map((part) => part[0])
      .join('')
      .toUpperCase()
      .slice(0, 2);
  });
}
