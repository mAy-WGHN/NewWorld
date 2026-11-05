import { NgTemplateOutlet } from '@angular/common';
import { Component, computed, input } from '@angular/core';
import { parseMarkdown } from './faq-markdown';

/** Shows the Markdown answer of an FAQ entry with normal bindings only (no innerHTML), see `faq-markdown.ts`. */
@Component({
  selector: 'app-faq-answer',
  imports: [NgTemplateOutlet],
  templateUrl: './faq-answer.html',
})
export class FaqAnswer {
  readonly markdown = input.required<string>();
  protected readonly blocks = computed(() => parseMarkdown(this.markdown()));
}
