// @ts-check
import playwright from 'eslint-plugin-playwright';
import tseslint from 'typescript-eslint';

export default tseslint.config(
  { ignores: ['node_modules/', 'playwright-report/', 'test-results/', 'blob-report/', 'dist/'] },
  ...tseslint.configs.recommended,
  {
    files: ['**/*.ts'],
    languageOptions: {
      parserOptions: { projectService: true, tsconfigRootDir: import.meta.dirname },
    },
    rules: {
      // A missing `await` on a Playwright call is the classic silent E2E bug.
      '@typescript-eslint/no-floating-promises': 'error',
      '@typescript-eslint/no-misused-promises': 'error',
      '@typescript-eslint/await-thenable': 'error',
    },
  },
  {
    ...playwright.configs['flat/recommended'],
    files: ['tests/**/*.ts', 'pages/**/*.ts', 'fixtures/**/*.ts', 'helpers/**/*.ts'],
    rules: {
      ...playwright.configs['flat/recommended'].rules,
      'playwright/no-conditional-in-test': 'error',
      'playwright/no-conditional-expect': 'error',
      'playwright/no-wait-for-timeout': 'error',
      'playwright/no-wait-for-selector': 'error',
      'playwright/no-force-option': 'error',
      'playwright/no-networkidle': 'error',
      'playwright/no-page-pause': 'error',
      // Skipping per device with a reason (`test.skip(isMobile, '...')`) is fine; bare skips are not.
      'playwright/no-skipped-test': ['error', { allowConditional: true }],
      'playwright/prefer-web-first-assertions': 'error',
      'playwright/no-useless-not': 'error',
      'playwright/prefer-to-have-count': 'error',
      'playwright/prefer-to-have-length': 'error',
      'playwright/missing-playwright-await': 'error',
      // Only calls that check an outcome count. Seeding helpers (`*ViaApi`) and page-object actions
      // don't, so a test that only arranges data and navigates is still flagged.
      'playwright/expect-expect': ['error', { assertFunctionPatterns: ['^expect', '^assert', '^waitFor'] }],
    },
  },
);
