import { Directive } from '@angular/core';
import { classes } from '@/shared/components/spartan/utils';

@Directive({
	selector: '[hlmCommandGroupLabel],hlm-command-group-label',
	host: {
		'data-slot': 'command-group-label',
		role: 'presentation',
	},
})
export class HlmCommandGroupLabel {
	constructor() {
		classes(() => 'inline-block');
	}
}
