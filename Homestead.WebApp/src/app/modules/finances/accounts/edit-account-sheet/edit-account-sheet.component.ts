import {
  Component,
  ChangeDetectionStrategy,
  input,
  output,
  signal,
  computed,
  inject,
  effect,
  untracked,
} from '@angular/core';
import { HlmSheetImports } from '@/shared/components/spartan/sheet';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmLabel } from '@/shared/components/spartan/label';
import { HlmInput } from '@/shared/components/spartan/input';
import { HlmCheckboxImports } from '@/shared/components/spartan/checkbox';
import { FinancesAccountApiService } from '@/api/services/finances-account-api.service';
import { FinancesCommodityApiService } from '@/api/services/finances-commodity-api.service';
import { replace } from '@/shared/json-patch';
import type { JsonPatchDocument } from '@/api/generated/models/json-patch-document';
import type { AccountResponse } from '@/api/generated/models/account-response';
import type { CommodityResponse } from '@/api/generated/models/commodity-response';
import type { BrnDialogState } from '@spartan-ng/brain/dialog';
import { toast } from '@spartan-ng/brain/sonner';
import { ErrorHandlerService } from '@/shared/services/error-handler.service';
import { handleMutationError } from '@/shared/utils/rxjs-error.utils';
import { seededLinkedSignal } from '@/shared/utils/seeded-linked-signal';
import { KIND_LABELS, isUserAccountKind, type UserAccountKind } from '../../shared/account-kind-meta';

@Component({
  selector: 'app-edit-account-sheet',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    HlmSheetImports,
    HlmButton,
    HlmLabel,
    HlmInput,
    HlmCheckboxImports,
  ],
  template: `
    <hlm-sheet side="right" [state]="state()" (stateChanged)="onStateChanged($event)">
      <hlm-sheet-content *hlmSheetPortal>
        <hlm-sheet-header>
          <h3 hlmSheetTitle>Edit account</h3>
          <p hlmSheetDescription>Kind and reporting currency cannot be changed.</p>
        </hlm-sheet-header>

        @if (account(); as a) {
          <div class="flex-1 space-y-6 overflow-y-auto px-4 py-4">
            <div class="space-y-2">
              <label hlmLabel for="account-name">Name</label>
              <input
                hlmInput
                id="account-name"
                class="w-full"
                maxlength="256"
                [value]="editName()"
                (input)="editName.set(asValue($event))"
              />
            </div>

            <div class="space-y-2">
              <label hlmLabel for="account-institution">Institution</label>
              <input
                hlmInput
                id="account-institution"
                class="w-full"
                maxlength="128"
                placeholder="(none)"
                [value]="editInstitution()"
                (input)="editInstitution.set(asValue($event))"
              />
            </div>

            <div class="space-y-2">
              <label hlmLabel>Kind</label>
              <p class="text-sm text-muted-foreground">{{ kindLabel() }}</p>
            </div>

            <div class="space-y-2">
              <label hlmLabel>Reporting currency</label>
              <p class="text-sm text-muted-foreground">{{ currencyLabel() ?? '—' }}</p>
            </div>

            <div class="flex items-center gap-2">
              <hlm-checkbox
                id="account-archived"
                [checked]="editArchived()"
                (checkedChange)="editArchived.set($event)"
              />
              <label hlmLabel for="account-archived">Archived</label>
            </div>
          </div>
        }

        <hlm-sheet-footer class="flex flex-col gap-2">
          <button hlmBtn class="w-full" (click)="handleSave()" [disabled]="!hasChanges() || saving()">
            {{ saving() ? 'Saving...' : 'Save' }}
          </button>
        </hlm-sheet-footer>
      </hlm-sheet-content>
    </hlm-sheet>
  `,
})
export class EditAccountSheetComponent {
  account = input<AccountResponse | null>(null);
  state = input<BrnDialogState>('closed');
  saved = output<AccountResponse>();
  closed = output<void>();

  private seedKey = computed(() =>
    `${this.account()?.id ?? ''}|${this.state() === 'open'}`,
  );

  editName = seededLinkedSignal(this.seedKey, () => {
    const a = this.account();
    if (!a || this.state() !== 'open') return '';
    return a.name;
  });
  editInstitution = seededLinkedSignal(this.seedKey, () => {
    const a = this.account();
    if (!a || this.state() !== 'open') return '';
    return a.institution ?? '';
  });
  editArchived = seededLinkedSignal(this.seedKey, () => {
    const a = this.account();
    if (!a || this.state() !== 'open') return false;
    return a.isArchived;
  });
  saving = seededLinkedSignal(this.seedKey, () => false);
  reportingCurrency = signal<CommodityResponse | null>(null);

  private accountApi = inject(FinancesAccountApiService);
  private commodityApi = inject(FinancesCommodityApiService);
  private errorHandler = inject(ErrorHandlerService);

  hasChanges = computed(() => {
    const a = this.account();
    if (!a) return false;
    if (this.editName() !== a.name) return true;
    if ((this.editInstitution() || null) !== (a.institution ?? null)) return true;
    if (this.editArchived() !== a.isArchived) return true;
    return false;
  });

  kindLabel = computed(() => {
    const k = this.account()?.kind ?? null;
    return isUserAccountKind(k) ? KIND_LABELS[k as UserAccountKind] : '—';
  });

  currencyLabel = computed(() => {
    const c = this.reportingCurrency();
    return c ? `${c.symbol} — ${c.name}` : null;
  });

  constructor() {
    effect(() => {
      this.seedKey();
      const a = untracked(() => {
        return this.state() === 'open' ? this.account() : null;
      });
      if (!a) return;
      this.reportingCurrency.set(null);
      this.commodityApi.getCommodity({ id: a.reportingCurrencyId }).subscribe({
        next: c => this.reportingCurrency.set(c),
        error: () => this.reportingCurrency.set(null),
      });
    });
  }

  asValue(event: Event): string {
    return (event.target as HTMLInputElement).value;
  }

  handleSave(): void {
    const a = this.account();
    if (!a || !this.hasChanges()) return;
    this.saving.set(true);
    const ops: JsonPatchDocument = [];
    if (this.editName() !== a.name) ops.push(replace('/name', this.editName().trim()));
    const newInst = this.editInstitution().trim() ? this.editInstitution().trim() : null;
    if (newInst !== (a.institution ?? null)) ops.push(replace('/institution', newInst));
    if (this.editArchived() !== a.isArchived) ops.push(replace('/isArchived', this.editArchived()));

    this.accountApi.patchAccount({ id: a.id, body: ops }).pipe(
      handleMutationError(this.errorHandler, this.saving, 'Failed to save changes.'),
    ).subscribe({
      next: (updated) => {
        this.saving.set(false);
        toast.success('Account updated.');
        this.saved.emit(updated);
      },
    });
  }

  onStateChanged(state: BrnDialogState): void {
    if (state === 'closed') {
      this.closed.emit();
    }
  }
}
