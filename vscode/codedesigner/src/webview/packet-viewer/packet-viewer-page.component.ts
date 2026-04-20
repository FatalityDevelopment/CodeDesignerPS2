import {
    Component,
    AfterViewInit,
    ViewEncapsulation,
    viewChild,
    NgZone,
    inject,
    OnDestroy,
} from '@angular/core';
import { getVsCodeApi } from '../shared/vscode-api';
import {
    PacketViewerComponent,
    PacketViewerAction,
} from './packet-viewer.component';
import './packet-viewer-page.css';

/**
 * Full-page host for the Packet Viewer standalone webview tab.
 *
 * This component wraps PacketViewerComponent and:
 *  - Routes PacketViewerAction outputs to the extension via vscode.postMessage().
 *  - Receives packetViewer* messages from the extension host and forwards
 *    them to the viewer's public API.
 *  - On init, signals 'ready' so the provider can send initial data.
 */
@Component({
    selector: 'app-packet-viewer-page',
    standalone: true,
    imports: [PacketViewerComponent],
    encapsulation: ViewEncapsulation.None,
    template: `<app-packet-viewer #pvComp />`,
})
export class PacketViewerPageComponent implements AfterViewInit, OnDestroy {
    private readonly pvRef = viewChild.required<PacketViewerComponent>('pvComp');
    private readonly vscode = getVsCodeApi();
    private readonly zone = inject(NgZone);
    private _messageHandler?: (event: MessageEvent) => void;

    ngAfterViewInit(): void {
        const pv = this.pvRef();

        // Wire the action callback so the component can emit actions.
        pv.onAction = (action: PacketViewerAction) => {
            this.vscode.postMessage({ type: 'packetViewerAction', action });
        };

        // Listen for messages from the extension host.
        this._messageHandler = (event: MessageEvent) => {
            const data = event.data as { type: string; [key: string]: unknown };
            this.zone.run(() => {
                switch (data['type']) {
                    case 'packetViewerStatus':
                        pv.setStatus(data['status'] as any);
                        break;
                    case 'packetViewerStats':
                        pv.setStats(data['stats'] as any);
                        break;
                    case 'packetViewerPackets':
                        pv.addCapturedPackets(data['packets'] as any[] ?? []);
                        pv.recvCaptureComplete();
                        break;
                    case 'packetViewerCaptureStarted':
                        pv.captureStarted();
                        break;
                    case 'packetViewerCaptureFailed':
                        pv.captureStartFailed(data['message'] as string ?? 'Capture failed');
                        break;
                    case 'packetViewerMessage':
                        pv.showStatus(data['message'] as string ?? '');
                        break;
                    case 'packetViewerPcapLoaded':
                        pv.onClear();
                        pv.addCapturedPackets(data['packets'] as any[] ?? []);
                        pv.recvCaptureComplete();
                        pv.showStatus(`Loaded ${(data['packets'] as any[] ?? []).length} packet(s) from file`);
                        break;
                    case 'packetViewerSaveResult':
                        pv.showStatus(data['message'] as string ?? '');
                        break;
                    case 'packetViewerError':
                        pv.showStatus(data['message'] as string ?? 'Error');
                        pv.recvCaptureComplete();
                        break;
                }
            });
        };
        window.addEventListener('message', this._messageHandler);

        // Tell the extension host the webview is ready to receive data.
        this.vscode.postMessage({ type: 'ready' });
    }

    ngOnDestroy(): void {
        if (this._messageHandler) {
            window.removeEventListener('message', this._messageHandler);
        }
    }
}
