# CodeDesigner Skills

[VS Code Agent Skills](https://code.visualstudio.com/docs/copilot/customization/agent-skills)
that drive the CodeDesigner MCP server (`CodeDesigner.AI`) through high-value
reverse-engineering workflows. Each folder contains a `SKILL.md` plus any
supporting files; invoke via `/skill-name [args]` in VS Code's Copilot chat
(also works with GitHub Copilot CLI and the Copilot cloud agent).

The skills assume `CodeDesigner.AI` is the active MCP server (see
`.vscode/mcp.json`). They sequence existing MCP tools — no client-side code.

| Skill | What it does | Profile recommended |
|---|---|---|
| [`cd-session-bootstrap`](./cd-session-bootstrap/SKILL.md) | Start every session right: init, set a goal, list tools, suggest next. | any |
| [`cd-reverse-function`](./cd-reverse-function/SKILL.md) | Reverse one MIPS function: disasm window + xrefs + label + save. | `re` |
| [`cd-packet-flow`](./cd-packet-flow/SKILL.md) | SOCOM/Medius/DNAS packet reversing end-to-end. | `packets` |
| [`cd-cheat-port`](./cd-cheat-port/SKILL.md) | Port GameShark/AR cheats between game revisions. | `patching` |
| [`cd-cds-patch`](./cd-cds-patch/SKILL.md) | Author + validate + compile + apply a CDS patch atomically. | `patching` |
| [`cd-multi-agent-session`](./cd-multi-agent-session/SKILL.md) | Lead role: dispatch + monitor + message sub-agents. | `core` |

To add a new skill, create a directory whose name matches the skill `name`
in the SKILL.md frontmatter and follow the existing format. Skills can also be
installed personally under `~/.copilot/skills/`, `~/.claude/skills/`, or
`~/.agents/skills/`.
