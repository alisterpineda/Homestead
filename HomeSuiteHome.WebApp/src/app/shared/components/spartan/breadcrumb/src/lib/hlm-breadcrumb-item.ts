import { Directive } from '@angular/core';
import { classes } from '@/shared/components/spartan/utils';

@Directive({
	selector: '[hlmBreadcrumbItem]',
})
export class HlmBreadcrumbItem {
	constructor() {
		classes(() => 'inline-flex items-center gap-1.5');
	}
}
