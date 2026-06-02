import { bootstrapApplication } from '@angular/platform-browser';
import { provideExperimentalZonelessChangeDetection } from '@angular/core';
import { AgentCtrlPanelPageComponent } from './agent-ctrl-panel-page.component';

bootstrapApplication(AgentCtrlPanelPageComponent, {
  providers: [provideExperimentalZonelessChangeDetection()],
}).catch(err => console.error('Bootstrap error:', err));
