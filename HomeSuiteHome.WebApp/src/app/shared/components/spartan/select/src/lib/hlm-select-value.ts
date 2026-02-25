import { Directive } from '@angular/core';
import { classes } from '@/shared/components/spartan/utils';

@Directive({
	selector: 'hlm-select-value,[hlmSelectValue], brn-select-value[hlm]',
})
export class HlmSelectValue {
	constructor() {
		classes(() => 'data-[placeholder]:text-muted-foreground line-clamp-1 flex items-center gap-2 truncate');
	}
}
