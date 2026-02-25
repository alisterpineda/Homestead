import { Directive } from '@angular/core';
import { classes } from '@/components/ui/utils';

@Directive({
	selector: 'li[hlmPaginationItem]',
	host: {
		'data-slot': 'pagination-item',
	},
})
export class HlmPaginationItem {
	constructor() {
		classes(() => '');
	}
}
