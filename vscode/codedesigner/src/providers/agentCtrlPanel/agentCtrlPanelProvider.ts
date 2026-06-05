import * as vscode from 'vscode';
import * as path from 'path';
import { promises as fs } from 'fs';
import { getOutputChannel } from '../../extension';
import { AiActivityEvent, AiLabelUpdateEvent, AiPacketEvent, AiSessionStartEvent, AiToolCallEvent, SocketClient, McpAgentInfo, McpMemoryInfo, McpOrchestrationInfo } from '../../socketClient';
import { getNonce, getWebviewErrorHandlerScript } from '../../utils/nonce';
import { McpProgressReportEditorProvider } from '../mcpProgressReportEditor/mcpProgressReportEditorProvider';
import { applyCodeDesignerPanelIcon } from '../../utils/webviewPanel';

/**
 * Provider for the AI agent control panel standalone webview tab.
 * Manages agent profiles (add / edit / delete / import / export)
 * and shows the agent's associated progress reports.
 */
/** Optional view to focus when opening the AI agent control panel. */
export interface AgentCtrlPanelFocus {
    /** Tab to switch to (e.g. 'agents', 'orchestration'). */
    tab?: string;
    /** Agent profile id to select once the agents tab is shown. */
    agentId?: number;
    /** Orchestration plan id to select once the orchestration tab is shown. */
    orchestrationId?: number;
}

type ActivityKind = 'activity' | 'session' | 'label' | 'packet' | 'tool';
interface ActivityEntry { at: number; kind: ActivityKind; text: string; address?: number; region?: string; args?: string; result?: string; ok?: boolean; }

export class AgentCtrlPanelProvider {
    private _panel: vscode.WebviewPanel | undefined;
    private readonly _mcpProgressReportEditor: McpProgressReportEditorProvider;
    private readonly _activityLog: ActivityEntry[] = [];
    private static readonly MAX_ACTIVITY = 2000;
    /** Focus request applied once the freshly created webview signals 'ready'. */
    private _pendingFocus: AgentCtrlPanelFocus | undefined;

    constructor(
        private readonly extensionUri: vscode.Uri,
        private readonly socketClient: SocketClient,
        private readonly requestMcpStatus: () => Promise<boolean>,
        private readonly runMcpServer: () => Promise<void>,
    ) {
        this._mcpProgressReportEditor = new McpProgressReportEditorProvider(socketClient, extensionUri);
        socketClient.onConnected(() => { void socketClient.subscribeActivity(); });
        socketClient.onActivityNotification((event: AiActivityEvent) => this._recordActivity({ at: Date.now(), kind: 'activity', address: event.address, region: event.region, text: `${event.command} @ ${this._hex(event.address)}${event.region ? `  (${event.region})` : ''}` }));
        socketClient.onSessionStart((_event: AiSessionStartEvent) => this._recordActivity({ at: Date.now(), kind: 'session', text: 'AI session started' }));
        socketClient.onLabelUpdate((event: AiLabelUpdateEvent) => this._recordActivity({ at: Date.now(), kind: 'label', text: `${event.command}${event.setName ? `  set '${event.setName}'` : ''}` }));
        socketClient.onPacketActivity((event: AiPacketEvent) => this._recordActivity({ at: Date.now(), kind: 'packet', text: `${event.command}${event.enable !== undefined ? (event.enable ? '  capture on' : '  capture off') : event.direction !== undefined ? `  dir ${event.direction}` : ''}` }));
        socketClient.onToolCall((event: AiToolCallEvent) => { const payload = event.payload; if (payload) { this._recordActivity({ at: Date.now(), kind: 'tool', text: `${payload.tool}${payload.ok ? '' : '  ✗'}${typeof payload.elapsedMs === 'number' ? `  ${payload.elapsedMs}ms` : ''}`, args: payload.args, result: payload.result, ok: payload.ok }); } });
    }

