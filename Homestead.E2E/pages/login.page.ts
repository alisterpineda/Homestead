import { type Locator, type Page } from '@playwright/test';

export class LoginPage {
  readonly heading: Locator;
  readonly description: Locator;
  readonly emailInput: Locator;
  readonly passwordInput: Locator;
  readonly signInButton: Locator;
  readonly oidcButton: Locator;
  readonly divider: Locator;

  constructor(readonly page: Page) {
    this.heading = page.getByRole('heading', { name: 'Welcome back' });
    this.description = page.getByText('Sign in to your account');
    this.emailInput = page.getByLabel('Email');
    this.passwordInput = page.getByLabel('Password');
    this.signInButton = page.getByRole('button', { name: /^Sign in$/ });
    this.oidcButton = page.getByRole('button', { name: /Sign in with/ });
    this.divider = page.getByText('or', { exact: true });
  }

  async goto() {
    await this.page.goto('/login');
  }

  async login(email: string, password: string) {
    await this.emailInput.fill(email);
    await this.passwordInput.fill(password);
    await this.signInButton.click();
  }
}
