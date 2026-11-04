import { Component } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { RouterLink } from '@angular/router';

@Component({
  selector: 'app-forbidden',
  imports: [MatButtonModule, RouterLink],
  template: `
    <h1 class="m-0 mb-2 text-2xl font-medium">Kein Zugriff</h1>
    <p class="m-0 mb-4 text-on-surface-variant">Für diesen Bereich fehlt dir das Recht. Wende dich bei Bedarf an die Verwaltung.</p>
    <a mat-stroked-button routerLink="/">Zur Startseite</a>
  `,
})
export class ForbiddenPage {}