    /**
     * Open (or reveal) the AI agent control panel. When `focus` is given the
     * panel switches to that tab and selects the requested agent — applied
     * immediately if the panel already exists, or after the webview's first
     * 'ready' message when it is freshly created.
     */
    public open(focus?: AgentCtrlPanelFocus): void {
        if (this._panel) {
            this._panel.reveal(vscode.ViewColumn.One);
            if (focus) {
                this._panel.webview.postMessage({
                    type:            'aiConfigFocus',
                    tab:             focus.tab,
                    agentId:         focus.agentId,
                    orchestrationId: focus.orchestrationId,
                });
            }
            return;
        }

        this._pendingFocus = focus;

        const panel = vscode.window.createWebviewPanel(
            'codedesigner.agentCtrlPanel',
            'AI',
            vscode.ViewColumn.One,
            {
                enableScripts:           true,
                retainContextWhenHidden: true,
                localResourceRoots:      [this.extensionUri],
            }
        );
        applyCodeDesignerPanelIcon(panel, this.extensionUri, 'bot');
        this._panel = panel;
        void this._sendMcpStatus(panel.webview);
        void this.socketClient.subscribeActivity();

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
                await this._sendMcpStatus(webview);
                webview.postMessage({ type: 'activityInit', entries: this._activityLog });
                // Initial load — send agent list and orchestration list
                await this._sendAgentList(webview);
                await this._sendOrchestrationList(webview);
                // Apply a pending focus request now that the webview is alive.
                if (this._pendingFocus) {
                    webview.postMessage({
                        type:            'aiConfigFocus',
                        tab:             this._pendingFocus.tab,
                        agentId:         this._pendingFocus.agentId,
                        orchestrationId: this._pendingFocus.orchestrationId,
                    });
                    this._pendingFocus = undefined;
                }
                break;

            case 'requestMcpStatus': await this._sendMcpStatus(webview); break;
            case 'runMcpServer': await this.runMcpServer(); await this._sendMcpStatus(webview); break;
            case 'activityClear': this._activityLog.length = 0; webview.postMessage({ type: 'activityInit', entries: [] }); break;

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
                this._mcpProgressReportEditor.open(
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

            // ── Workspace skill recipes ───────────────────────────────────────
            case 'skillList': await this._sendSkillList(webview); break;
            case 'skillSave': await this._handleSkillSave(msg, webview); break;
            case 'skillDelete': await this._handleSkillDelete(msg['path'] as string, webview); break;

            // ── Chat tab (sessions + streamed messages) ───────────────────────
            case 'chatSessionList':   await this._sendChatSessionList(webview); break;
            case 'chatSessionGet':    await this._handleChatSessionGet(msg['id'] as number, webview); break;
            case 'chatSessionCreate': await this._handleChatSessionCreate(msg, webview); break;
            case 'chatSessionRename': await this._handleChatSessionRename(msg, webview); break;
            case 'chatSessionDelete': await this._handleChatSessionDelete(msg['id'] as number, webview); break;
            case 'chatMessageClear':  await this._handleChatMessageClear(msg['sessionId'] as number, webview); break;
            case 'chatSend':          await this._handleChatSend(msg, webview); break;

            default:
                getOutputChannel().warn(`[AgentCtrlPanel] Unknown message type: ${type}`);
                break;
        }
    }

    private _hex(value: number): string { return `0x${value.toString(16).toUpperCase().padStart(8, '0')}`; }
    private _recordActivity(entry: ActivityEntry): void { this._activityLog.push(entry); if (this._activityLog.length > AgentCtrlPanelProvider.MAX_ACTIVITY) { this._activityLog.shift(); } this._panel?.webview.postMessage({ type: 'activityAppend', entry }); }
    private async _sendMcpStatus(webview: vscode.Webview): Promise<void> { webview.postMessage({ type: 'mcpStatus', online: await this.requestMcpStatus() }); }

    // ── Agent handlers ────────────────────────────────────────────────────────

    private async _sendAgentList(webview: vscode.Webview): Promise<void> {
        try {
            const agents = (await this.socketClient.mcpAgentList()).map(agent => ({ ...agent, imageUri: this.resolveAgentImageUri(agent.name, webview) }));
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
            const list = (this.socketClient as any).mcpConversationList;
            const messages = list ? await list.call(this.socketClient) : [];
            webview.postMessage({ type: 'historyListResult', messages });
        } catch (err: any) {
            webview.postMessage({ type: 'historyListResult', messages: [], error: err?.message ?? String(err) });
        }
    }

    // ── Workspace skill handlers ─────────────────────────────────────────────

    private getWorkspaceRoot(): string | undefined { return vscode.workspace.workspaceFolders?.[0]?.uri.fsPath; }
    private getSkillsRoot(): string | undefined { const root = this.getWorkspaceRoot(); return root ? path.join(root, '.github', 'skills') : undefined; }

