import { Directive } from '@angular/core';
import { classes } from '@/shared/components/spartan/utils';

@Directive({
	selector: '[hlmDropdownMenuShortcut],hlm-dropdown-menu-shortcut',
	host: {
		'data-slot': 'dropdown-menu-shortcut',
	},
})
export class HlmDropdownMenuShortcut {
	constructor() {
		classes(() => 'text-muted-foreground ml-auto text-xs tracking-widest');
	}
}
