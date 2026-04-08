import { bootstrapApplication } from '@angular/platform-browser';
import { provideExperimentalZonelessChangeDetection } from '@angular/core';
import { AiConfigPageComponent } from './ai-config-page.component';

bootstrapApplication(AiConfigPageComponent, {
  providers: [provideExperimentalZonelessChangeDetection()],
}).catch(err => console.error('Bootstrap error:', err));
