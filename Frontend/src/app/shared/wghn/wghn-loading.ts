import { Component } from '@angular/core';

/** "Wird geladen …" while a page or a part of it waits for its data. Another text can go between the tags. */
@Component({
  selector: 'wghn-loading',
  host: { class: 'block' },
  template: `<p class="m-0 text-sm text-on-surface-variant" role="status"><ng-content>Wird geladen …</ng-content></p>`,
})
export class WghnLoading {}
