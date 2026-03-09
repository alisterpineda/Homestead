import { Component, ChangeDetectionStrategy, inject, computed } from '@angular/core';
import { Router, RouterLink, NavigationEnd } from '@angular/router';
import { toSignal } from '@angular/core/rxjs-interop';
import { filter, map } from 'rxjs';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideFileText, lucideFiles, lucideUsers, lucideTags, lucideTrash2, lucideX } from '@ng-icons/lucide';
import { HlmIconImports } from '@/shared/components/spartan/icon';
import { HlmSidebarImports } from '@/shared/components/spartan/sidebar';
import { isNavItemActive } from '@/shared/utils';
import { OpenDocumentsService } from '../../services/open-documents.service';

@Component({
  selector: 'app-documents-sidebar',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    RouterLink,
    NgIcon,
    HlmIconImports,
    HlmSidebarImports,
  ],
  providers: [provideIcons({ lucideFileText, lucideFiles, lucideUsers, lucideTags, lucideTrash2, lucideX })],
  template: `
    <div hlmSidebarGroup>
      <div hlmSidebarGroupContent>
        <ul hlmSidebarMenu>
          <li hlmSidebarMenuItem>
            <a hlmSidebarMenuButton routerLink="/documents" [isActive]="isActive('/documents')">
              <ng-icon hlm name="lucideFiles" size="sm" />
              <span>Documents</span>
            </a>
          </li>
        </ul>
      </div>
    </div>

    @if (showOpenDocuments()) {
      <div hlmSidebarGroup>
        <div hlmSidebarGroupLabel class="justify-between">
          <span>Open <span class="text-muted-foreground">({{ openDocumentsService.openDocuments().length }})</span></span>
          <button class="text-xs text-muted-foreground hover:text-foreground transition-colors" (click)="clearAllOpenDocuments()">Close all</button>
        </div>
        <div hlmSidebarGroupContent>
          <ul hlmSidebarMenu>
            @for (doc of openDocumentsService.openDocuments(); track doc.id) {
              <li hlmSidebarMenuItem class="group/menu-item">
                <a
                  hlmSidebarMenuButton
                  size="sm"
                  [routerLink]="'/documents/' + doc.id"
                  [isActive]="isOpenDocActive(doc.id)"
                >
                  <ng-icon hlm name="lucideFileText" size="sm" />
                  <span>{{ doc.name }}</span>
                </a>
                <button hlmSidebarMenuAction [showOnHover]="true" (click)="closeOpenDocument($event, doc.id)" aria-label="Close document">
                  <ng-icon hlm name="lucideX" />
                </button>
              </li>
            }
          </ul>
        </div>
      </div>
    }

    <div hlmSidebarGroup>
      <div hlmSidebarGroupLabel>Manage</div>
      <div hlmSidebarGroupContent>
        <ul hlmSidebarMenu>
          <li hlmSidebarMenuItem>
            <a hlmSidebarMenuButton routerLink="/documents/parties" [isActive]="isActive('/documents/parties')">
              <ng-icon hlm name="lucideUsers" size="sm" />
              <span>Parties</span>
            </a>
          </li>
          <li hlmSidebarMenuItem>
            <a hlmSidebarMenuButton routerLink="/documents/tags" [isActive]="isActive('/documents/tags')">
              <ng-icon hlm name="lucideTags" size="sm" />
              <span>Tags</span>
            </a>
          </li>
        </ul>
      </div>
    </div>

    <div hlmSidebarGroup>
      <div hlmSidebarGroupContent>
        <ul hlmSidebarMenu>
          <li hlmSidebarMenuItem>
            <a hlmSidebarMenuButton routerLink="/documents/trash" [isActive]="isActive('/documents/trash')">
              <ng-icon hlm name="lucideTrash2" size="sm" />
              <span>Trash</span>
            </a>
          </li>
        </ul>
      </div>
    </div>
  `,
})
export class DocumentsSidebarComponent {
  private router = inject(Router);
  protected openDocumentsService = inject(OpenDocumentsService);

  private url = toSignal(
    this.router.events.pipe(
      filter((e) => e instanceof NavigationEnd),
      map((e) => (e as NavigationEnd).urlAfterRedirects),
    ),
    { initialValue: this.router.url },
  );

  private urlPath = computed(() => this.url().split('?')[0]);

  protected showOpenDocuments = computed(() => this.openDocumentsService.openDocuments().length > 0);

  /** More specific paths (e.g. /documents/parties) take priority over shorter ones (e.g. /documents). */
  private readonly navPaths = ['/documents', '/documents/parties', '/documents/tags', '/documents/trash'];

  isActive(path: string): boolean {
    return isNavItemActive(this.urlPath(), path, this.navPaths);
  }

  isOpenDocActive(docId: string): boolean {
    const url = this.urlPath();
    return url === `/documents/${docId}` || url.startsWith(`/documents/${docId}/`);
  }

  closeOpenDocument(event: Event, docId: string): void {
    event.stopPropagation();
    const isActive = this.isOpenDocActive(docId);
    this.openDocumentsService.removeDocument(docId);
    if (isActive) {
      this.router.navigate(['/documents']);
    }
  }

  clearAllOpenDocuments(): void {
    const docs = this.openDocumentsService.openDocuments();
    const isOnOpenDoc = docs.some(doc => this.isOpenDocActive(doc.id));
    this.openDocumentsService.clearAll();
    if (isOnOpenDoc) {
      this.router.navigate(['/documents']);
    }
  }
}
