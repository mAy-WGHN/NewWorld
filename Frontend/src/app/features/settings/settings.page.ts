import { Component, computed, inject } from '@angular/core';
import { MatTabsModule } from '@angular/material/tabs';
import { AuthService } from '../../core/auth/auth.service';
import { ProfileService } from '../../core/profile.service';
import { ThemeMode, ThemeService } from '../../core/theme.service';
import { PAGE_UI } from '../../shared/ui/material';
import { NotificationPreferences } from '../notifications/notification-preferences';
import { groupPrivileges } from './settings-models';

/**
 * "Einstellungen" in tabs: appearance (light/dark, kept in this browser), which notifications to receive, the sign-in (who, and where the PIN
 * is managed) and a read-only view of the own roles and privileges.
 */
@Component({
  selector: 'app-settings',
  imports: [MatTabsModule, NotificationPreferences, ...PAGE_UI],
  templateUrl: './settings.page.html',
})
export class SettingsPage {
  private readonly auth = inject(AuthService);
  protected readonly profile = inject(ProfileService);
  protected readonly theme = inject(ThemeService);

  protected readonly modes: readonly { value: ThemeMode; label: string; icon: string }[] = [
    { value: 'system', label: 'Wie das Gerät', icon: 'brightness_auto' },
    { value: 'light', label: 'Hell', icon: 'light_mode' },
    { value: 'dark', label: 'Dunkel', icon: 'dark_mode' },
  ];

  protected readonly canReadChoices = computed(() => this.auth.privileges().has('preference:read-own'));
  protected readonly roles = this.auth.roles;
  protected readonly groups = computed(() => groupPrivileges(this.auth.privileges().grants));
}
