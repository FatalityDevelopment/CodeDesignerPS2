import * as vscode from 'vscode';
import * as path from 'path';
import { getOutputChannel } from '../../extension';
import { SocketClient, McpAgentInfo, McpMemoryInfo, McpOrchestrationInfo } from '../../socketClient';
import { getNonce, getWebviewErrorHandlerScript } from '../../utils/nonce';
import { McpMemoryEditorProvider } from '../mcpMemoryEditor/mcpMemoryEditorProvider';

/**
 * Provider for the AI Configuration standalone webview tab.
 * Manages agent profiles (add / edit / delete / import / export)
 * and shows the agent's associated MCP memories.
 */
export class AiConfigProvider {
    private _panel: vscode.WebviewPanel | undefined;
    private readonly _mcpMemoryEditor: McpMemoryEditorProvider;

    constructor(
        private readonly extensionUri: vscode.Uri,
        private readonly socketClient: SocketClient,
    ) {
        this._mcpMemoryEditor = new McpMemoryEditorProvider(socketClient);
    }

    /** Open (or reveal) the AI Configuration panel. */
    public open(): void {
        if (this._panel) {
            this._panel.reveal(vscode.ViewColumn.One);
            return;
        }

        const panel = vscode.window.createWebviewPanel(
            'codedesigner.aiConfig',
            '🤖 AI Configuration',
            vscode.ViewColumn.One,
            {
                enableScripts:           true,
                retainContextWhenHidden: true,
                localResourceRoots:      [this.extensionUri],
            }
        );
        this._panel = panel;

        panel.onDidDispose(() => { this._panel = undefined; });

        panel.webview.onDidReceiveMessage(async (msg: Record<string, unknown>) => {
            await this._handleMessage(msg, panel.webview);
        });

        panel.webview.html = this._buildHtml(panel.webview);
    }

    // ── Message dispatch ──────────────────────────────────────────────────────

    private async _handleMessage(msg: Record<string, unknown>, webview: vscode.Webview): Promise<void> {
        const type = msg['type'] as string | undefined;

        switch (type) {
            case 'ready':
                // Initial load — send agent list and orchestration list
                await this._sendAgentList(webview);
                await this._sendOrchestrationList(webview);
                break;

            // ── Agent CRUD ────────────────────────────────────────────────────
            case 'agentList':
                await this._sendAgentList(webview);
                break;

            case 'agentAdd':
                await this._handleAgentAdd(msg, webview);
                break;

            case 'agentUpdate':
                await this._handleAgentUpdate(msg, webview);
                break;

            case 'agentDelete':
                await this._handleAgentDelete(msg['id'] as number, webview);
                break;

            case 'agentImport':
                await this._handleAgentImport(webview);
                break;

            case 'agentExport': {
                const id   = msg['id']   as number;
                const name = msg['name'] as string;
                await this._handleAgentExport(id, name, webview);
                break;
            }

            // ── Orchestration CRUD ────────────────────────────────────────────
            case 'orchestrationList':
                await this._sendOrchestrationList(webview);
                break;

            case 'orchestrationAdd':
                await this._handleOrchestrationAdd(msg, webview);
                break;

            case 'orchestrationUpdate':
                await this._handleOrchestrationUpdate(msg, webview);
                break;

            case 'orchestrationDelete':
                await this._handleOrchestrationDelete(msg['id'] as number, webview);
                break;

            case 'orchestrationImport':
                await this._handleOrchestrationImport(webview);
                break;

            case 'orchestrationExport': {
                const id   = msg['id']   as number;
                const name = msg['name'] as string;
                await this._handleOrchestrationExport(id, name, webview);
                break;
            }

            case 'orchestrationSetActive': {
                const id = msg['id'] as number;
                await this._handleOrchestrationSetActive(id, webview);
                break;
            }

            case 'exportAll':
                await this._handleExportAll(webview);
                break;

            // ── Memory operations ─────────────────────────────────────────────
            case 'memoryList':
                await this._sendMemoryList(webview);
                break;

            case 'memoryListForAgent':
                await this._sendMemoryListForAgent(msg['agentId'] as number, webview);
                break;

            case 'memoryListAll':
                await this._sendMemoryListAll(webview);
                break;

            case 'memoryAdd':
                await this._handleMemoryAdd(msg['name'] as string, msg['agentId'] as number | undefined, webview);
                break;

            case 'memoryEdit':
                this._mcpMemoryEditor.open(
                    msg['id']      as number,
                    msg['name']    as string,
                    msg['content'] as string,
                );
                break;

            case 'memoryExport': {
                const id   = msg['id']   as number;
                const name = msg['name'] as string;
                await this._handleMemoryExport(id, name, webview);
                break;
            }

            case 'memoryImport':
                await this._handleMemoryImport(webview);
                break;

            case 'memoryDelete':
                await this._handleMemoryDelete(msg['id'] as number, webview);
                break;

            // ── Tool description handlers ─────────────────────────────────────
            case 'toolList':
                await this._sendToolList(webview);
                break;

            case 'toolUpdate':
                await this._handleToolUpdate(msg['key'] as string, msg['value'] as string, webview);
                break;

            case 'historyList':
                await this._sendHistoryList(webview);
                break;

            default:
                getOutputChannel().warn(`[AiConfig] Unknown message type: ${type}`);
                break;
        }
    }

