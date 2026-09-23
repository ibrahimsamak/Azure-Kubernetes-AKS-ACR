import { Component } from '@angular/core';
import { OrderPage } from './order-page';

@Component({
  selector: 'app-root',
  imports: [OrderPage],
  template: `<app-order-page />`,
})
export class App {}
