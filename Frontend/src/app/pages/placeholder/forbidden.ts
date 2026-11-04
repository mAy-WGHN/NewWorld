import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';

@Component({
  selector: 'app-forbidden',
  imports: [RouterLink],
  template: `
    <h1>Kein Zugriff</h1>
    <p class="muted">Für diesen Bereich fehlt dir das Recht. Wende dich bei Bedarf an die Verwaltung.</p>
    <a routerLink="/">Zur Startseite</a>
  `,
  styles: `
    h1 { margin: 0 0 8px; font-size: 26px; }
    .muted { color: var(--muted); }
  `,
})
export class ForbiddenPage {}
