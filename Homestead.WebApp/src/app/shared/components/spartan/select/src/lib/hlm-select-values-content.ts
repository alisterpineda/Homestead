import { Directive } from '@angular/core';
import { classes } from '@/shared/components/spartan/utils';

@Directive({ selector: '[hlmSelectValuesContent],hlm-select-values-content' })
export class HlmSelectValuesContent {
	constructor() {
		classes(() => 'flex gap-1');
	}
}
