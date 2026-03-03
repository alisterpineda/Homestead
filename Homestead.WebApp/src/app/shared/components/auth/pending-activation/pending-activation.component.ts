import { Component, ChangeDetectionStrategy, inject } from '@angular/core';
import { AuthService } from '@/shared/services/auth.service';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmCardImports } from '@/shared/components/spartan/card';

@Component({
  selector: 'app-pending-activation',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [HlmButton, HlmCardImports],
  template: `
    <div class="flex min-h-screen items-center justify-center bg-background px-4">
      <div hlmCard class="w-full max-w-sm">
        <div hlmCardHeader class="text-center">
          <h1 hlmCardTitle>Account Pending</h1>
          <p hlmCardDescription>
            Your account is pending activation. Please contact an administrator.
          </p>
        </div>

        <div hlmCardContent>
          <button hlmBtn variant="outline" class="w-full" (click)="onLogout()">Sign out</button>
        </div>
      </div>
    </div>
  `,
})
export class PendingActivationComponent {
  private auth = inject(AuthService);

  onLogout(): void {
    this.auth.logout();
  }
}
