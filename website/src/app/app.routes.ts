import { Routes } from '@angular/router';
import { HomeComponent } from './pages/home/home.component';
import { ProjectsComponent } from './pages/projects/projects.component';
import { ProjectPageComponent } from './pages/projects/project-page.component';
import { CheatsComponent } from './pages/cheats/cheats.component';
import { CheatGameComponent } from './pages/cheats/cheat-game.component';
import { ResearchComponent } from './pages/research/research.component';
import { LoginComponent } from './pages/login/login.component';
import { RegisterComponent } from './pages/register/register.component';
import { ServersComponent } from './pages/servers/servers.component';

export const routes: Routes = [
  { path: '', component: HomeComponent, pathMatch: 'full' },
  { path: 'projects', component: ProjectsComponent },
  { path: 'projects/:slug', component: ProjectPageComponent },
  { path: 'cheats', component: CheatsComponent },
  { path: 'cheats/game', component: CheatGameComponent },
  { path: 'research', component: ResearchComponent },
  { path: 'login', component: LoginComponent },
  { path: 'register', component: RegisterComponent },
  { path: 'servers', component: ServersComponent },
];
