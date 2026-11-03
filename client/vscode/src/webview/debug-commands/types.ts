/** One entry of Mods/debug-commands.json. */
export interface DebugCommand {
    id: string;
    /** Section heading on the Debug page, e.g. "SOCOM II · Bots". */
    group: string;
    name: string;
    description?: string;
    /** Program that runs the script, e.g. "python". Empty for plain shell commands. */
    interpreter?: string;
    /** Script path, relative to the working directory (normally the Mods folder). Empty for plain commands. */
    script?: string;
    /** Script arguments, or the whole command line when there is no script. */
    args?: string;
    /** Working directory relative to the Mods folder ("" = Mods, ".." = repo root). */
    cwd?: string;
    /** Help: a Markdown file relative to the Mods folder (optionally `#heading`), opened in the preview by the
     *  card's help button. No button when empty. */
    readme?: string;
}

export interface DebugCommandsFile {
    version: number;
    commands: DebugCommand[];
}

/** The command line sent to the terminal: `interpreter "script" args`, or just args. */
export function buildCommandLine(cmd: DebugCommand): string {
    const script = (cmd.script ?? '').trim();
    const args = (cmd.args ?? '').trim();
    if (!script) { return args; }
    const quoted = /\s/.test(script) ? `"${script}"` : script;
    return [cmd.interpreter?.trim(), quoted, args].filter(Boolean).join(' ');
}
