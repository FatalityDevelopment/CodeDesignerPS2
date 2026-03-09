declare function acquireVsCodeApi(): {
    postMessage(message: unknown): void;
    getState(): unknown;
    setState(state: unknown): void;
};

let _api: ReturnType<typeof acquireVsCodeApi> | undefined;

export function getVsCodeApi() {
    if (!_api) {
        _api = acquireVsCodeApi();
    }
    return _api;
}
