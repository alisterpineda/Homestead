import { Directive } from '@angular/core';
import { classes } from '@/shared/components/spartan/utils';

@Directive({
	selector: '[hlmPopoverDescription]',
	host: { 'data-slot': 'popover-description' },
})
export class HlmPopoverDescription {
	constructor() {
		classes(() => 'text-muted-foreground');
	}
}
