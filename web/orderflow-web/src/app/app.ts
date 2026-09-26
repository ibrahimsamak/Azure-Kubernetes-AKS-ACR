import { Component, DestroyRef, OnInit, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { filter } from 'rxjs';
import { MsalBroadcastService, MsalService } from '@azure/msal-angular';
import { InteractionStatus } from '@azure/msal-browser';
import { OrderPage } from './order-page';
import { environment } from '../environments/environment';

@Component({
  selector: 'app-root',
  imports: [OrderPage],
  template: `
    <header class="bar">
      @if (user(); as u) {
        <span>{{ u }}</span>
        <button (click)="logout()">Sign out</button>
      } @else {
        <button (click)="login()">Sign in</button>
      }
    </header>
    @if (user()) {
      <app-order-page />
    } @else {
      <p class="hint">Sign in with your organisation account to place an order.</p>
    }
  `,
  styles: [`
    .bar { display: flex; gap: 1rem; justify-content: flex-end; align-items: center; padding: 1rem; font-family: system-ui, sans-serif; }
    .hint { max-width: 36rem; margin: 2rem auto; font-family: system-ui, sans-serif; }
  `],
})
export class App implements OnInit {
  private readonly msal = inject(MsalService);
  private readonly broadcast = inject(MsalBroadcastService);
  private readonly destroyRef = inject(DestroyRef);

  readonly user = signal<string | null>(null);

  ngOnInit(): void {
    // Completes the redirect round-trip: reads the auth response from the URL and caches the tokens.
    // Without this, MSAL throws "interaction_in_progress" on the next login attempt.
    this.msal.handleRedirectObservable().subscribe();

    this.broadcast.inProgress$
      .pipe(filter(status => status === InteractionStatus.None), takeUntilDestroyed(this.destroyRef))
      .subscribe(() => {
        const instance = this.msal.instance;
        if (!instance.getActiveAccount() && instance.getAllAccounts().length > 0) {
          instance.setActiveAccount(instance.getAllAccounts()[0]);
        }
        this.user.set(instance.getActiveAccount()?.username ?? null);
      });
  }

  login(): void {
    this.msal.loginRedirect({ scopes: [environment.auth.apiScope] }).subscribe();
  }

  logout(): void {
    this.msal.logoutRedirect().subscribe();
  }
}
