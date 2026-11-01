import { Injectable, OnDestroy } from '@angular/core';
import { fromEvent, Observable, ReplaySubject, Subscription } from 'rxjs';
import { map, filter } from 'rxjs/operators';
import { getVsCodeApi } from './vscode-api';

/**
 * Maximum number of VS Code messages to buffer while Angular is bootstrapping.
 * In normal operation the extension sends ≤5 messages before ngOnInit subscribes
 * (init, connectionStatus, paused, updateSnapshots, possibly one more).
 * 50 gives ample headroom without unbounded memory use.
 */
const MESSAGE_BUFFER_SIZE = 50;

@Injectable({ providedIn: 'root' })
export class VsCodeBridgeService implements OnDestroy {
    readonly messages$: Observable<unknown>;
    private readonly _subscription: Subscription;

    constructor() {
        // Use a ReplaySubject so ALL messages that arrive during Angular bootstrap
        // (before ngOnInit subscribes) are buffered and replayed to late subscribers.
        const subject = new ReplaySubject<unknown>(MESSAGE_BUFFER_SIZE);
        this._subscription = fromEvent<MessageEvent>(window, 'message').pipe(
            map(event => event.data)
        ).subscribe(subject);
        this.messages$ = subject.asObservable();
    }

    ngOnDestroy(): void {
        this._subscription.unsubscribe();
    }

    postMessage(msg: unknown): void {
        getVsCodeApi().postMessage(msg);
    }

    on<T>(type: string): Observable<T> {
        return this.messages$.pipe(
            filter((msg: unknown) => (msg as Record<string, unknown>)?.['type'] === type
                                  || (msg as Record<string, unknown>)?.['command'] === type)
        ) as Observable<T>;
    }
}
