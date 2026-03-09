import { Type } from '@angular/core';

export interface ModuleDefinition {
  id: string;
  label: string;
  labelColor: string;
  icon: string;
  basePath: string;
  sidebarComponent: Type<unknown>;
  requiredRole?: string;
}
