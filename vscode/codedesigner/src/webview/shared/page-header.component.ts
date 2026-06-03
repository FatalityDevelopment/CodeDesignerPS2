import {
  ChangeDetectionStrategy,
  Component,
  ViewEncapsulation,
  input,
  output,
} from '@angular/core';
import { IconComponent } from './icon.component';

/**
 * Shared full-width header for standalone CodeDesigner webviews.
 * The disassembler editor intentionally keeps its own editor chrome.
 */
@Component({
  selector: 'app-page-header',
  standalone: true,
  imports: [IconComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  encapsulation: ViewEncapsulation.None,
  styles: [`
app-page-header {
  display: block;
  flex: 0 0 auto;
  min-width: 0;
  box-sizing: border-box;
}
.cd-page-header,
.cd-page-header * {
  box-sizing: border-box;
}
.cd-page-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 16px;
  flex: 0 0 70px;
  min-height: 70px;
  width: 100%;
  padding: 0 22px;
  border-bottom: 1px solid var(--vscode-panel-border, #2d2d30);
  background: #181818;
  color: var(--vscode-foreground, #d4d4d4);
}
.cd-page-brand {
  display: flex;
  align-items: center;
  min-width: 0;
  gap: 10px;
}
.cd-page-mark {
  width: 36px;
  height: 36px;
  display: grid;
  place-items: center;
  flex: 0 0 36px;
  border: 1px solid #0e639c;
  border-radius: 7px;
  background: linear-gradient(135deg, #0e639c, #174b70);
  color: #fff;
}
.cd-page-header-ai .cd-page-mark {
  border-color: #7a5500;
  background: linear-gradient(135deg, #4b3400, #251a00);
  color: #e8a030;
}
.cd-page-copy {
  min-width: 0;
}
.cd-page-title {
  margin: 0;
  overflow: hidden;
  color: var(--vscode-foreground, #f2f2f2);
  font-size: 15px;
  font-weight: 700;
  line-height: 1.25;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.cd-page-subtitle {
  margin: 3px 0 0;
  overflow: hidden;
  color: var(--vscode-descriptionForeground, #929292);
  font-size: 11px;
  line-height: 1.25;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.cd-page-actions {
  display: flex;
  align-items: center;
  justify-content: flex-end;
  gap: 8px;
  flex: 0 0 auto;
  min-width: 0;
}
.cd-mcp-status {
  display: inline-flex;
  align-items: center;
  gap: 7px;
  min-height: 35px;
  padding: 7px 11px;
  border: 1px solid var(--vscode-panel-border, #313131);
  border-radius: 6px;
  background: #242424;
  color: var(--vscode-descriptionForeground, #8f8f8f);
  font-size: 11px;
  font-weight: 700;
  letter-spacing: 0.06em;
  text-transform: uppercase;
}
.cd-mcp-status.cd-mcp-online {
  border-color: #7a5500;
  background: linear-gradient(90deg, #3a2800, #402f08);
  color: #e8a030;
}
.cd-mcp-ico { opacity: .55; }
.cd-mcp-online .cd-mcp-ico { opacity: 1; }
.cd-mcp-state {
  display: inline-flex;
  align-items: center;
  gap: 6px;
}
.cd-mcp-dot {
  width: 7px;
  height: 7px;
  border-radius: 50%;
  background: currentColor;
}
.cd-mcp-online .cd-mcp-dot {
  box-shadow: 0 0 6px 1px rgba(232, 160, 48, .9);
}
.cd-mcp-run-btn {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  gap: 4px;
  height: 22px;
  margin-left: 3px;
  padding: 0 7px;
  border: 1px solid var(--vscode-panel-border, #313131);
  border-radius: 4px;
  background: var(--vscode-button-secondaryBackground, #2f2f30);
  color: var(--vscode-button-secondaryForeground, var(--vscode-foreground, #d4d4d4));
  cursor: pointer;
  font: inherit;
  font-size: 11px;
  font-weight: 700;
  letter-spacing: 0;
  text-transform: none;
}
.cd-mcp-run-btn:hover {
  background: var(--vscode-button-secondaryHoverBackground, #3a3a3b);
  color: var(--vscode-foreground, #d4d4d4);
}
@media (max-width: 680px) {
  .cd-page-header { padding: 0 14px; }
  .cd-page-subtitle { display: none; }
  .cd-mcp-label { display: none; }
}
  `],
  template: `
<header class="cd-page-header" [class.cd-page-header-ai]="accent() === 'ai'">
  <div class="cd-page-brand">
    <div class="cd-page-mark"><app-icon [name]="icon()" [size]="22" /></div>
    <div class="cd-page-copy">
      <h1 class="cd-page-title">{{ title() }}</h1>
      <p class="cd-page-subtitle">{{ subtitle() }}</p>
    </div>
  </div>
  <div class="cd-page-actions">
    <ng-content select="[page-header-actions]"></ng-content>
    @if (showMcp()) {
      <div class="cd-mcp-status" [class.cd-mcp-online]="mcpOnline()" [title]="'CodeDesigner.AI server' + (mcpOnline() ? ' is running' : ' is not running')">
        <app-icon class="cd-mcp-ico" name="bot" [size]="14" />
        <span class="cd-mcp-label">MCP</span>
        <span class="cd-mcp-state"><span class="cd-mcp-dot"></span>{{ mcpOnline() ? 'Online' : 'Offline' }}</span>
        @if (!mcpOnline()) {
          <button class="cd-mcp-run-btn" type="button" (click)="runMcp.emit()" title="Run CodeDesigner.AI MCP server">
            <app-icon name="play" [size]="12" />
            <span>Run</span>
          </button>
        }
      </div>
    }
  </div>
</header>
`,
})
export class PageHeaderComponent {
  readonly icon = input.required<string>();
  readonly title = input.required<string>();
  readonly subtitle = input('');
  readonly accent = input<'blue' | 'ai'>('blue');
  readonly showMcp = input(false);
  readonly mcpOnline = input(false);
  readonly runMcp = output<void>();
}
