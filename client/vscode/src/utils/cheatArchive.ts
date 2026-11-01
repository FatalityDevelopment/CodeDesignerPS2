import * as vscode from 'vscode';

/**
 * Locate a CheatCodeArchive checkout whose `Database/` text files can be imported
 * into the cheat archive (CHEATEDITOR_IMPORT): the `CheatCodeArchive` submodule in
 * an open workspace folder, or next to the extension in a repo checkout.
 * Returns undefined when none is found.
 */
export async function findCheatArchiveRoot(extensionUri: vscode.Uri): Promise<string | undefined> {
    const candidates: vscode.Uri[] = [];
    for (const folder of vscode.workspace.workspaceFolders ?? []) {
        candidates.push(vscode.Uri.joinPath(folder.uri, 'CheatCodeArchive'));
    }
    candidates.push(vscode.Uri.joinPath(extensionUri, '..', '..', 'CheatCodeArchive'));

    for (const candidate of candidates) {
        try {
            const stat = await vscode.workspace.fs.stat(vscode.Uri.joinPath(candidate, 'Database'));
            if (stat.type & vscode.FileType.Directory) { return candidate.fsPath; }
        } catch { /* keep looking */ }
    }
    return undefined;
}
