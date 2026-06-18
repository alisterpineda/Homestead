import {
  Component,
  ChangeDetectionStrategy,
  inject,
  input,
  output,
  signal,
  computed,
  effect,
} from '@angular/core';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideChevronDown, lucideSearch, lucideCheck } from '@ng-icons/lucide';
import { HlmIconImports } from '@/shared/components/spartan/icon';
import { HlmPopoverImports } from '@/shared/components/spartan/popover';
import { HlmInput } from '@/shared/components/spartan/input';
import { HlmSpinner } from '@/shared/components/spartan/spinner';
import { FinancesAccountApiService } from '@/api/services/finances-account-api.service';
import type { AccountResponse } from '@/api/generated/models/account-response';

// TODO: switch to server-side search when account count > ~50.
const PAGE_SIZE = 200;

@Component({
  selector: 'app-account-picker',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [NgIcon, HlmIconImports, HlmPopoverImports, HlmInput, HlmSpinner],
  providers: [provideIcons({ lucideChevronDown, lucideSearch, lucideCheck })],
  template: `
    <div hlmPopover [state]="popoverState()" (stateChanged)="popoverState.set($event)">
      <button
        hlmPopoverTrigger
        type="button"
        [disabled]="disabled()"
        [attr.aria-label]="ariaLabel() || 'Select account'"
        class="flex h-9 w-full items-center justify-between rounded-md border border-input bg-transparent px-3 py-1 text-sm shadow-xs transition-colors disabled:cursor-not-allowed disabled:opacity-50 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
      >
        @if (selected(); as sel) {
          <span class="truncate">{{ sel.name }}@if (sel.institution) { <span class="text-muted-foreground"> · {{ sel.institution }}</span>}</span>
        } @else {
          <span class="truncate text-muted-foreground">{{ placeholder() }}</span>
        }
        <ng-icon hlm name="lucideChevronDown" size="sm" class="text-muted-foreground" />
      </button>
      <div hlmPopoverContent *hlmPopoverPortal class="w-[360px] p-0" align="start">
        <div class="relative p-2">
          <ng-icon hlm name="lucideSearch" size="sm" class="absolute left-4 top-1/2 -translate-y-1/2 text-muted-foreground" />
          <input
            hlmInput
            class="h-8 w-full pl-8"
            placeholder="Search accounts..."
            [value]="searchTerm()"
            (input)="searchTerm.set(asValue($event))"
            aria-label="Search accounts"
          />
        </div>
        <div class="max-h-[280px] overflow-y-auto p-1">
          @if (loading()) {
            <div class="flex justify-center py-6">
              <hlm-spinner />
            </div>
          } @else if (results().length === 0) {
            <p class="py-4 text-center text-sm text-muted-foreground">No accounts found.</p>
          } @else {
            @for (a of results(); track a.id) {
              <button
                type="button"
                class="flex w-full items-center gap-2 rounded-sm px-2 py-1.5 text-sm hover:bg-accent cursor-pointer min-w-0"
                (click)="onSelect(a)"
              >
                <span class="size-4 shrink-0 flex items-center justify-center">
                  @if (a.id === value()) {
                    <ng-icon hlm name="lucideCheck" size="sm" />
                  }
                </span>
                <span class="font-medium truncate">{{ a.name }}</span>
                @if (a.institution) {
                  <span class="truncate text-muted-foreground">· {{ a.institution }}</span>
                }
              </button>
            }
          }
        </div>
      </div>
    </div>
  `,
})
export class AccountPickerComponent {
  value = input<string | null>(null);
  disabled = input(false);
  placeholder = input('Select account');
  ariaLabel = input<string | null>(null);
  excludeAccountId = input<string | null>(null);
  selectedAccount = input<AccountResponse | null>(null);

  valueChange = output<string>();
  accountChange = output<AccountResponse>();

  popoverState = signal<'open' | 'closed'>('closed');
  searchTerm = signal('');
  loading = signal(false);
  allAccounts = signal<AccountResponse[]>([]);
  loaded = signal(false);

  private accountApi = inject(FinancesAccountApiService);

  results = computed(() => {
    const exclude = this.excludeAccountId();
    const term = this.searchTerm().trim().toLowerCase();
    return this.allAccounts().filter(a => {
      if (exclude && a.id === exclude) return false;
      if (!term) return true;
      const haystack = `${a.name} ${a.institution ?? ''}`.toLowerCase();
      return haystack.includes(term);
    });
  });

  selected = computed(() => {
    const sel = this.selectedAccount();
    if (sel && sel.id === this.value()) return sel;
    return this.allAccounts().find(a => a.id === this.value()) ?? null;
  });

  constructor() {
    effect(() => {
      if (this.popoverState() === 'open' && !this.loaded()) {
        this.loading.set(true);
        this.accountApi.getAccounts({
          Page: 1,
          PageSize: PAGE_SIZE,
          IncludeArchived: false,
        }).subscribe({
          next: r => {
            this.allAccounts.set(r.items ?? []);
            this.loaded.set(true);
            this.loading.set(false);
          },
          error: () => {
            this.allAccounts.set([]);
            this.loaded.set(true);
            this.loading.set(false);
          },
        });
      }
    });
  }

  onSelect(account: AccountResponse): void {
    this.valueChange.emit(account.id);
    this.accountChange.emit(account);
    this.popoverState.set('closed');
  }

  asValue(event: Event): string {
    return (event.target as HTMLInputElement).value;
  }
}