    // ── Agent handlers ────────────────────────────────────────────────────────

    private async _sendAgentList(webview: vscode.Webview): Promise<void> {
        try {
            const agents = await this.socketClient.mcpAgentList();
            webview.postMessage({ type: 'agentListResult', agents });
        } catch (err: any) {
            webview.postMessage({ type: 'agentListResult', agents: [], error: err?.message ?? String(err) });
        }
    }

    private async _handleAgentAdd(msg: Record<string, unknown>, webview: vscode.Webview): Promise<void> {
        try {
            const agent = await this.socketClient.mcpAgentAdd({
                name:                msg['name']                as string,
                description:         msg['description']         as string | undefined,
                isAgentLead:         (msg['isAgentLead']        as boolean) ?? false,
                status:              msg['status']              as string | undefined,
                objective:           msg['objective']           as string | undefined,
                rules:               msg['rules']               as string | undefined,
                guidelines:          msg['guidelines']          as string | undefined,
                recommendedWorkflow: msg['recommendedWorkflow'] as string | undefined,
                sessionBegin:        msg['sessionBegin']        as string | undefined,
                concurrentTasks:     msg['concurrentTasks']     as string | undefined,
                sessionEnd:          msg['sessionEnd']          as string | undefined,
                research:            msg['research']            as string | undefined,
            });
            webview.postMessage({ type: 'agentActionResult', agent, message: `Created '${agent.name}'` });
        } catch (err: any) {
            webview.postMessage({ type: 'agentActionResult', error: err?.message ?? String(err) });
        }
    }

    private async _handleAgentUpdate(msg: Record<string, unknown>, webview: vscode.Webview): Promise<void> {
        try {
            const id    = msg['id'] as number;
            const agent = await this.socketClient.mcpAgentUpdate(id, {
                name:                msg['name']                as string | undefined,
                description:         msg['description']         as string | undefined,
                isAgentLead:         msg['isAgentLead']         as boolean | undefined,
                status:              msg['status']              as string | undefined,
                objective:           msg['objective']           as string | undefined,
                rules:               msg['rules']               as string | undefined,
                guidelines:          msg['guidelines']          as string | undefined,
                recommendedWorkflow: msg['recommendedWorkflow'] as string | undefined,
                sessionBegin:        msg['sessionBegin']        as string | undefined,
                concurrentTasks:     msg['concurrentTasks']     as string | undefined,
                sessionEnd:          msg['sessionEnd']          as string | undefined,
                research:            msg['research']            as string | undefined,
            });
            webview.postMessage({ type: 'agentActionResult', agent, message: `Saved '${agent.name}'` });
        } catch (err: any) {
            webview.postMessage({ type: 'agentActionResult', error: err?.message ?? String(err) });
        }
    }

