// The module 'vscode' contains the VS Code extensibility API
// Import the module and reference it with the alias vscode in your code below
import * as vscode from 'vscode';
import { DisasmEditorProvider } from './providers/disasmEditor/disasmEditorProvider';
import { DisasmPanelProvider } from './providers/disasmPanel/disasmPanelProvider';
import { AsmPanelProvider } from './providers/asmPanel/asmPanelProvider';

// This method is called when your extension is activated
// Your extension is activated the very first time the command is executed
export function activate(context: vscode.ExtensionContext) {

	// Register custom editor for disassembly
	const editorProvider = new DisasmEditorProvider();
	context.subscriptions.push(
		vscode.window.registerCustomEditorProvider('disasm.disasmEditor', editorProvider)
	);

	// Register webview panels
	const disasmPanelProvider = new DisasmPanelProvider();
	disasmPanelProvider.register(context);

	const asmPanelProvider = new AsmPanelProvider();
	asmPanelProvider.register(context);
}

// This method is called when your extension is deactivated
export function deactivate() {}
