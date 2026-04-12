import { Routes } from '@angular/router';
import { ProjectsComponent } from './pages/projects/projects.component';
import { CheatsComponent } from './pages/cheats/cheats.component';
import { ResearchComponent } from './pages/research/research.component';
import { LoginComponent } from './pages/login/login.component';
import { RegisterComponent } from './pages/register/register.component';
import { ServersComponent } from './pages/servers/servers.component';

export const routes: Routes = [
  { path: '', redirectTo: 'projects', pathMatch: 'full' },
  { path: 'projects', component: ProjectsComponent },
  { path: 'cheats', component: CheatsComponent },
  { path: 'research', component: ResearchComponent },
  { path: 'login', component: LoginComponent },
  { path: 'register', component: RegisterComponent },
  { path: 'servers', component: ServersComponent },
];
