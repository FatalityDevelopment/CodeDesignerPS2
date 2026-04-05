import * as vscode from 'vscode';
import * as fs from 'fs';
import * as path from 'path';
import { getOutputChannel, getSocketClient } from '../../extension';
import { LabelEntry, StructEntry } from '../../socketClient';

// ── Data model ────────────────────────────────────────────────────────────────

export interface PointerChildLabel {
    /** Hex offset from the parent pointer's dereferenced value (e.g. "0x00000010"). */
    offset: string;
    name: string;
    comment?: string;
    /** When present, this child is itself a pointer; its entries are offsets from
     *  the value read at this child's resolved address. */
    children?: PointerChildLabel[];
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
        /** 'pointer'      – top-level user-defined pointer
         *  'child'        – leaf offset label (no nested children)
         *  'childPointer' – an offset label that is itself a pointer (has nested children) */
        public readonly itemType: 'pointer' | 'child' | 'childPointer',
        /** Resolved live address when known; otherwise the raw hex offset string. */
        public readonly address: string,
        label: string,
        collapsibleState: vscode.TreeItemCollapsibleState,
        /** Always the address of the root UserPointer this node belongs to. */
        public readonly rootPointerAddress?: string,
        comment?: string,
        /** Ordered array of hex offsets leading from the root pointer to this node.
         *  Empty for top-level pointer nodes. */
        public readonly nodePath: string[] = [],
    ) {
        super(label, collapsibleState);
        this.description = address;

        // Build a richer tooltip that includes the offset when relevant.
        const offset = nodePath.length > 0 ? nodePath[nodePath.length - 1] : undefined;
        const offsetSuffix = offset ? `  (offset ${offset})` : '';
        this.tooltip = comment
            ? `${address}${offsetSuffix}  —  ${comment}`
            : `${address}${offsetSuffix}`;

        if (itemType === 'pointer') {
            this.iconPath = new vscode.ThemeIcon('symbol-variable');
            this.contextValue = 'pointerNode';
        } else if (itemType === 'childPointer') {
            this.iconPath = new vscode.ThemeIcon('symbol-variable');
            this.contextValue = 'childPointerNode';
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

    /**
     * Server-side structs pushed from DisasmEditorProvider via onStructsChanged.
     * When non-empty these are shown INSTEAD of local _pointers (server is source-of-truth).
     */
    private _serverStructs: StructEntry[] = [];

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
            this._pointers = Array.isArray(doc.pointers)
                ? doc.pointers.map(p => ({ ...p, children: this._migrateChildren(p.children ?? []) }))
                : [];
        } catch (err: any) {
            getOutputChannel().warn(`PointerManager: failed to load ${this._storagePath}: ${err?.message ?? err}`);
            this._pointers = [];
        }
    }

    /** Recursively migrate children that use the legacy `address` field to use `offset`. */
    private _migrateChildren(children: any[]): PointerChildLabel[] {
        return children.map(c => {
            const migrated: PointerChildLabel = {
                offset: c.offset ?? c.address ?? '0x00000000',
                name:   c.name ?? '',
                comment: c.comment,
            };
            // Preserve `children` array even when empty — its presence marks this
            // node as a sub-pointer rather than a plain leaf label.
            if (Array.isArray(c.children)) {
                migrated.children = this._migrateChildren(c.children);
            }
            return migrated;
        });
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

    async getChildren(element?: PointerTreeItem): Promise<PointerTreeItem[]> {
        if (!element) {
            // Root level — prefer server structs when available (source-of-truth when connected).
            if (this._serverStructs.length > 0) {
                return this._serverStructs.map(s =>
                    new PointerTreeItem(
                        'pointer',
                        s.baseAddress,
                        s.name,
                        s.members.length > 0
                            ? vscode.TreeItemCollapsibleState.Collapsed
                            : vscode.TreeItemCollapsibleState.None,
                        s.baseAddress,
                        undefined,
                        []
                    )
                );
            }
            // Fallback — local pointers from pointerMap.json.
            return this._pointers.map(p =>
                new PointerTreeItem(
                    'pointer',
                    p.address,
                    p.name,
                    p.children.length > 0
                        ? vscode.TreeItemCollapsibleState.Collapsed
                        : vscode.TreeItemCollapsibleState.None,
                    p.address,
                    p.comment,
                    []
                )
            );
        }

        // Children of a server struct item — show members.
        if (element.itemType === 'pointer' && this._serverStructs.length > 0) {
            const serverStruct = this._serverStructs.find(
                s => s.baseAddress.toLowerCase() === (element.rootPointerAddress ?? element.address).toLowerCase()
            );
            if (serverStruct) {
                return serverStruct.members.map(m => {
                    const offsetStr = `+0x${m.offset.toString(16).toUpperCase().padStart(4, '0')}`;
                    return new PointerTreeItem(
                        'child',
                        offsetStr,
                        m.name,
                        vscode.TreeItemCollapsibleState.None,
                        serverStruct.baseAddress,
                        undefined,
                        [offsetStr]
                    );
                });
            }
        }

        if (element.itemType === 'pointer' || element.itemType === 'childPointer') {
            const rootAddr = element.rootPointerAddress ?? element.address;
            const ptr = this._findPointer(rootAddr);
            if (!ptr) { return []; }

            // For a top-level pointer, use ptr.children; for a childPointer, navigate via nodePath.
            const modelChildren: PointerChildLabel[] =
                element.itemType === 'pointer'
                    ? ptr.children
                    : (this._findNodeByPath(ptr, element.nodePath)?.children ?? []);

            if (modelChildren.length === 0) { return []; }

            // Read the live pointer value from memory to resolve child addresses.
            const ptrValue = await this._resolvePointerValue(element.address);

            return modelChildren.map(c => {
                const offsetNum = this._parseOffset(c.offset);
                let resolvedAddr: string | undefined;
                if (ptrValue !== undefined && offsetNum !== undefined) {
                    resolvedAddr = this._normaliseAddress(`0x${((ptrValue + offsetNum) >>> 0).toString(16)}`);
                }

                // A node is a sub-pointer when its `children` array exists (even if
                // currently empty) — this was set at creation time via the "Pointer" choice.
                const isSubPointer = c.children !== undefined;
                const childPath = [...element.nodePath, c.offset];

                return new PointerTreeItem(
                    isSubPointer ? 'childPointer' : 'child',
                    resolvedAddr ?? c.offset,
                    c.name,
                    isSubPointer
                        ? vscode.TreeItemCollapsibleState.Collapsed
                        : vscode.TreeItemCollapsibleState.None,
                    rootAddr,
                    c.comment,
                    childPath
                );
            });
        }

        return [];
    }

    // ── Public mutators ───────────────────────────────────────────────────────

    /** Refresh the tree (e.g. after the storage file changes externally). */
    refresh(): void {
        this._load();
        this._onDidChangeTreeData.fire();
    }

    /**
     * Called by DisasmEditorProvider whenever the active label set changes on the server.
     * Struct-like entries (those with members) are displayed in the STRUCTS panel.
     * Passing an empty array clears server structs and falls back to local pointers.
     */
    setServerStructs(structs: StructEntry[]): void {
        this._serverStructs = structs;
        this._onDidChangeTreeData.fire();
    }

    /** Add a new user-defined pointer. */
    async addPointer(): Promise<void> {
        const addressRaw = await vscode.window.showInputBox({
            title: 'Add Struct — Address',
            prompt: 'Enter the virtual address of the struct pointer (hex)',
            placeHolder: '0x80100000',
            ignoreFocusOut: true,
            validateInput: v => this._validateHexAddress(v),
        });
        if (addressRaw === undefined) { return; }

        const name = await vscode.window.showInputBox({
            title: 'Add Struct — Name',
            prompt: 'Enter a name for this struct',
            placeHolder: 'e.g. PlayerStruct',
            ignoreFocusOut: true,
            validateInput: v => v.trim() ? undefined : 'Name cannot be empty',
        });
        if (name === undefined) { return; }

        const comment = await vscode.window.showInputBox({
            title: 'Add Struct — Comment (optional)',
            prompt: 'Enter an optional comment',
            placeHolder: 'Leave blank to skip',
            ignoreFocusOut: true,
        });
        if (comment === undefined) { return; }

        const address = this._normaliseAddress(addressRaw);
        const existing = this._findPointer(address);
        if (existing) {
            vscode.window.showWarningMessage(`A struct already exists at ${address}.`);
            return;
        }

        this._pointers.push({ address, name: name.trim(), comment: comment.trim() || undefined, children: [] });
        this._save();
        this._onDidChangeTreeData.fire();
    }

    /** Add a child label (or sub-pointer label) under the given tree item.
     *  Works for top-level pointer nodes, childPointer nodes, and leaf child nodes
     *  (the last case promotes the child into a sub-pointer). */
    async addChildLabel(parentItem: PointerTreeItem): Promise<void> {
        const rootAddr = parentItem.itemType === 'pointer'
            ? parentItem.address
            : (parentItem.rootPointerAddress ?? '');
        const ptr = this._findPointer(rootAddr);
        if (!ptr) {
            vscode.window.showErrorMessage(`Pointer at ${rootAddr} not found.`);
            return;
        }

        const parentLabel = typeof parentItem.label === 'string' ? parentItem.label : ptr.name;

        // ── Step 1: ask whether this child is a plain label or a pointer itself ──
        const kindPick = await vscode.window.showQuickPick(
            [
                { label: '$(symbol-field)  Label',   description: 'A plain value at an offset (leaf)',           itemKind: 'label'   as const },
                { label: '$(symbol-variable)  Pointer', description: 'A pointer at an offset — can have its own children', itemKind: 'pointer' as const },
            ],
            {
                title: `Add Child under "${parentLabel}" — Type`,
                placeHolder: 'Is this child a plain label or a pointer?',
                ignoreFocusOut: true,
            }
        );
        if (kindPick === undefined) { return; }
        const isPointer = kindPick.itemKind === 'pointer';

        // ── Step 2: offset ────────────────────────────────────────────────────
        const offsetRaw = await vscode.window.showInputBox({
            title: `Add ${isPointer ? 'Pointer' : 'Label'} under "${parentLabel}" — Offset`,
            prompt: 'Enter the byte offset from the pointer value (hex)',
            placeHolder: '0x10',
            ignoreFocusOut: true,
            validateInput: v => this._validateHexOffset(v),
        });
        if (offsetRaw === undefined) { return; }

        // ── Step 3: name ─────────────────────────────────────────────────────
        const name = await vscode.window.showInputBox({
            title: `Add ${isPointer ? 'Pointer' : 'Label'} under "${parentLabel}" — Name`,
            prompt: `Enter a name for this ${isPointer ? 'pointer' : 'label'}`,
            placeHolder: isPointer ? 'e.g. SubStruct' : 'e.g. health',
            ignoreFocusOut: true,
            validateInput: v => v.trim() ? undefined : 'Name cannot be empty',
        });
        if (name === undefined) { return; }

        // ── Step 4: comment ───────────────────────────────────────────────────
        const comment = await vscode.window.showInputBox({
            title: `Add ${isPointer ? 'Pointer' : 'Label'} under "${parentLabel}" — Comment (optional)`,
            prompt: 'Enter an optional comment',
            placeHolder: 'Leave blank to skip',
            ignoreFocusOut: true,
        });
        if (comment === undefined) { return; }

        const offset = this._normaliseOffset(offsetRaw);

        // Locate the correct children array in the data model.
        let targetChildren: PointerChildLabel[];
        if (parentItem.itemType === 'pointer') {
            targetChildren = ptr.children;
        } else {
            // child or childPointer — navigate to the node and use/create its children array.
            const parentNode = this._findNodeByPath(ptr, parentItem.nodePath);
            if (!parentNode) {
                vscode.window.showErrorMessage('Could not locate the parent node.');
                return;
            }
            if (!parentNode.children) { parentNode.children = []; }
            targetChildren = parentNode.children;
        }

        if (targetChildren.some(c => c.offset === offset)) {
            vscode.window.showWarningMessage(`A label at offset ${offset} already exists under "${parentLabel}".`);
            return;
        }

        const newChild: PointerChildLabel = {
            offset,
            name: name.trim(),
            comment: comment.trim() || undefined,
            // `children: []` marks this node as a sub-pointer even before any children are added.
            ...(isPointer ? { children: [] } : {}),
        };
        targetChildren.push(newChild);
        this._save();
        // Fire the change so getChildren re-runs and resolves live addresses.
        this._onDidChangeTreeData.fire();
    }

    /** Edit the name/comment of an existing pointer, child label, or child-pointer node. */
    async editNode(item: PointerTreeItem): Promise<void> {
        if (item.itemType === 'pointer') {
            const ptr = this._findPointer(item.address);
            if (!ptr) { return; }

            const newName = await vscode.window.showInputBox({
                title: `Edit Struct "${ptr.name}"`,
                prompt: 'New name',
                value: ptr.name,
                ignoreFocusOut: true,
                validateInput: v => v.trim() ? undefined : 'Name cannot be empty',
            });
            if (newName === undefined) { return; }

            const newComment = await vscode.window.showInputBox({
                title: `Edit Struct "${ptr.name}" — Comment`,
                prompt: 'New comment (leave blank to remove)',
                value: ptr.comment ?? '',
                ignoreFocusOut: true,
            });
            if (newComment === undefined) { return; }

            ptr.name    = newName.trim();
            ptr.comment = newComment.trim() || undefined;
        } else {
            const rootAddr = item.rootPointerAddress ?? '';
            const ptr = this._findPointer(rootAddr);
            const node = ptr ? this._findNodeByPath(ptr, item.nodePath) : undefined;
            if (!node) { return; }

            const newName = await vscode.window.showInputBox({
                title: `Edit Label "${node.name}"`,
                prompt: 'New name',
                value: node.name,
                ignoreFocusOut: true,
                validateInput: v => v.trim() ? undefined : 'Name cannot be empty',
            });
            if (newName === undefined) { return; }

            const newComment = await vscode.window.showInputBox({
                title: `Edit Label "${node.name}" — Comment`,
                prompt: 'New comment (leave blank to remove)',
                value: node.comment ?? '',
                ignoreFocusOut: true,
            });
            if (newComment === undefined) { return; }

            node.name    = newName.trim();
            node.comment = newComment.trim() || undefined;
        }

        this._save();
        this._onDidChangeTreeData.fire();
    }

    /** Delete a pointer (and all its descendants) or any child/childPointer node. */
    async deleteNode(item: PointerTreeItem): Promise<void> {
        if (item.itemType === 'pointer') {
            const ptr = this._findPointer(item.address);
            const childCount = ptr?.children.length ?? 0;
            const confirmed = await vscode.window.showWarningMessage(
                `Delete struct "${item.label}"${childCount > 0 ? ` and its ${childCount} child label(s)` : ''}?`,
                { modal: true },
                'Delete'
            );
            if (confirmed !== 'Delete') { return; }
            this._pointers = this._pointers.filter(p => p.address !== item.address);
        } else {
            const rootAddr = item.rootPointerAddress ?? '';
            const ptr = this._findPointer(rootAddr);
            if (!ptr) { return; }

            // For a childPointer that has nested children, warn about the cascade.
            const nodeToDelete = this._findNodeByPath(ptr, item.nodePath);
            const subChildCount = nodeToDelete?.children?.length ?? 0;
            const labelKind = item.itemType === 'childPointer' ? 'pointer' : 'label';
            const confirmed = await vscode.window.showWarningMessage(
                `Delete ${labelKind} "${item.label}"${subChildCount > 0 ? ` and its ${subChildCount} child label(s)` : ''}?`,
                { modal: true },
                'Delete'
            );
            if (confirmed !== 'Delete') { return; }

            // Navigate to the parent's children array and remove by offset.
            const parentPath  = item.nodePath.slice(0, -1);
            const leafOffset  = item.nodePath[item.nodePath.length - 1];

            if (parentPath.length === 0) {
                ptr.children = ptr.children.filter(c => c.offset !== leafOffset);
            } else {
                const parentNode = this._findNodeByPath(ptr, parentPath);
                if (!parentNode?.children) { return; }
                parentNode.children = parentNode.children.filter(c => c.offset !== leafOffset);
                // Do NOT delete an empty children array — its presence is what marks
                // the parent node as a childPointer rather than a plain leaf label.
            }
        }

        this._save();
        this._onDidChangeTreeData.fire();
    }

    /**
     * Resolve all user-defined pointer labels to their live addresses and return
     * a flat LabelEntry list suitable for display in the instruction view.
     *
     * Top-level pointers are included at their fixed storage address.  Child
     * labels are resolved by reading the live pointer value from emulator memory
     * and adding the configured offset.  Sub-pointers are resolved recursively.
     * Entries whose addresses cannot be resolved are silently omitted.
     */
    public async getResolvedLabels(): Promise<LabelEntry[]> {
        const labels: LabelEntry[] = [];
        for (const ptr of this._pointers) {
            labels.push({
                address: this._normaliseAddress(ptr.address),
                label:   ptr.name,
                comment: ptr.comment,
            });
            if (ptr.children.length > 0) {
                const ptrValue = await this._resolvePointerValue(ptr.address);
                if (ptrValue !== undefined) {
                    await this._collectChildLabels(ptrValue, ptr.children, labels);
                }
            }
        }
        return labels;
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    /** Recursively resolve child labels and append them to `out`. */
    private async _collectChildLabels(
        parentValue: number,
        children: PointerChildLabel[],
        out: LabelEntry[]
    ): Promise<void> {
        for (const child of children) {
            const offsetNum = this._parseOffset(child.offset);
            if (offsetNum === undefined) { continue; }
            const resolvedAddr = (parentValue + offsetNum) >>> 0;
            const addrStr = this._normaliseAddress(`0x${resolvedAddr.toString(16)}`);
            out.push({ address: addrStr, label: child.name, comment: child.comment });
            if (child.children && child.children.length > 0) {
                const childValue = await this._resolvePointerValue(addrStr);
                if (childValue !== undefined) {
                    await this._collectChildLabels(childValue, child.children, out);
                }
            }
        }
    }

    private _findPointer(address: string): UserPointer | undefined {
        const norm = this._normaliseAddress(address).toUpperCase();
        return this._pointers.find(p => p.address.toUpperCase() === norm);
    }

    /**
     * Walk the nested PointerChildLabel tree following `path` (array of hex offset strings).
     * Returns the node at the end of the path, or undefined if any step is missing.
     */
    private _findNodeByPath(ptr: UserPointer, path: string[]): PointerChildLabel | undefined {
        let children: PointerChildLabel[] = ptr.children;
        let node: PointerChildLabel | undefined;
        for (const offset of path) {
            node = children.find(c => c.offset === offset);
            if (!node) { return undefined; }
            children = node.children ?? [];
        }
        return node;
    }

    /**
     * Read a 32-bit little-endian pointer value from live emulator memory.
     * Returns undefined when the emulator is not connected or the read fails.
     */
    private async _resolvePointerValue(address: string): Promise<number | undefined> {
        const client = getSocketClient();
        if (!client) { return undefined; }
        try {
            const addr = parseInt(address.replace(/^0x/i, ''), 16) >>> 0;
            const bytes = await client.readMemory(addr, 4, 'ee');
            if (bytes.length < 4) { return undefined; }
            return bytes.readUInt32LE(0) >>> 0;
        } catch {
            return undefined;
        }
    }

    private _validateHexAddress(v: string): string | undefined {
        const trimmed = v.trim().replace(/^0x/i, '');
        if (!/^[0-9a-fA-F]{1,8}$/.test(trimmed)) {
            return 'Enter a valid hex address (e.g. 0x80100000)';
        }
        return undefined;
    }

    private _validateHexOffset(v: string): string | undefined {
        const trimmed = v.trim().replace(/^0x/i, '');
        if (!/^[0-9a-fA-F]{1,8}$/.test(trimmed)) {
            return 'Enter a valid hex offset (e.g. 0x10 or 10)';
        }
        return undefined;
    }

    private _normaliseAddress(raw: string): string {
        return this._normaliseHex(raw);
    }

    private _normaliseOffset(raw: string): string {
        return this._normaliseHex(raw);
    }

    private _normaliseHex(raw: string): string {
        const num = parseInt(raw.trim().replace(/^0x/i, ''), 16) >>> 0;
        return `0x${num.toString(16).toUpperCase().padStart(8, '0')}`;
    }

    private _parseOffset(offset: string): number | undefined {
        const trimmed = offset.trim().replace(/^0x/i, '');
        if (!/^[0-9a-fA-F]+$/.test(trimmed)) { return undefined; }
        return parseInt(trimmed, 16) >>> 0;
    }
}
