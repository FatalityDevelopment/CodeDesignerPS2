import * as vscode from 'vscode';
import * as fs from 'fs';
import * as path from 'path';
import { getOutputChannel } from '../../extension';

// ── Data model ────────────────────────────────────────────────────────────────

export interface PointerChildLabel {
    address: string;
    name: string;
    comment?: string;
}

export interface UserPointer {
    address: string;
    name: string;
    comment?: string;
    children: PointerChildLabel[];
}

interface PointerMapFile {
    pointers: UserPointer[];
}

// ── Tree item ─────────────────────────────────────────────────────────────────

export class PointerTreeItem extends vscode.TreeItem {
    constructor(
        public readonly itemType: 'pointer' | 'child',
        public readonly address: string,
        label: string,
        collapsibleState: vscode.TreeItemCollapsibleState,
        public readonly parentAddress?: string,
        comment?: string
    ) {
        super(label, collapsibleState);
        this.description = address;
        this.tooltip = comment
            ? `${address}  —  ${comment}`
            : address;

        if (itemType === 'pointer') {
            this.iconPath = new vscode.ThemeIcon('symbol-variable');
            this.contextValue = 'pointerNode';
        } else {
            this.iconPath = new vscode.ThemeIcon('symbol-field');
            this.contextValue = 'childLabelNode';
        }

        this.command = {
            command: 'codedesigner.pointerManager.goToAddress',
            title: 'Go to Address',
            arguments: [address],
        };
    }
}

// ── Provider ──────────────────────────────────────────────────────────────────

export class PointerManagerProvider implements vscode.TreeDataProvider<PointerTreeItem> {
    private readonly _onDidChangeTreeData = new vscode.EventEmitter<PointerTreeItem | undefined | void>();
    readonly onDidChangeTreeData = this._onDidChangeTreeData.event;

    private _pointers: UserPointer[] = [];

    /** Absolute path to the persisted JSON file, or undefined when unavailable. */
    private _storagePath: string | undefined;

    constructor(private readonly _context: vscode.ExtensionContext) {
        this._storagePath = this._resolveStoragePath();
        this._load();
    }

    // ── Storage helpers ───────────────────────────────────────────────────────

    private _resolveStoragePath(): string | undefined {
        const workspaceFolder = vscode.workspace.workspaceFolders?.[0]?.uri.fsPath;
        if (workspaceFolder) {
            return path.join(workspaceFolder, 'pointerMap.json');
        }
        // Fallback to VS Code's global storage (survives session restarts).
        try {
            const dir = this._context.globalStorageUri.fsPath;
            if (!fs.existsSync(dir)) { fs.mkdirSync(dir, { recursive: true }); }
            return path.join(dir, 'pointerMap.json');
        } catch {
            return undefined;
        }
    }

    private _load(): void {
        if (!this._storagePath || !fs.existsSync(this._storagePath)) {
            this._pointers = [];
            return;
        }
        try {
            const raw = fs.readFileSync(this._storagePath, 'utf-8');
            const doc = JSON.parse(raw) as PointerMapFile;
            this._pointers = Array.isArray(doc.pointers) ? doc.pointers : [];
        } catch (err: any) {
            getOutputChannel().warn(`PointerManager: failed to load ${this._storagePath}: ${err?.message ?? err}`);
            this._pointers = [];
        }
    }

    private _save(): void {
        if (!this._storagePath) { return; }
        try {
            const doc: PointerMapFile = { pointers: this._pointers };
            fs.writeFileSync(this._storagePath, JSON.stringify(doc, null, 2), 'utf-8');
        } catch (err: any) {
            getOutputChannel().warn(`PointerManager: failed to save ${this._storagePath}: ${err?.message ?? err}`);
        }
    }

    // ── TreeDataProvider ──────────────────────────────────────────────────────

    getTreeItem(element: PointerTreeItem): vscode.TreeItem {
        return element;
    }

    getChildren(element?: PointerTreeItem): PointerTreeItem[] {
        if (!element) {
            // Root level — return all pointers.
            return this._pointers.map(p =>
                new PointerTreeItem(
                    'pointer',
                    p.address,
                    p.name,
                    p.children.length > 0
                        ? vscode.TreeItemCollapsibleState.Collapsed
                        : vscode.TreeItemCollapsibleState.None,
                    undefined,
                    p.comment
                )
            );
        }
        if (element.itemType === 'pointer') {
            const ptr = this._findPointer(element.address);
            return (ptr?.children ?? []).map(c =>
                new PointerTreeItem(
                    'child',
                    c.address,
                    c.name,
                    vscode.TreeItemCollapsibleState.None,
                    element.address,
                    c.comment
                )
            );
        }
        return [];
    }

    // ── Public mutators ───────────────────────────────────────────────────────

    /** Refresh the tree (e.g. after the storage file changes externally). */
    refresh(): void {
        this._load();
        this._onDidChangeTreeData.fire();
    }

    /** Add a new user-defined pointer. */
    async addPointer(): Promise<void> {
        const addressRaw = await vscode.window.showInputBox({
            title: 'Add Pointer — Address',
            prompt: 'Enter the virtual address of the pointer (hex)',
            placeHolder: '0x80100000',
            ignoreFocusOut: true,
            validateInput: v => this._validateHexAddress(v),
        });
        if (addressRaw === undefined) { return; }

        const name = await vscode.window.showInputBox({
            title: 'Add Pointer — Name',
            prompt: 'Enter a name for this pointer',
            placeHolder: 'e.g. PlayerStruct',
            ignoreFocusOut: true,
            validateInput: v => v.trim() ? undefined : 'Name cannot be empty',
        });
        if (name === undefined) { return; }

        const comment = await vscode.window.showInputBox({
            title: 'Add Pointer — Comment (optional)',
            prompt: 'Enter an optional comment',
            placeHolder: 'Leave blank to skip',
            ignoreFocusOut: true,
        });
        if (comment === undefined) { return; }

        const address = this._normaliseAddress(addressRaw);
        const existing = this._findPointer(address);
        if (existing) {
            vscode.window.showWarningMessage(`A pointer already exists at ${address}.`);
            return;
        }

        this._pointers.push({ address, name: name.trim(), comment: comment.trim() || undefined, children: [] });
        this._save();
        this._onDidChangeTreeData.fire();
    }

