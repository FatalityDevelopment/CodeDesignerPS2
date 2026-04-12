import { Component } from '@angular/core';
import { LayoutComponent } from './layout/layout.component';

@Component({
  selector: 'app-root',
  imports: [LayoutComponent],
  template: `<app-layout />`,
  styles: [`:host { display: block; height: 100%; }`]
})
export class AppComponent {}
