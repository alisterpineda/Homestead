import { Component, ChangeDetectionStrategy, input, computed } from '@angular/core';
import { HlmBadge } from '@/shared/components/spartan/badge';
import { KIND_LABELS, isUserAccountKind, type UserAccountKind } from './account-kind-meta';
import type { AccountKind } from '@/api/generated/models/account-kind';

@Component({
  selector: 'app-kind-badge',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [HlmBadge],
  template: `
    @if (label()) {
      <span hlmBadge variant="secondary">{{ label() }}</span>
    }
  `,
})
export class KindBadgeComponent {
  kind = input<AccountKind | null | undefined>(null);

  label = computed<string | null>(() => {
    const k = this.kind();
    return isUserAccountKind(k) ? KIND_LABELS[k as UserAccountKind] : null;
  });
}