    private async _handleAgentDelete(id: number, webview: vscode.Webview): Promise<void> {
        try {
            await this.socketClient.mcpAgentDelete(id);
            webview.postMessage({ type: 'agentDeleteResult', message: 'Deleted.' });
        } catch (err: any) {
            webview.postMessage({ type: 'agentDeleteResult', error: err?.message ?? String(err) });
        }
    }

    private async _handleAgentImport(webview: vscode.Webview): Promise<void> {
        const uris = await vscode.window.showOpenDialog({
            canSelectFiles:     true,
            canSelectFolders:   false,
            canSelectMany:      false,
            filters:            { 'Markdown': ['md'] },
            title:              'Import Agent Profile',
        });
        if (!uris || uris.length === 0) { return; }
        try {
            const agent = await this.socketClient.mcpAgentImport(uris[0].fsPath);
            webview.postMessage({ type: 'agentActionResult', agent, message: `Imported '${agent.name}'` });
        } catch (err: any) {
            webview.postMessage({ type: 'agentActionResult', error: err?.message ?? String(err) });
        }
    }

    private async _handleAgentExport(id: number, name: string, webview: vscode.Webview): Promise<void> {
        const safeName = name.replace(/[<>:"/\\|?*]/g, '_');
        const uri = await vscode.window.showSaveDialog({
            defaultUri: vscode.Uri.file(`${safeName}.md`),
            filters:    { 'Markdown': ['md'] },
            title:      `Export Agent Profile: ${name}`,
        });
        if (!uri) { return; }
        try {
            await this.socketClient.mcpAgentExport(id, uri.fsPath);
            webview.postMessage({ type: 'agentActionResult', message: `Exported to '${path.basename(uri.fsPath)}'` });
            vscode.window.showInformationMessage(`Agent profile '${name}' exported.`);
        } catch (err: any) {
            webview.postMessage({ type: 'agentActionResult', error: err?.message ?? String(err) });
        }
    }

    // ── Memory handlers ───────────────────────────────────────────────────────

    private async _sendMemoryList(webview: vscode.Webview): Promise<void> {
        try {
            const entries = await this.socketClient.mcpMemoryList();
            webview.postMessage({ type: 'memoryListResult', entries });
        } catch (err: any) {
            webview.postMessage({ type: 'memoryListResult', entries: [], error: err?.message ?? String(err) });
        }
    }

    private async _sendMemoryListForAgent(agentId: number, webview: vscode.Webview): Promise<void> {
        try {
            const entries = await this.socketClient.mcpMemoryListForAgent(agentId);
            webview.postMessage({ type: 'memoryListResult', entries });
        } catch (err: any) {
            webview.postMessage({ type: 'memoryListResult', entries: [], error: err?.message ?? String(err) });
        }
    }

    private async _sendMemoryListAll(webview: vscode.Webview): Promise<void> {
        try {
            const entries = await this.socketClient.mcpMemoryList();
            webview.postMessage({ type: 'memoryListAllResult', entries });
        } catch (err: any) {
            webview.postMessage({ type: 'memoryListAllResult', entries: [], error: err?.message ?? String(err) });
        }
    }

    private async _handleMemoryAdd(name: string, agentId: number | undefined, webview: vscode.Webview): Promise<void> {
        try {
            const entry = await this.socketClient.mcpMemoryAdd(name, '', undefined, agentId);
            webview.postMessage({ type: 'memoryActionResult', entry, message: `Created '${entry.name}'` });
        } catch (err: any) {
            webview.postMessage({ type: 'memoryActionResult', error: err?.message ?? String(err) });
        }
    }

    private async _handleMemoryExport(id: number, name: string, webview: vscode.Webview): Promise<void> {
        const safeName = name.replace(/[<>:"/\\|?*]/g, '_');
        const uri = await vscode.window.showSaveDialog({
            defaultUri: vscode.Uri.file(`${safeName}.md`),
            filters:    { 'Markdown': ['md'] },
            title:      `Export Memory: ${name}`,
        });
        if (!uri) { return; }
        try {
            await this.socketClient.mcpMemoryExport(id, uri.fsPath);
            webview.postMessage({ type: 'memoryActionResult', message: `Exported '${name}'` });
        } catch (err: any) {
            webview.postMessage({ type: 'memoryActionResult', error: err?.message ?? String(err) });
        }
    }

    private async _handleMemoryImport(webview: vscode.Webview): Promise<void> {
        const uris = await vscode.window.showOpenDialog({
            canSelectFiles:   true,
            canSelectFolders: false,
            canSelectMany:    false,
            filters:          { 'Markdown': ['md'] },
            title:            'Import Memory',
        });
        if (!uris || uris.length === 0) { return; }
        try {
            const entry = await this.socketClient.mcpMemoryImport(uris[0].fsPath);
            webview.postMessage({ type: 'memoryActionResult', entry, message: `Imported '${entry.name}'` });
        } catch (err: any) {
            webview.postMessage({ type: 'memoryActionResult', error: err?.message ?? String(err) });
        }
    }

    private async _handleMemoryDelete(id: number, webview: vscode.Webview): Promise<void> {
        try {
            await this.socketClient.mcpMemoryDelete(id);
            webview.postMessage({ type: 'memoryActionResult', message: 'Deleted.' });
        } catch (err: any) {
            webview.postMessage({ type: 'memoryActionResult', error: err?.message ?? String(err) });
        }
    }

    // ── Orchestration handlers ────────────────────────────────────────────────

    private async _sendOrchestrationList(webview: vscode.Webview): Promise<void> {
        try {
            const profiles = await this.socketClient.mcpOrchestrationList();
            webview.postMessage({ type: 'orchestrationListResult', profiles });
        } catch (err: any) {
            webview.postMessage({ type: 'orchestrationListResult', profiles: [], error: err?.message ?? String(err) });
        }
    }

    private async _handleOrchestrationAdd(msg: Record<string, unknown>, webview: vscode.Webview): Promise<void> {
        try {
            const profile = await this.socketClient.mcpOrchestrationAdd({
                name:        msg['name']        as string,
                description: msg['description'] as string | undefined,
                strategy:    msg['strategy']    as string | undefined,
                roles:       msg['roles']       as string | undefined,
                workflow:    msg['workflow']     as string | undefined,
                rules:       msg['rules']       as string | undefined,
                notes:       msg['notes']       as string | undefined,
            });
            webview.postMessage({ type: 'orchestrationActionResult', profile, message: `Created '${profile.name}'` });
        } catch (err: any) {
            webview.postMessage({ type: 'orchestrationActionResult', error: err?.message ?? String(err) });
        }
    }

    private async _handleOrchestrationUpdate(msg: Record<string, unknown>, webview: vscode.Webview): Promise<void> {
        try {
            const id      = msg['id'] as number;
            const profile = await this.socketClient.mcpOrchestrationUpdate(id, {
                name:        msg['name']        as string | undefined,
                description: msg['description'] as string | undefined,
                strategy:    msg['strategy']    as string | undefined,
                roles:       msg['roles']       as string | undefined,
                workflow:    msg['workflow']     as string | undefined,
                rules:       msg['rules']       as string | undefined,
                notes:       msg['notes']       as string | undefined,
            });
            webview.postMessage({ type: 'orchestrationActionResult', profile, message: `Saved '${profile.name}'` });
        } catch (err: any) {
            webview.postMessage({ type: 'orchestrationActionResult', error: err?.message ?? String(err) });
        }
    }

    private async _handleOrchestrationDelete(id: number, webview: vscode.Webview): Promise<void> {
        try {
            await this.socketClient.mcpOrchestrationDelete(id);
            webview.postMessage({ type: 'orchestrationDeleteResult', message: 'Deleted.' });
        } catch (err: any) {
            webview.postMessage({ type: 'orchestrationDeleteResult', error: err?.message ?? String(err) });
        }
    }

    private async _handleOrchestrationImport(webview: vscode.Webview): Promise<void> {
        const uris = await vscode.window.showOpenDialog({
            canSelectFiles:     true,
            canSelectFolders:   false,
            canSelectMany:      false,
            filters:            { 'Markdown': ['md'] },
            title:              'Import Orchestration Profile',
        });
        if (!uris || uris.length === 0) { return; }
        try {
            const profile = await this.socketClient.mcpOrchestrationImport(uris[0].fsPath);
            webview.postMessage({ type: 'orchestrationActionResult', profile, message: `Imported '${profile.name}'` });
        } catch (err: any) {
            webview.postMessage({ type: 'orchestrationActionResult', error: err?.message ?? String(err) });
        }
    }

    private async _handleOrchestrationExport(id: number, name: string, webview: vscode.Webview): Promise<void> {
        const safeName = name.replace(/[<>:"/\\|?*]/g, '_');
        const uri = await vscode.window.showSaveDialog({
            defaultUri: vscode.Uri.file(`${safeName}.md`),
            filters:    { 'Markdown': ['md'] },
            title:      `Export Orchestration Profile: ${name}`,
        });
        if (!uri) { return; }
        try {
            await this.socketClient.mcpOrchestrationExport(id, uri.fsPath);
            webview.postMessage({ type: 'orchestrationActionResult', message: `Exported to '${path.basename(uri.fsPath)}'` });
            vscode.window.showInformationMessage(`Orchestration profile '${name}' exported.`);
        } catch (err: any) {
            webview.postMessage({ type: 'orchestrationActionResult', error: err?.message ?? String(err) });
        }
    }

    private async _handleOrchestrationSetActive(id: number, webview: vscode.Webview): Promise<void> {
        try {
            const profile = await this.socketClient.mcpOrchestrationSetActive(id);
            webview.postMessage({ type: 'orchestrationActionResult', profile, message: `'${profile.name}' is now the active plan.` });
        } catch (err: any) {
            webview.postMessage({ type: 'orchestrationActionResult', error: err?.message ?? String(err) });
        }
    }

    private async _handleExportAll(webview: vscode.Webview): Promise<void> {
        const uri = await vscode.window.showSaveDialog({
            defaultUri: vscode.Uri.file('orchestration-plan.md'),
            filters:    { 'Markdown': ['md'] },
            title:      'Export All — Full Orchestration Plan (as the AI sees it)',
        });
        if (!uri) { return; }
        try {
            const [orchestrations, agents] = await Promise.all([
                this.socketClient.mcpOrchestrationList(),
                this.socketClient.mcpAgentList(),
            ]);

            const lines: string[] = [];

            const appendField = (label: string, value: string | undefined) => {
                if (value && value.trim()) {
                    lines.push(`### ${label}`);
                    lines.push(value);
                    lines.push('');
                }
            };

            if (orchestrations.length > 0) {
                lines.push('# Orchestration Plans');
                lines.push('');
                for (const orch of orchestrations) {
                    const activePrefix = orch.isActive ? '● ' : '';
                    const activeSuffix = orch.isActive ? '  [ACTIVE]' : '';
                    lines.push('---');
                    lines.push('');
                    lines.push(`# ${activePrefix}${orch.name}${activeSuffix}`);
                    lines.push('');
                    appendField('Description', orch.description);
                    appendField('Strategy',    orch.strategy);
                    appendField('Roles',       orch.roles);
                    appendField('Workflow',    orch.workflow);
                    appendField('Rules',       orch.rules);
                    appendField('Notes',       orch.notes);
                }
            }

            if (agents.length > 0) {
                if (lines.length > 0) { lines.push(''); lines.push('---'); lines.push(''); }
                lines.push('# Agent Profiles');
                lines.push('');
                for (const agent of agents) {
                    lines.push('---');
                    lines.push('');
                    const lead   = agent.isAgentLead ? '★ ' : '';
                    const status = agent.status ? ` [${agent.status}]` : '';
                    lines.push(`# ${lead}${agent.name}${status}`);
                    lines.push('');
                    appendField('Description',         agent.description);
                    appendField('Objective',            agent.objective);
                    appendField('Rules',                agent.rules);
                    appendField('Guidelines',           agent.guidelines);
                    appendField('Session Begin',        agent.sessionBegin);
                    appendField('Concurrent Tasks',     agent.concurrentTasks);
                    appendField('Session End',          agent.sessionEnd);
                    appendField('Recommended Workflow', agent.recommendedWorkflow);
                    appendField('Research',             agent.research);
                }
            }

            const content = lines.join('\n');
            await vscode.workspace.fs.writeFile(uri, Buffer.from(content, 'utf8'));
            webview.postMessage({ type: 'exportAllResult', message: `Exported to '${path.basename(uri.fsPath)}'` });
            vscode.window.showInformationMessage('Full orchestration plan exported.');
        } catch (err: any) {
            webview.postMessage({ type: 'exportAllResult', error: err?.message ?? String(err) });
        }
    }

    // ── Tool description handlers ─────────────────────────────────────────────

    private async _sendToolList(webview: vscode.Webview): Promise<void> {
        try {
            const tools = await this.socketClient.mcpConfigList();
            webview.postMessage({ type: 'toolListResult', tools });
        } catch (err: any) {
            webview.postMessage({ type: 'toolListResult', tools: [], error: err?.message ?? String(err) });
        }
    }

    private async _handleToolUpdate(key: string, value: string, webview: vscode.Webview): Promise<void> {
        try {
            await this.socketClient.mcpConfigUpdate(key, value);
            webview.postMessage({ type: 'toolUpdateResult', message: `Updated description for '${key}'.` });
        } catch (err: any) {
            webview.postMessage({ type: 'toolUpdateResult', error: err?.message ?? String(err) });
        }
    }

    // ── History handler ───────────────────────────────────────────────────────

    private async _sendHistoryList(webview: vscode.Webview): Promise<void> {
        try {
            const messages = await this.socketClient.mcpConversationList?.() ?? [];
            webview.postMessage({ type: 'historyListResult', messages });
        } catch (err: any) {
            webview.postMessage({ type: 'historyListResult', messages: [], error: err?.message ?? String(err) });
        }
    }

    // ── HTML builder ──────────────────────────────────────────────────────────

    private _buildHtml(webview: vscode.Webview): string {
        const scriptUri = webview.asWebviewUri(
            vscode.Uri.joinPath(this.extensionUri, 'dist', 'webview', 'ai-config.js')
        );
        const nonce = getNonce();
        return `<!DOCTYPE html>
<html>
<head>
<meta charset="UTF-8">
<meta http-equiv="Content-Security-Policy"
      content="default-src 'none';
               style-src 'unsafe-inline';
               script-src ${webview.cspSource} 'nonce-${nonce}';">
<script nonce="${nonce}">${getWebviewErrorHandlerScript()}</script>
</head>
<body>
<app-ai-config-page><p style="color:#888;font-family:sans-serif;padding:16px;font-size:13px;">Loading AI Configuration…</p></app-ai-config-page>
<script src="${scriptUri}"></script>
</body>
</html>`;
    }
}