    /** Add a child label under the given pointer address. */
    async addChildLabel(pointerAddress: string): Promise<void> {
        const ptr = this._findPointer(pointerAddress);
        if (!ptr) {
            vscode.window.showErrorMessage(`Pointer at ${pointerAddress} not found.`);
            return;
        }

        const addressRaw = await vscode.window.showInputBox({
            title: `Add Label under "${ptr.name}" — Address`,
            prompt: 'Enter the virtual address of this label (hex)',
            placeHolder: '0x80100004',
            ignoreFocusOut: true,
            validateInput: v => this._validateHexAddress(v),
        });
        if (addressRaw === undefined) { return; }

        const name = await vscode.window.showInputBox({
            title: `Add Label under "${ptr.name}" — Name`,
            prompt: 'Enter a name for this label',
            placeHolder: 'e.g. health',
            ignoreFocusOut: true,
            validateInput: v => v.trim() ? undefined : 'Name cannot be empty',
        });
        if (name === undefined) { return; }

        const comment = await vscode.window.showInputBox({
            title: `Add Label under "${ptr.name}" — Comment (optional)`,
            prompt: 'Enter an optional comment',
            placeHolder: 'Leave blank to skip',
            ignoreFocusOut: true,
        });
        if (comment === undefined) { return; }

        const address = this._normaliseAddress(addressRaw);
        ptr.children.push({ address, name: name.trim(), comment: comment.trim() || undefined });
        this._save();
        this._onDidChangeTreeData.fire();
    }

    /** Edit the name/comment of an existing pointer or child label. */
    async editNode(item: PointerTreeItem): Promise<void> {
        if (item.itemType === 'pointer') {
            const ptr = this._findPointer(item.address);
            if (!ptr) { return; }

            const newName = await vscode.window.showInputBox({
                title: `Edit Pointer "${ptr.name}"`,
                prompt: 'New name',
                value: ptr.name,
                ignoreFocusOut: true,
                validateInput: v => v.trim() ? undefined : 'Name cannot be empty',
            });
            if (newName === undefined) { return; }

            const newComment = await vscode.window.showInputBox({
                title: `Edit Pointer "${ptr.name}" — Comment`,
                prompt: 'New comment (leave blank to remove)',
                value: ptr.comment ?? '',
                ignoreFocusOut: true,
            });
            if (newComment === undefined) { return; }

            ptr.name    = newName.trim();
            ptr.comment = newComment.trim() || undefined;
        } else {
            const ptr   = this._findPointer(item.parentAddress ?? '');
            const child = ptr?.children.find(c => c.address === item.address);
            if (!child) { return; }

            const newName = await vscode.window.showInputBox({
                title: `Edit Label "${child.name}"`,
                prompt: 'New name',
                value: child.name,
                ignoreFocusOut: true,
                validateInput: v => v.trim() ? undefined : 'Name cannot be empty',
            });
            if (newName === undefined) { return; }

            const newComment = await vscode.window.showInputBox({
                title: `Edit Label "${child.name}" — Comment`,
                prompt: 'New comment (leave blank to remove)',
                value: child.comment ?? '',
                ignoreFocusOut: true,
            });
            if (newComment === undefined) { return; }

            child.name    = newName.trim();
            child.comment = newComment.trim() || undefined;
        }

        this._save();
        this._onDidChangeTreeData.fire();
    }

    /** Delete a pointer (and all its children) or a single child label. */
    async deleteNode(item: PointerTreeItem): Promise<void> {
        if (item.itemType === 'pointer') {
            const ptr = this._findPointer(item.address);
            const childCount = ptr?.children.length ?? 0;
            const confirmed = await vscode.window.showWarningMessage(
                `Delete pointer "${item.label}"${childCount > 0 ? ` and its ${childCount} child label(s)` : ''}?`,
                { modal: true },
                'Delete'
            );
            if (confirmed !== 'Delete') { return; }
            this._pointers = this._pointers.filter(p => p.address !== item.address);
        } else {
            const ptr = this._findPointer(item.parentAddress ?? '');
            if (!ptr) { return; }
            const confirmed = await vscode.window.showWarningMessage(
                `Delete label "${item.label}"?`,
                { modal: true },
                'Delete'
            );
            if (confirmed !== 'Delete') { return; }
            ptr.children = ptr.children.filter(c => c.address !== item.address);
        }

        this._save();
        this._onDidChangeTreeData.fire();
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private _findPointer(address: string): UserPointer | undefined {
        const norm = this._normaliseAddress(address).toUpperCase();
        return this._pointers.find(p => p.address.toUpperCase() === norm);
    }

    private _validateHexAddress(v: string): string | undefined {
        const trimmed = v.trim().replace(/^0x/i, '');
        if (!/^[0-9a-fA-F]{1,8}$/.test(trimmed)) {
            return 'Enter a valid hex address (e.g. 0x80100000)';
        }
        return undefined;
    }

    private _normaliseAddress(raw: string): string {
        const num = parseInt(raw.trim().replace(/^0x/i, ''), 16) >>> 0;
        return `0x${num.toString(16).toUpperCase().padStart(8, '0')}`;
    }
}
