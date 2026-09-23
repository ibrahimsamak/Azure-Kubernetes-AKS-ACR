import { Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { HttpClient, HttpErrorResponse, HttpHeaders } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import { EMPTY, Observable, Subscription, throwError, timer } from 'rxjs';
import { distinctUntilChanged, expand, map, retry, startWith, switchMap } from 'rxjs/operators';
import { environment } from '../environments/environment';

interface PlaceOrderResponse { orderId: string; status: string; }
// Mirrors OrderSagaStatus on the Order API: the saga's state, not the order's.
interface SagaStatus { orderId: string; state: string; failureReason?: string | null; }
interface StatusChange { state: string; failureReason?: string | null; at: Date; }

const TERMINAL = new Set(['Confirmed', 'Cancelled']);

// APIM allows 30 calls / 60 s per subscription, so a once-a-second poll spends the whole budget
// on one order. Back off instead: 1s, 2s, 4s, then 5s — a saga that settles in 10s costs ~4 calls.
const MAX_ATTEMPTS = 40;
const MAX_RATE_LIMIT_RETRIES = 5;
const backoffMs = (attempt: number) => Math.min(1000 * 2 ** attempt, 5000);

@Component({
  selector: 'app-order-page',
  imports: [FormsModule],
  template: `
    <main class="page">
      <h1>OrderFlow</h1>

      <form (ngSubmit)="placeOrder()" class="card">
        <label>SKU <input name="sku" [(ngModel)]="sku" required /></label>
        <label>Quantity <input name="quantity" type="number" min="1" [(ngModel)]="quantity" required /></label>
        <label>Unit price <input name="unitPrice" type="number" step="0.01" [(ngModel)]="unitPrice" required /></label>
        <p class="hint">Use 13.13 to force a payment decline and watch compensation.</p>
        <button type="submit" [disabled]="busy()">{{ busy() ? 'Working…' : 'Place order' }}</button>
      </form>

      @if (error()) {
        <p class="error">{{ error() }}</p>
      }

      @if (status(); as s) {
        <section class="card">
          <div>Order <code>{{ s.orderId }}</code></div>
          <div class="status" [class.ok]="s.state === 'Confirmed'" [class.bad]="s.state === 'Cancelled'">
            {{ s.state }}
          </div>
          @if (s.failureReason) {
            <div>Reason: {{ s.failureReason }}</div>
          }
          <ol class="history">
            @for (h of history(); track $index) {
              <li>
                <span [class.ok]="h.state === 'Confirmed'" [class.bad]="h.state === 'Cancelled' || h.state === 'Compensating'">{{ h.state }}</span>
                <time>{{ h.at.toLocaleTimeString() }}</time>
                @if (h.failureReason) { <small>— {{ h.failureReason }}</small> }
              </li>
            }
          </ol>
        </section>
      }
    </main>
  `,
  styles: [`
    .page { max-width: 36rem; margin: 2rem auto; padding-inline: 1rem; font-family: system-ui, sans-serif; }
    .card { display: grid; gap: .75rem; padding: 1rem; border: 1px solid #ccc; border-radius: .5rem; margin-block: 1rem; }
    label { display: grid; gap: .25rem; }
    .hint { color: #666; font-size: .85rem; margin: 0; }
    .status { font-size: 1.5rem; font-weight: 600; }
    .history { margin: 0; padding-inline-start: 1.25rem; display: grid; gap: .25rem; }
    .history time { color: #666; font-size: .85rem; margin-inline-start: .5rem; }
    .ok { color: #1a7f37; } .bad { color: #cf222e; } .error { color: #cf222e; }
  `],
})
export class OrderPage {
  private readonly http = inject(HttpClient);
  private readonly destroyRef = inject(DestroyRef);
  private readonly baseHeaders = new HttpHeaders({ 'Ocp-Apim-Subscription-Key': environment.apimSubscriptionKey });

  // The live saga subscription for the current order; replaced on each new submit.
  private sagaSubscription?: Subscription;

  sku = 'SKU-1';
  quantity = 1;
  unitPrice = 29.99;

  readonly busy = signal(false);
  readonly error = signal<string | null>(null);
  readonly status = signal<SagaStatus | null>(null);
  readonly history = signal<StatusChange[]>([]);

  placeOrder(): void {
    this.sagaSubscription?.unsubscribe();
    this.busy.set(true);
    this.error.set(null);
    this.status.set(null);
    this.history.set([]);

    const body = {
      customerId: 'CUST-1',
      currency: 'CAD',
      street: '1 King St W', city: 'Toronto', postalCode: 'M5H 1A1', country: 'CA',
      lines: [{ sku: this.sku, quantity: this.quantity, unitPrice: this.unitPrice }],
    };

    // A new Idempotency-Key per user action: a double-click or retry of THIS submit is safe.
    const headers = this.baseHeaders.set('Idempotency-Key', crypto.randomUUID());

    this.sagaSubscription = this.http.post<PlaceOrderResponse>(`${environment.apiBaseUrl}/api/v1/orders`, body, { headers })
      .pipe(
        // The 202 already tells us the saga started in Pending; show it before the first poll.
        switchMap(r => this.sagaChanges(r.orderId).pipe(
          startWith<SagaStatus>({ orderId: r.orderId, state: r.status }))),
        // Emit only real transitions, so the history is one line per saga state.
        distinctUntilChanged((a, b) => a.state === b.state && a.failureReason === b.failureReason),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: s => {
          this.status.set(s);
          this.history.update(h => [...h, { state: s.state, failureReason: s.failureReason, at: new Date() }]);
        },
        error: (e: HttpErrorResponse) => this.fail(e),
        complete: () => this.busy.set(false),
      });
  }

  /** Every status the saga reports until it reaches a terminal state (or we give up).
   *  Polling can still skip a state that lasts less than one backoff interval; only a server
   *  push (SignalR) guarantees every transition is seen. */
  private sagaChanges(orderId: string): Observable<SagaStatus> {
    const fetch = (attempt: number) =>
      this.http.get<SagaStatus>(`${environment.apiBaseUrl}/api/v1/orders/${orderId}/status`, { headers: this.baseHeaders })
        .pipe(
          // 429 is APIM's rate limit, not a failed order: the saga is still running, we just
          // asked too often. Honour Retry-After and keep watching instead of giving up.
          retry({
            count: MAX_RATE_LIMIT_RETRIES,
            delay: (e: HttpErrorResponse) => e.status === 429 ? timer(retryAfterMs(e)) : throwError(() => e),
          }),
          map(status => ({ status, attempt })),
        );

    return fetch(0).pipe(
      expand(({ status, attempt }) =>
        TERMINAL.has(status.state) || attempt >= MAX_ATTEMPTS
          ? EMPTY
          : timer(backoffMs(attempt)).pipe(switchMap(() => fetch(attempt + 1)))),
      map(({ status }) => status),
    );
  }

  private fail(e: HttpErrorResponse): void {
    this.busy.set(false);
    // status 0 in the browser almost always means CORS: check spa-origin in APIM.
    this.error.set(e.status === 0 ? 'Network/CORS error — check APIM spa-origin.' : `${e.status} ${e.statusText}`);
  }
}

function retryAfterMs(e: HttpErrorResponse): number {
  const retryAfter = Number(e.headers.get('Retry-After'));
  return Number.isFinite(retryAfter) && retryAfter > 0 ? retryAfter * 1000 : 10_000;
}
