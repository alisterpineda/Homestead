import { type Locator, type Page, expect } from '@playwright/test';
import { step } from '../../helpers/step';
import { KeycloakLoginPage } from './keycloak-login.page';

export class LoginPage {
  readonly heading: Locator;
  readonly description: Locator;
  readonly oidcButton: Locator;

  constructor(readonly page: Page) {
    this.heading = page.getByRole('heading', { name: 'Welcome back' });
    this.description = page.getByText('Sign in to your account');
    this.oidcButton = page.getByRole('button', { name: /Sign in with/ });
  }

  async goto(): Promise<void> {
    await this.page.goto('/login');
  }

  /** The full OIDC round trip from a signed-out browser: app → login page → Keycloak → app. */
  @step()
  async signIn(user: { email: string; password: string }): Promise<void> {
    await this.page.goto('/');
    await expect(this.page).toHaveURL(/\/login(\?|$)/);
    await this.oidcButton.click();
    await new KeycloakLoginPage(this.page).login(user.email, user.password);
  }
}
