import { Component, ChangeDetectionStrategy, inject, signal, viewChild, afterNextRender, OnDestroy, TemplateRef } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Router, RouterOutlet, NavigationEnd } from '@angular/router';
import { filter } from 'rxjs';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideUpload } from '@ng-icons/lucide';
import { AuthService } from '@/shared/services/auth.service';
import { UploadManagerService } from '@/shared/services/upload-manager.service';
import { PageHeaderService } from '@/shared/services/page-header.service';
import { UploadPopoverComponent } from '../list/upload-popover/upload-popover.component';

@Component({
  selector: 'app-documents-layout',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterOutlet, NgIcon, UploadPopoverComponent],
  providers: [provideIcons({ lucideUpload })],
  host: {
    class: 'flex min-h-0 flex-1 flex-col',
    '(window:dragenter)': 'onDragEnter($event)',
    '(window:dragleave)': 'onDragLeave($event)',
    '(window:dragover)': 'onDragOver($event)',
    '(window:drop)': 'onDrop($event)',
  },
  template: `
    <ng-template #headerActions>
      @if (auth.canWrite()) {
        <app-upload-popover />
      }
    </ng-template>
    <router-outlet />
    @if (dragActive() && auth.canWrite()) {
      <div class="fixed inset-0 z-50 flex items-center justify-center bg-background/80 backdrop-blur-sm p-3">
        <div class="flex h-full w-full items-center justify-center rounded-lg border-2 border-dashed border-primary">
          <div class="flex flex-col items-center gap-2 text-primary">
            <ng-icon name="lucideUpload" class="text-[length:--spacing(12)]" />
            <p class="text-lg font-medium">Drop files to upload</p>
            <p class="text-sm text-muted-foreground">Files will be uploaded and processed automatically</p>
          </div>
        </div>
      </div>
    }
  `,
})
export class DocumentsLayoutComponent implements OnDestroy {
  protected auth = inject(AuthService);
  private uploadManager = inject(UploadManagerService);
  private pageHeaderService = inject(PageHeaderService);
  private router = inject(Router);

  private headerActionsTpl = viewChild.required<TemplateRef<unknown>>('headerActions');

  protected dragActive = signal(false);
  private dragCounter = 0;

  constructor() {
    afterNextRender(() => {
      this.pageHeaderService.setActions(this.headerActionsTpl());
    });

    // Re-register actions on NavigationEnd because AppShellComponent clears on NavigationStart
    this.router.events
      .pipe(
        filter(e => e instanceof NavigationEnd),
        takeUntilDestroyed(),
      )
      .subscribe(() => {
        this.pageHeaderService.setActions(this.headerActionsTpl());
      });
  }

  ngOnDestroy(): void {
    this.pageHeaderService.clearActions();
  }

  onDragEnter(event: DragEvent): void {
    event.preventDefault();
    if (!this.auth.canWrite()) return;
    if (!event.dataTransfer?.types.includes('Files')) return;
    if (this.isDialogOpen()) return;
    this.dragCounter++;
    this.dragActive.set(true);
  }

  onDragLeave(event: DragEvent): void {
    event.preventDefault();
    this.dragCounter--;
    if (this.dragCounter <= 0) {
      this.dragCounter = 0;
      this.dragActive.set(false);
    }
  }

  onDragOver(event: DragEvent): void {
    event.preventDefault();
  }

  onDrop(event: DragEvent): void {
    event.preventDefault();
    this.dragCounter = 0;
    this.dragActive.set(false);

    if (!this.auth.canWrite()) return;
    if (this.isDialogOpen()) return;

    const files = event.dataTransfer?.files;
    if (files && files.length > 0) {
      this.uploadManager.addFiles(Array.from(files));
    }
  }

  private isDialogOpen(): boolean {
    return document.querySelector(
      '.cdk-overlay-container [role="dialog"], .cdk-overlay-container [role="alertdialog"]'
    ) !== null;
  }
}
