import {
  Component,
  ChangeDetectionStrategy,
  TemplateRef,
  afterNextRender,
  inject,
  viewChild,
  OnDestroy,
} from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideEye, lucideEyeOff } from '@ng-icons/lucide';
import { HlmIconImports } from '@/shared/components/spartan/icon';
import { HlmButton } from '@/shared/components/spartan/button';
import { PageHeaderService } from '@/shared/services/page-header.service';
import { UserPreferencesService } from '@/shared/services/user-preferences.service';
import { FinanceSettingsService } from '../shared/finance-settings.service';

@Component({
  selector: 'app-finances-layout',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterOutlet, NgIcon, HlmIconImports, HlmButton],
  providers: [provideIcons({ lucideEye, lucideEyeOff })],
  host: { class: 'flex min-h-0 flex-1 flex-col' },
  template: `
    <ng-template #privacyToggle>
      <button
        hlmBtn
        variant="ghost"
        size="icon-sm"
        [title]="prefs.financesPrivacyMasked() ? 'Show values' : 'Hide values'"
        [attr.aria-label]="prefs.financesPrivacyMasked() ? 'Show values' : 'Hide values'"
        [attr.aria-pressed]="prefs.financesPrivacyMasked()"
        (click)="togglePrivacy()"
      >
        <ng-icon
          hlm
          [name]="prefs.financesPrivacyMasked() ? 'lucideEyeOff' : 'lucideEye'"
          size="sm"
        />
      </button>
    </ng-template>

    <router-outlet />
  `,
})
export class FinancesLayoutComponent implements OnDestroy {
  protected readonly prefs = inject(UserPreferencesService);
  private readonly pageHeaderService = inject(PageHeaderService);
  private readonly privacyToggleTpl = viewChild.required<TemplateRef<unknown>>('privacyToggle');

  constructor() {
    inject(FinanceSettingsService).load();
    afterNextRender(() => {
      this.pageHeaderService.setSecondaryActions(this.privacyToggleTpl());
    });
  }

  ngOnDestroy(): void {
    this.pageHeaderService.clearSecondaryActions();
  }

  protected togglePrivacy(): void {
    this.prefs.financesPrivacyMasked.update(v => !v);
  }
}
