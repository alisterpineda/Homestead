import { type Locator, type Page } from '@playwright/test';

export class SetupPage {
  readonly heading: Locator;
  readonly description: Locator;
  readonly emailInput: Locator;
  readonly displayNameInput: Locator;
  readonly passwordInput: Locator;
  readonly confirmPasswordInput: Locator;
  readonly submitButton: Locator;

  constructor(readonly page: Page) {
    this.heading = page.getByRole('heading', { name: 'Welcome to Homestead' });
    this.description = page.getByText('Create your administrator account to get started');
    this.emailInput = page.getByLabel('Email');
    this.displayNameInput = page.getByLabel('Display name');
    this.passwordInput = page.getByLabel('Password', { exact: true });
    this.confirmPasswordInput = page.getByLabel('Confirm password');
    this.submitButton = page.getByRole('button', { name: 'Complete setup' });
  }

  async goto() {
    await this.page.goto('/setup');
  }

  async fillForm(email: string, displayName: string, password: string) {
    await this.emailInput.fill(email);
    await this.displayNameInput.fill(displayName);
    await this.passwordInput.fill(password);
    await this.confirmPasswordInput.fill(password);
  }

  async submit() {
    await this.submitButton.click();
  }
}
