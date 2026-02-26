import { Injectable, signal, TemplateRef } from '@angular/core';

@Injectable({ providedIn: 'root' })
export class PageHeaderService {
  readonly actions = signal<TemplateRef<unknown> | null>(null);
  readonly title = signal<TemplateRef<unknown> | null>(null);

  setActions(template: TemplateRef<unknown>): void {
    this.actions.set(template);
  }

  setTitle(template: TemplateRef<unknown>): void {
    this.title.set(template);
  }

  clearActions(): void {
    this.actions.set(null);
  }

  clearTitle(): void {
    this.title.set(null);
  }
}
