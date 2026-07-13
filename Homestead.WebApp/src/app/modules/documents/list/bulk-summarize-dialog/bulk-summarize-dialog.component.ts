import { Component, ChangeDetectionStrategy, input, output, computed } from '@angular/core';

import { HlmButton } from '@/shared/components/spartan/button';
import { HlmIconImports } from '@/shared/components/spartan/icon';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideFileText } from '@ng-icons/lucide';
import type { DocumentSuggestionField } from '@/api/generated/models/document-suggestion-field';
import { ClassifyOptionsDialogComponent } from '../../shared/classify-options-dialog/classify-options-dialog.component';

/**
 * Bulk-summarize trigger: a button that opens the shared classify-options dialog with field
 * checkboxes hidden. Presentational — the parent list performs the API call.
 */
@Component({
  selector: 'app-bulk-summarize-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [HlmButton, HlmIconImports, NgIcon, ClassifyOptionsDialogComponent],
  providers: [provideIcons({ lucideFileText })],
  template: `
    <button hlmBtn variant="outline" [disabled]="disabled()" (click)="optionsDialog.open()">
      <ng-icon hlm name="lucideFileText" size="sm" />
      Summarize
    </button>

    <app-classify-options-dialog
      #optionsDialog
      mode="bulk"
      [documentCount]="documentCount()"
      [showFields]="false"
      (confirmed)="confirmed.emit($event)"
    />
  `,
})
export class BulkSummarizeDialogComponent {
  documentIds = input.required<Set<string>>();
  disabled = input(false);
  confirmed = output<{ fields: DocumentSuggestionField[] }>();

  documentCount = computed(() => this.documentIds().size);
}
