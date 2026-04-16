import { Component, ChangeDetectionStrategy, inject, signal, OnInit } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { AuthService } from '@/shared/services/auth.service';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmCardImports } from '@/shared/components/spartan/card';

const INACTIVE_ERROR = 'Your account is inactive. Please contact an administrator.';

@Component({
  selector: 'app-login',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [HlmButton, HlmCardImports],
  template: `
    <div class="flex min-h-screen items-center justify-center bg-background px-4">
      <div hlmCard class="w-full max-w-sm">
        <div hlmCardHeader class="text-center">
          <h1 hlmCardTitle>Welcome back</h1>
          <p hlmCardDescription>Sign in to your account</p>
        </div>

        <div hlmCardContent>
          <div class="flex flex-col gap-4">
            @if (error()) {
              <div
                class="rounded-md border border-destructive/50 bg-destructive/10 px-4 py-3 text-sm text-destructive"
                role="alert"
              >
                {{ error() }}
              </div>
            }

            <button hlmBtn type="button" class="w-full" (click)="onOidcLogin()">
              Sign in with {{ auth.oidcDisplayName() }}
            </button>
          </div>
        </div>
      </div>
    </div>
  `,
})
export class LoginComponent implements OnInit {
  private route = inject(ActivatedRoute);
  protected auth = inject(AuthService);

  protected error = signal('');

  ngOnInit(): void {
    const errorParam = this.route.snapshot.queryParamMap.get('error');
    if (errorParam === 'oidc-failed') {
      this.error.set('Single sign-on failed. Please try again.');
    } else if (errorParam === 'account-inactive') {
      this.error.set(INACTIVE_ERROR);
    }
  }

  onOidcLogin(): void {
    this.auth.loginWithOidc();
  }
}