    private async _sendSkillList(webview: vscode.Webview): Promise<void> {
        try {
            const root = this.getSkillsRoot(); if (!root) { throw new Error('Open a workspace to manage skills.'); }
            await fs.mkdir(root, { recursive: true });
            const entries = await fs.readdir(root, { withFileTypes: true });
            const skills = await Promise.all(entries.filter(entry => entry.isDirectory()).map(async entry => {
                const skillPath = path.join(root, entry.name, 'SKILL.md');
                try { const [content, stat] = await Promise.all([fs.readFile(skillPath, 'utf8'), fs.stat(skillPath)]); return { name: entry.name, path: path.relative(this.getWorkspaceRoot()!, skillPath), content, updatedAt: stat.mtime.toISOString() }; } catch { return undefined; }
            }));
            webview.postMessage({ type: 'skillListResult', skills: skills.filter(Boolean).sort((a, b) => a!.name.localeCompare(b!.name)) });
        } catch (err: any) { webview.postMessage({ type: 'skillListResult', skills: [], error: err?.message ?? String(err) }); }
    }

    private async _handleSkillSave(msg: Record<string, unknown>, webview: vscode.Webview): Promise<void> {
        try {
            const root = this.getSkillsRoot(), workspaceRoot = this.getWorkspaceRoot(); if (!root || !workspaceRoot) { throw new Error('Open a workspace to manage skills.'); }
            const skill = msg['skill'] as { name?: string; path?: string; content?: string }; const name = (skill?.name ?? '').trim();
            if (!/^[a-z0-9][a-z0-9-]*$/.test(name)) { throw new Error('Skill names must use lowercase letters, numbers, and hyphens.'); }
            const skillPath = path.join(root, name, 'SKILL.md'); if (!skillPath.startsWith(`${root}${path.sep}`)) { throw new Error('Invalid skill path.'); }
            if (!msg['isNew'] && skill?.path) { const oldPath = path.resolve(workspaceRoot, skill.path); if (oldPath !== skillPath && oldPath.startsWith(`${root}${path.sep}`)) { await fs.rm(path.dirname(oldPath), { recursive: true, force: true }); } }
            await fs.mkdir(path.dirname(skillPath), { recursive: true }); await fs.writeFile(skillPath, skill?.content ?? '', 'utf8'); const stat = await fs.stat(skillPath);
            webview.postMessage({ type: 'skillActionResult', skill: { name, path: path.relative(workspaceRoot, skillPath), content: skill?.content ?? '', updatedAt: stat.mtime.toISOString() }, message: `Saved '${name}'.` });
        } catch (err: any) { webview.postMessage({ type: 'skillActionResult', error: err?.message ?? String(err) }); }
    }

    private async _handleSkillDelete(relativePath: string, webview: vscode.Webview): Promise<void> {
        try {
            const root = this.getSkillsRoot(), workspaceRoot = this.getWorkspaceRoot(); if (!root || !workspaceRoot) { throw new Error('Open a workspace to manage skills.'); }
            const skillPath = path.resolve(workspaceRoot, relativePath); if (!skillPath.startsWith(`${root}${path.sep}`) || path.basename(skillPath) !== 'SKILL.md') { throw new Error('Invalid skill path.'); }
            await fs.rm(path.dirname(skillPath), { recursive: true, force: true }); webview.postMessage({ type: 'skillActionResult', message: 'Deleted skill.' });
        } catch (err: any) { webview.postMessage({ type: 'skillActionResult', error: err?.message ?? String(err) }); }
    }

    // ── Chat tab handlers ────────────────────────────────────────────────────
    //
    // The webview owns the rendered chat shell; this provider is the message
    // courier between it and the SocketClient. Wire payloads follow the
    // contract in docs/ai-chat-templates.md.

    private async _sendChatSessionList(webview: vscode.Webview): Promise<void> {
        try {
            const sessions = await this.socketClient.mcpChatSessionList();
            webview.postMessage({ type: 'chatSessionListResult', sessions });
        } catch (err: any) {
            webview.postMessage({ type: 'chatSessionListResult', sessions: [], error: err?.message ?? String(err) });
        }
    }

    private async _handleChatSessionGet(id: number, webview: vscode.Webview): Promise<void> {
        try {
            const { session, messages } = await this.socketClient.mcpChatSessionGet(id);
            webview.postMessage({ type: 'chatSessionGetResult', session, messages });
        } catch (err: any) {
            webview.postMessage({ type: 'chatSessionGetResult', error: err?.message ?? String(err) });
        }
    }

