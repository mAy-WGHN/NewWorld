import { Component, input } from '@angular/core';

/** Title of a page with room for buttons at the top right (they go between the tags). */
@Component({
  selector: 'app-page-head',
  template: `
    <header class="head mb-5 flex flex-wrap items-center justify-between gap-4">
      <h1 class="m-0 text-2xl font-medium">{{ title() }}</h1>
      <div class="flex flex-wrap items-center gap-2">
        <ng-content />
      </div>
    </header>
  `,
})
export class PageHead {
  readonly title = input.required<string>();
}
