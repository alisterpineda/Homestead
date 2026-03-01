import { Directive } from '@angular/core';
import { HlmInput } from '@/shared/components/spartan/input';
import { classes } from '@/shared/components/spartan/utils';

@Directive({
	selector: 'input[hlmInputGroupInput]',
	hostDirectives: [HlmInput],
	host: {
		'data-slot': 'input-group-control',
	},
})
export class HlmInputGroupInput {
	constructor() {
		classes(() => `flex-1 rounded-none border-0 bg-transparent shadow-none focus-visible:ring-0 dark:bg-transparent`);
	}
}