    private async _handleChatSessionCreate(msg: Record<string, unknown>, webview: vscode.Webview): Promise<void> {
        try {
            const session = await this.socketClient.mcpChatSessionCreate({
                title:           (msg['title']           as string | undefined) ?? 'New chat',
                orchestrationId:  msg['orchestrationId'] as number | undefined,
                gameSerial:       msg['gameSerial']      as string | undefined,
                gameTag:          msg['gameTag']         as string | undefined,
            });
            webview.postMessage({ type: 'chatSessionActionResult', session, message: `Created '${session.title}'.` });
        } catch (err: any) {
            webview.postMessage({ type: 'chatSessionActionResult', error: err?.message ?? String(err) });
        }
    }

    private async _handleChatSessionRename(msg: Record<string, unknown>, webview: vscode.Webview): Promise<void> {
        try {
            const session = await this.socketClient.mcpChatSessionRename(msg['id'] as number, msg['title'] as string);
            webview.postMessage({ type: 'chatSessionActionResult', session, message: `Renamed to '${session.title}'.` });
        } catch (err: any) {
            webview.postMessage({ type: 'chatSessionActionResult', error: err?.message ?? String(err) });
        }
    }

    private async _handleChatSessionDelete(id: number, webview: vscode.Webview): Promise<void> {
        try {
            await this.socketClient.mcpChatSessionDelete(id);
            webview.postMessage({ type: 'chatSessionActionResult', deletedId: id, message: 'Deleted.' });
        } catch (err: any) {
            webview.postMessage({ type: 'chatSessionActionResult', error: err?.message ?? String(err) });
        }
    }

    private async _handleChatMessageClear(sessionId: number, webview: vscode.Webview): Promise<void> {
        try {
            await this.socketClient.mcpChatMessageClear(sessionId);
            webview.postMessage({ type: 'chatMessageClearResult', sessionId, message: 'Cleared.' });
        } catch (err: any) {
            webview.postMessage({ type: 'chatMessageClearResult', error: err?.message ?? String(err) });
        }
    }

    private async _handleChatSend(msg: Record<string, unknown>, webview: vscode.Webview): Promise<void> {
        const sessionId = msg['sessionId'] as number;
        const text      = (msg['text'] as string | undefined) ?? '';
        try {
            const appended = await this.socketClient.mcpChatSend(sessionId, text);
            webview.postMessage({ type: 'chatSendResult', sessionId, messages: appended });
        } catch (err: any) {
            webview.postMessage({ type: 'chatSendResult', sessionId, messages: [], error: err?.message ?? String(err) });
        }
    }

    private static readonly AGENT_IMAGE_MAP = [
        { file: 'scorpion.png', keywords: ['scorpion'] }, { file: 'sub-zero.png', keywords: ['subzero'] }, { file: 'raiden.png', keywords: ['raiden'] }, { file: 'liu-kang.png', keywords: ['liukang', 'liu'] }, { file: 'johnny-cage.png', keywords: ['johnnycage', 'johnny', 'cage'] }, { file: 'sonya-blade.png', keywords: ['sonyablade', 'sonya'] }, { file: 'kano.png', keywords: ['kano'] }, { file: 'kitana.png', keywords: ['kitana'] }, { file: 'mileena.png', keywords: ['mileena'] },
    ];
    private resolveAgentImageUri(name: string, webview: vscode.Webview): string { const normalized = (name ?? '').toLowerCase().replace(/[^a-z0-9]/g, ''); const match = AgentCtrlPanelProvider.AGENT_IMAGE_MAP.find(item => item.keywords.some(keyword => normalized.includes(keyword))); return match ? webview.asWebviewUri(vscode.Uri.joinPath(this.extensionUri, 'media', 'agents', match.file)).toString() : ''; }

    // ── HTML builder ──────────────────────────────────────────────────────────

    private _buildHtml(webview: vscode.Webview): string {
        const scriptUri = webview.asWebviewUri(
            vscode.Uri.joinPath(this.extensionUri, 'dist', 'webview', 'agent-ctrl-panel.js')
        );
        const nonce = getNonce();
        return `<!DOCTYPE html>
<html>
<head>
<meta charset="UTF-8">
<meta http-equiv="Content-Security-Policy"
      content="default-src 'none';
               style-src 'unsafe-inline';
               img-src ${webview.cspSource} data:;
               script-src ${webview.cspSource} 'nonce-${nonce}';">
<script nonce="${nonce}">${getWebviewErrorHandlerScript()}</script>
</head>
<body>
<app-agent-ctrl-panel-page><p style="color:#888;font-family:sans-serif;padding:16px;font-size:13px;">Loading AI…</p></app-agent-ctrl-panel-page>
<script src="${scriptUri}"></script>
</body>
</html>`;
    }
}
