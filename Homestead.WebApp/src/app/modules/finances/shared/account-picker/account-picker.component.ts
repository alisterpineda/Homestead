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
import { lucideChevronDown, lucideCheck } from '@ng-icons/lucide';
import { HlmPopoverImports } from '@/shared/components/spartan/popover';
import { SearchInputComponent } from '@/shared/components/search-input/search-input.component';
import { PickerTriggerDirective } from '@/shared/directives/picker-trigger.directive';
import { HlmSpinner } from '@/shared/components/spartan/spinner';
import { FinancesAccountApiService } from '@/api/services/finances-account-api.service';
import type { AccountResponse } from '@/api/generated/models/account-response';

// TODO: switch to server-side search when account count > ~50.
const PAGE_SIZE = 200;

@Component({
  selector: 'app-account-picker',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [NgIcon, HlmPopoverImports, SearchInputComponent, HlmSpinner, PickerTriggerDirective],
  providers: [provideIcons({ lucideChevronDown, lucideCheck })],
  template: `
    <div hlmPopover [state]="popoverState()" (stateChanged)="popoverState.set($event)">
      <button
        hlmPopoverTrigger
        appPickerTrigger
        type="button"
        [disabled]="disabled()"
        [attr.aria-label]="ariaLabel() || 'Select account'"
      >
        @if (selected(); as sel) {
          <span class="min-w-0 truncate">{{ sel.name }}@if (sel.institution) { <span class="text-muted-foreground"> · {{ sel.institution }}</span>}</span>
        } @else {
          <span class="min-w-0 truncate text-muted-foreground">{{ placeholder() }}</span>
        }
        <ng-icon name="lucideChevronDown" class="text-[length:--spacing(4)] shrink-0 text-muted-foreground" />
      </button>
      <div hlmPopoverContent *hlmPopoverPortal class="w-[360px] p-0" align="start">
        <div class="p-2">
          <app-search-input [(value)]="searchTerm" placeholder="Search accounts..." ariaLabel="Search accounts" />
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
                    <ng-icon name="lucideCheck" class="text-[length:--spacing(4)]" />
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
}
