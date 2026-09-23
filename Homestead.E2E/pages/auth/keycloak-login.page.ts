import { type Locator, type Page, expect } from '@playwright/test';
import { KEYCLOAK_URL_PATTERN } from '../../e2e.config';
import { step } from '../../helpers/step';

export class KeycloakLoginPage {
  readonly usernameInput: Locator;
  readonly passwordInput: Locator;
  readonly signInButton: Locator;

  constructor(readonly page: Page) {
    this.usernameInput = page.getByLabel('Username or email');
    this.passwordInput = page.getByLabel('Password', { exact: true });
    this.signInButton = page.getByRole('button', { name: 'Sign In' });
  }

  @step()
  async login(username: string, password: string): Promise<void> {
    await expect(this.page).toHaveURL(KEYCLOAK_URL_PATTERN);
    await this.usernameInput.fill(username);
    await this.passwordInput.fill(password);
    await this.signInButton.click();
  }
}
