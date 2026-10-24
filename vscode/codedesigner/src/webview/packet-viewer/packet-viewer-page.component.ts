import {
    Component,
    AfterViewInit,
    ViewEncapsulation,
    viewChild,
    NgZone,
    inject,
    OnDestroy,
    signal,
} from '@angular/core';
import { getVsCodeApi } from '../shared/vscode-api';
import { PageHeaderComponent } from '../shared/page-header.component';
import { PacketViewerComponent, ViewerSettings } from './packet-viewer.component';
import { ComposerSettings, PacketComposerComponent } from './packet-composer.component';
import { CapturedPacket, CatalogEntry, DissectedLayer, FilterPreset, NetStats, NetStatus, PacketViewerAction, ParsedPacketRow, StructDef } from './types';
import '../shared/styles/shared.css';
import './packet-viewer-page.css';

interface SavedState {
    viewer?: ViewerSettings;
    composer?: ComposerSettings;
    composerOpen?: boolean;
}

/**
 * Full-page host for the Packet Viewer tab: routes the list's and composer's actions to the
 * extension host and the host's packetViewer* messages back to them, and keeps the view
 * settings in the webview state.
 */
@Component({
    selector: 'app-packet-viewer-page',
    standalone: true,
    imports: [PacketViewerComponent, PacketComposerComponent, PageHeaderComponent],
    encapsulation: ViewEncapsulation.None,
    template: `
<div class="pv-page">
    <app-page-header icon="radiotower" title="Packets" subtitle="Capture, label and inject PS2 network traffic" />
    <div class="pv-layout">
        <app-packet-viewer #pv class="pv-viewer"
            (action)="post($event)"
            (resendRequested)="resend($event)"
            (editRequested)="edit($event)"
            (composerToggled)="setComposerOpen(!composerOpen())"
            (selectionChanged)="selected.set($event)"
            (settingsChanged)="saveViewer($event)" />
        <!-- Kept mounted while closed so its frame, form and log survive toggling. -->
        <app-packet-composer #composer class="pv-composer-host" [class.pv-hidden]="!composerOpen()"
            [catalog]="catalog()"
            [selected]="selected()"
            (action)="post($event)"
            (closed)="setComposerOpen(false)"
            (settingsChanged)="saveComposer($event)" />
    </div>
</div>`,
})
export class PacketViewerPageComponent implements AfterViewInit, OnDestroy {
    private readonly pvRef       = viewChild.required<PacketViewerComponent>('pv');
    private readonly composerRef = viewChild.required<PacketComposerComponent>('composer');
    private readonly vscode = getVsCodeApi();
    private readonly zone = inject(NgZone);
    private _messageHandler?: (event: MessageEvent) => void;
    private _state: SavedState = {};
    /** A resend in flight, reported in the status bar instead of the composer log. */
    private _resendPending = false;

    readonly composerOpen = signal(false);
    readonly catalog      = signal<CatalogEntry[]>([]);
    readonly selected     = signal<CapturedPacket | null>(null);

    ngAfterViewInit(): void {
        const pv = this.pvRef();
        this._state = (this.vscode.getState() ?? {}) as SavedState;
        if (this._state.viewer) { pv.applySettings(this._state.viewer); }
        this.composerOpen.set(this._state.composerOpen === true);
        pv.composerOpen.set(this.composerOpen());
        if (this._state.composer) { this.composerRef().applySettings(this._state.composer); }

        this._messageHandler = (event: MessageEvent) => {
            const data = event.data as { type: string; [key: string]: unknown };
            this.zone.run(() => this.onHostMessage(data));
        };
        window.addEventListener('message', this._messageHandler);

        this.vscode.postMessage({ type: 'ready' });
        this.post({ type: 'getPresets' });
    }

    ngOnDestroy(): void {
        if (this._messageHandler) { window.removeEventListener('message', this._messageHandler); }
        this.pvRef().stopPolling();
    }

    post(action: PacketViewerAction): void {
        if (action.type === 'getCatalog' && !this.catalog().length) { this.pvRef().showStatus('Loading message catalog…'); }
        this.vscode.postMessage({ type: 'packetViewerAction', action });
    }

