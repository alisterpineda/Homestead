import { Directive } from '@angular/core';
import { HlmInput, inputVariants } from '@/shared/components/spartan/input';
import { classes } from '@/shared/components/spartan/utils';

@Directive({
	selector: 'input[hlmSidebarInput]',
	host: {
		'data-slot': 'sidebar-input',
		'data-sidebar': 'input',
	},
})
export class HlmSidebarInput extends HlmInput {
	constructor() {
		super();
		classes(() => [
			inputVariants({ error: this._state().error }),
			'bg-background focus-visible:ring-sidebar-ring h-8 w-full shadow-none focus-visible:ring-2',
		]);
	}
}
