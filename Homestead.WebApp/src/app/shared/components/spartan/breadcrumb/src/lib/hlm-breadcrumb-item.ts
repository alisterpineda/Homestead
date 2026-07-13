import { Directive } from '@angular/core';
import { classes } from '@/shared/components/spartan/utils';

@Directive({
	selector: '[hlmBreadcrumbItem]',
	host: {
		'data-slot': 'breadcrumb-item',
	},
})
export class HlmBreadcrumbItem {
	constructor() {
		classes(() => 'gap-1 inline-flex items-center');
	}
}