    /** Send a captured packet again the way it travelled (TX → from the PS2, RX → into the PS2). */
    resend(index: number): void {
        const p = this.pvRef().packetAt(index);
        if (!p) { return; }
        this._resendPending = true;
        this.pvRef().showStatus(`Resending packet #${index + 1}…`);
        // tcpContinue moves a TCP segment onto the live flow; a verbatim TCP replay is dropped as a duplicate.
        this.post({ type: 'inject', frame: p.data, tx: p.direction === 0, fix: false, tcpContinue: true });
    }

    edit(index: number): void {
        const p = this.pvRef().packetAt(index);
        if (!p) { return; }
        this.setComposerOpen(true);
        this.composerRef().load(p);
    }

    setComposerOpen(open: boolean): void {
        this.composerOpen.set(open);
        this.pvRef().composerOpen.set(open);
        if (open && !this.catalog().length) { this.post({ type: 'getCatalog' }); }
        this.save({ composerOpen: open });
    }

    saveViewer(s: ViewerSettings): void { this.save({ viewer: s }); }
    saveComposer(s: ComposerSettings): void { this.save({ composer: s }); }

    private save(patch: Partial<SavedState>): void {
        this._state = { ...this._state, ...patch };
        this.vscode.setState(this._state);
    }

    private onHostMessage(data: { type: string; [key: string]: unknown }): void {
        const pv = this.pvRef();
        const composer = this.composerRef();
        const message = (data['message'] as string | undefined) ?? '';
        switch (data.type) {
            case 'packetViewerStatus':
                pv.setStatus(data['status'] as NetStatus);
                break;
            case 'packetViewerStats':
                pv.setStats(data['stats'] as NetStats);
                break;
            case 'packetViewerCatalog':
                this.catalog.set((data['catalog'] as CatalogEntry[]) ?? []);
                if (pv.statusMessage() === 'Loading message catalog…') { pv.showStatus(''); }
                break;
            case 'packetViewerPackets':
                pv.addCapturedPackets((data['packets'] as CapturedPacket[]) ?? [], (data['parsedRows'] as ParsedPacketRow[]) ?? []);
                pv.recvCaptureComplete();
                break;
            case 'packetViewerCaptureStarted':
                pv.captureStarted();
                break;
            case 'packetViewerCaptureFailed':
                pv.captureStartFailed(message || 'Capture failed');
                break;
            case 'packetViewerMessage':
            case 'packetViewerSaveResult':
                pv.showStatus(message);
                break;
            case 'packetViewerPcapLoaded': {
                const packets = (data['packets'] as CapturedPacket[]) ?? [];
                pv.onClear();
                pv.addCapturedPackets(packets, (data['parsedRows'] as ParsedPacketRow[]) ?? []);
                pv.setFilename((data['filename'] as string) ?? '');
                pv.showStatus(`Loaded ${packets.length} packet(s)`);
                break;
            }
            case 'packetViewerInjectResult': {
                const ok = data['ok'] === true;
                if (this._resendPending) {
                    this._resendPending = false;
                    pv.showStatus(message, !ok);
                } else {
                    composer?.onInjectResult(ok, message);
                }
                break;
            }
            case 'packetViewerFramePreview':
                composer?.onPreview(data as unknown as { requestId: number; frame: string; layers: DissectedLayer[]; row?: ParsedPacketRow; message: string });
                break;
            case 'packetViewerFramePreviewError':
                composer?.onPreviewError(message);
                break;
            case 'packetViewerMessageBuilt':
                composer?.onMessageBuilt(data['data'] as string, message);
                break;
            case 'packetViewerPresets':
                pv.setPresets((data['presets'] as FilterPreset[]) ?? [], data['selected'] as string | undefined);
                break;
            case 'packetViewerStructs':
                pv.setStructDefs((data['structs'] as StructDef[]) ?? [], message);
                break;
            case 'packetViewerError':
                pv.showStatus(message || 'Error', true);
                pv.recvCaptureComplete();
                break;
        }
    }
}
