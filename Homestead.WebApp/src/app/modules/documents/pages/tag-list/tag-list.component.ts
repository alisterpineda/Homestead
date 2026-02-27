import { Component, ChangeDetectionStrategy, inject, signal, computed } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Router } from '@angular/router';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { switchMap, map, catchError, of } from 'rxjs';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideTags, lucidePlus, lucideSearch } from '@ng-icons/lucide';
import { HlmIconImports } from '@/shared/components/spartan/icon';
import { HlmSpinner } from '@/shared/components/spartan/spinner';
import { HlmEmptyImports } from '@/shared/components/spartan/empty';
import { HlmInput } from '@/shared/components/spartan/input';
import { TagTableComponent } from '../../components/tag-table/tag-table.component';
import { CreateTagDialogComponent } from '../../components/create-tag-dialog/create-tag-dialog.component';
import { ApiConfiguration } from '@/api/generated/api-configuration';
import { getTags } from '@/api/generated/fn/documents/get-tags';
import type { GetTagsResponse } from '@/api/generated/models/get-tags-response';

@Component({
  selector: 'app-tag-list',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    NgIcon,
    HlmIconImports,
    HlmSpinner,
    HlmEmptyImports,
    HlmInput,
    TagTableComponent,
    CreateTagDialogComponent,
  ],
  providers: [provideIcons({ lucideTags, lucidePlus, lucideSearch })],
  template: `
    <div>
      @if (loading() && !allTags()) {
        <div class="flex justify-center py-16">
          <hlm-spinner />
        </div>
      }

      @if (error()) {
        <section hlmEmpty>
          <hlm-empty-media variant="icon">
            <ng-icon hlm name="lucideTags" size="xl" />
          </hlm-empty-media>
          <div hlmEmptyHeader>
            <h3 hlmEmptyTitle>Failed to load tags</h3>
            <p hlmEmptyDescription>There was an error loading the tag list. Please try again.</p>
          </div>
        </section>
      }

      @if (allTags(); as tags) {
        @if (tags.length === 0 && !loading()) {
          <section hlmEmpty>
            <hlm-empty-media variant="icon">
              <ng-icon hlm name="lucideTags" size="xl" />
            </hlm-empty-media>
            <div hlmEmptyHeader>
              <h3 hlmEmptyTitle>No tags yet</h3>
              <p hlmEmptyDescription>Create your first tag to get started.</p>
            </div>
            <app-create-tag-dialog (created)="onRefresh()">
              <ng-icon hlm name="lucidePlus" size="sm" />
              New Tag
            </app-create-tag-dialog>
          </section>
        } @else {
          <div class="flex items-center gap-2 mb-4">
            <div class="relative">
              <ng-icon hlm name="lucideSearch" size="sm" class="absolute left-2.5 top-1/2 -translate-y-1/2 text-muted-foreground" />
              <input
                hlmInput
                type="text"
                placeholder="Search tags..."
                class="pl-8 h-8 w-[200px] text-sm"
                [value]="searchInput()"
                (input)="searchInput.set(asInputValue($event))"
              />
            </div>
            <div class="ml-auto flex items-center gap-2">
              <app-create-tag-dialog triggerSize="sm" (created)="onRefresh()">
                <ng-icon hlm name="lucidePlus" size="sm" />
                New Tag
              </app-create-tag-dialog>
            </div>
          </div>

          <app-tag-table
            [tags]="filteredTags()"
            (tagSelect)="onTagSelect($event)"
          />
        }
      }
    </div>
  `,
})
export class TagListComponent {
  refreshTrigger = signal(0);
  loading = signal(false);
  error = signal(false);
  searchInput = signal('');

  private http = inject(HttpClient);
  private apiConfig = inject(ApiConfiguration);
  private router = inject(Router);

  allTags = toSignal(
    toObservable(this.refreshTrigger).pipe(
      switchMap(() => {
        this.loading.set(true);
        this.error.set(false);
        return getTags(this.http, this.apiConfig.rootUrl, {
          IncludeChildren: true,
        }).pipe(
          map((r) => {
            this.loading.set(false);
            return r.body as GetTagsResponse[];
          }),
          catchError(() => {
            this.loading.set(false);
            this.error.set(true);
            return of(null);
          }),
        );
      }),
    ),
    { initialValue: null },
  );

  filteredTags = computed(() => {
    const tags = this.allTags();
    if (!tags) return [];
    const search = this.searchInput().toLowerCase().trim();
    if (!search) return tags;
    return tags.filter(
      (t) => t.name.toLowerCase().includes(search) || t.path.toLowerCase().includes(search),
    );
  });

  asInputValue(event: Event): string {
    return (event.target as HTMLInputElement).value;
  }

  onTagSelect(id: string): void {
    this.router.navigate(['/documents/tags', id]);
  }

  onRefresh(): void {
    this.refreshTrigger.update((v) => v + 1);
  }
}
