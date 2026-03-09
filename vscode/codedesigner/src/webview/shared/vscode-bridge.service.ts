import { Injectable } from '@angular/core';
import { fromEvent, Observable } from 'rxjs';
import { map, filter, shareReplay } from 'rxjs/operators';
import { getVsCodeApi } from './vscode-api';

@Injectable({ providedIn: 'root' })
export class VsCodeBridgeService {
    readonly messages$: Observable<unknown>;

    constructor() {
        this.messages$ = fromEvent<MessageEvent>(window, 'message').pipe(
            map(event => event.data),
            shareReplay(1)
        );
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
