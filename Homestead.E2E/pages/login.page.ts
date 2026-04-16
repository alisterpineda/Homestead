import { type Locator, type Page } from '@playwright/test';

export class LoginPage {
  readonly heading: Locator;
  readonly description: Locator;
  readonly oidcButton: Locator;

  constructor(readonly page: Page) {
    this.heading = page.getByRole('heading', { name: 'Welcome back' });
    this.description = page.getByText('Sign in to your account');
    this.oidcButton = page.getByRole('button', { name: /Sign in with/ });
  }

  async goto() {
    await this.page.goto('/login');
  }
}
