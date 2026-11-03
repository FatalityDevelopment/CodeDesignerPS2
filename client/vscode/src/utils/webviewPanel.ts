import * as vscode from 'vscode';

/**
 * Lucide icon assets shared by the disassembler sidebar buttons and the
 * corresponding VS Code webview tab icons. Each file is generated from the same
 * SVG path used by `IconComponent`, so tabs stay visually aligned with the
 * buttons that opened them.
 */
export type CodeDesignerPanelIcon =
    | 'bot'
    | 'bug'
    | 'camera'
    | 'database'
    | 'disc3'
    | 'gamepad2'
    | 'gitcompare'
    | 'harddrive'
    | 'memorystick'
    | 'monitor'
    | 'radiotower'
    | 'repeat'
    | 'save'
    | 'search'
    | 'sparkles'
    | 'tag'
    | 'workflow';

/** Default CPU icon used by disassembler/editor tabs that do not map to a sidebar button. */
const DEFAULT_ICON_FILE = 'disasm-icon.svg';

/**
 * Apply a CodeDesigner tab icon. Pass the sidebar button's `app-icon` name when
 * the webview is opened from a disassembler-panel button.
 */
export function applyCodeDesignerPanelIcon(
    panel: vscode.WebviewPanel,
    extensionUri: vscode.Uri,
    iconName?: CodeDesignerPanelIcon,
): void {
    const fileName = iconName ? `panel-icon-${iconName}.svg` : DEFAULT_ICON_FILE;
    panel.iconPath = vscode.Uri.joinPath(extensionUri, 'dist', 'resources', fileName);
}

/** The Memory Region button icons vary by region; keep this in sync with the sidebar menu. */
export function getMemoryRegionPanelIcon(label: string): CodeDesignerPanelIcon {
    if (label === 'BIOS ROM' || label.startsWith('ROM')) {
        return 'disc3';
    }
    if (label === 'Scratchpad') {
        return 'harddrive';
    }
    return 'memorystick';
}
