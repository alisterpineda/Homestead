import { type Locator, type Page } from '@playwright/test';

export class KeycloakLoginPage {
  readonly usernameInput: Locator;
  readonly passwordInput: Locator;
  readonly signInButton: Locator;

  constructor(readonly page: Page) {
    this.usernameInput = page.getByLabel('Username or email');
    this.passwordInput = page.getByLabel('Password', { exact: true });
    this.signInButton = page.getByRole('button', { name: 'Sign In' });
  }

  async login(username: string, password: string) {
    await this.usernameInput.fill(username);
    await this.passwordInput.fill(password);
    await this.signInButton.click();
  }
}
