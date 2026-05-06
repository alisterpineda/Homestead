import { Directive } from '@angular/core';
import { BrnComboboxValue } from '@spartan-ng/brain/combobox';
import { classes } from '@/shared/components/spartan/utils';

@Directive({
	selector: '[hlmComboboxValue],hlm-combobox-value',
	hostDirectives: [{ directive: BrnComboboxValue, inputs: ['placeholder'] }],
})
export class HlmComboboxValue {
	constructor() {
		classes(() => 'data-hidden:hidden');
	}
}
