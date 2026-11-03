import { GITHUB_ORG } from '../../services/github.service';

export interface ProjectLink { label: string; url: string; primary?: boolean; download?: boolean; }
export interface ProjectFeature { title: string; text: string; }
export interface ProjectShot { file: string; caption: string; }

/** A project with its own page under /projects/<slug>; other org repos only get a card. */
export interface Project {
  slug: string;
  /** Repository name in the org, used to match the GitHub card. */
  repo: string;
  name: string;
  tagline: string;
  logo: string;
  blurb: string;
  links: ProjectLink[];
  /** Get-started steps; trusted static HTML (<code>, <strong>). */
  steps: string[];
  stepsNote: string;
  features: ProjectFeature[];
  shots: ProjectShot[];
}

const gh = (repo: string) => `https://github.com/${GITHUB_ORG}/${repo}`;

export const PROJECTS: Project[] = [
  {
    slug: 'codedesigner',
    repo: 'CodeDesignerPS2',
    name: 'CodeDesigner',
    tagline: 'PlayStation 2 debugger & toolkit for VS Code',
    logo: 'assets/codedesigner-logo-512.png',
    blurb: `Assemble and disassemble the Emotion Engine (MIPS R5900), debug games live in CodeDesigner's
      PCSX2 fork or the built-in emulator, build and port cheats, edit game assets and run online
      servers. Its MCP server gives AI assistants the same tools, so you and an assistant work in
      one reverse-engineering workflow.`,
    links: [
      { label: 'Download installer', url: `${gh('CodeDesignerPS2')}/releases/latest/download/CodeDesignerInstaller.exe`, primary: true, download: true },
      { label: 'GitHub', url: gh('CodeDesignerPS2') },
      { label: 'Documentation', url: `${gh('CodeDesignerPS2')}/blob/master/docs/README.md` },
    ],
    steps: [
      'Run <code>CodeDesignerInstaller.exe</code> and choose <strong>Install CodeDesigner</strong>, or <strong>Developer setup</strong> to build from source.',
      'Open VS Code and the <strong>CodeDesigner</strong> sidebar.',
      'Pick PCSX2 or the built-in emulator, your BIOS and a game, then click <strong>Run</strong>.',
    ],
    stepsNote: 'Windows 10/11, 64-bit. The installer adds anything missing (.NET, VS Code, SQL Server LocalDB).',
    features: [
      { title: 'Disassembler', text: 'Live views of every memory region with function and table detection, cross-references, breakpoints, watches and registers. Full R5900 ISA including MMI, FPU and VU.' },
      { title: 'Emulators', text: 'Debug commercial games in the PCSX2 fork over PINE (breakpoints, stepping, symbols, save states, pad, picture, sound) or in the built-in emulator.' },
      { title: 'CDS language', text: 'A patch scripting language that compiles to MIPS, GameShark codes or a frame-loop cheat table.' },
      { title: 'Cheats', text: 'Cheat lists and the CheatCodeArchive, format conversion, a porter between game versions and a joker code builder.' },
      { title: 'Memory tools', text: 'Memory Diff, save states, scanners, labels, pointer structs and structure diagrams.' },
      { title: 'Assets', text: 'Viewer plus model, map and animation editors for SOCOM II, with SOCOM: Combined Assault read-only.' },
      { title: 'Network', text: 'DEV9 packet capture with every Medius, DME and SOCOM II message labelled, plus packet building and injection.' },
      { title: 'Online', text: 'SOCOM II online servers (DNAS, Medius, DME, NAT, patch download), verified on real hardware.' },
      { title: 'AI assistants', text: 'An MCP server exposes the toolset to AI clients, with profiles that pick which tools a client sees.' },
    ],
    shots: [
      { file: 'workspace.png', caption: 'The integrated reverse-engineering workspace' },
      { file: 'debugger.png', caption: 'Disassembly and debugging tools inspecting game memory' },
      { file: 'live-labels.png', caption: 'Live memory labels and address annotations' },
      { file: 'label-lists.png', caption: 'Label lists and mapped memory addresses' },
      { file: 'cheat-development.png', caption: 'Cheat development: code and source workflows' },
      { file: 'cheat-list-manager.png', caption: 'Cheat List Manager for the cheat archive' },
      { file: 'cheat-scanner.png', caption: 'Memory scanner for values in a running game' },
      { file: 'cheat-converter.png', caption: 'Cheat converter between code formats' },
      { file: 'cheat-porter.png', caption: 'Cheat Porter between game versions' },
      { file: 'memory-diff.png', caption: 'Memory Diff comparing snapshots' },
      { file: 'memory-diff-results.png', caption: 'Memory Diff results for save states' },
      { file: 'memory-compare.png', caption: 'Memory comparison view' },
      { file: 'packet-debugger.png', caption: 'Packet debugger: capture, dissect and inject' },
      { file: 'socom2-servers.png', caption: 'SOCOM II online server tools' },
      { file: 'socom2-diagnostics.png', caption: 'SOCOM II multiplayer diagnostics' },
      { file: 'memory-card.png', caption: 'Memory card tools' },
      { file: 'hdd.png', caption: 'HDD image tools' },
      { file: 'game-library.png', caption: 'Game library and compatibility' },
      { file: 'ai-control-panel.png', caption: 'AI control panel: memory, skills, profiles' },
      { file: 'ai-workflow.png', caption: 'AI-assisted workflow in the workspace' },
    ],
  },
  {
    slug: 'map-builder',
    repo: 'SocomMapBuilder',
    name: 'SOCOM Map Builder',
    tagline: 'Map, model, texture & animation editors for SOCOM',
    logo: 'assets/mapbuilder-icon.svg',
    blurb: `Open a SOCOM U.S. Navy SEALs II package (.ZDB / .CDB) and edit the map, the props, vehicles,
      characters and weapons on it, their textures and palettes, and the character animations. Every
      page shares one in-memory session, so all edits save together as a copy of the package or as a
      memory-card pack the game loads from mc0:. SOCOM 3 / Combined Assault and GTA: San Andreas
      models can be converted to SOCOM II. Still a work in progress, tested in game with SOCOM II.`,
    links: [
      { label: 'GitHub', url: gh('SocomMapBuilder'), primary: true },
      { label: 'Format documentation', url: `${gh('SocomMapBuilder')}/blob/master/docs/formats/README.md` },
    ],
    steps: [
      'Install the <strong>.NET 8 SDK</strong> and <strong>Node.js 20</strong> or later, and clone the repository.',
      'Build the page: <code>cd web</code>, <code>npm ci</code>, <code>npm run build</code>.',
      'Start the server with <code>cd server</code>, <code>dotnet run</code>, open <code>http://127.0.0.1:8770</code> and choose <strong>File › Open package…</strong>',
    ],
    stepsNote: 'Ships no game files: use your own SOCOM II packages, and edit copies.',
    features: [
      { title: 'Map Builder', text: 'The composed map with textures and baked lighting. Move, rotate, scale, duplicate and delete props and clutter, place clutter on any surface, and move AIMAPS spawn and waypoint points.' },
      { title: 'Model Editor', text: 'Props, vehicles, weapons and skinned characters. Edit vertices, faces and UVs, paint vertex colours, add meshes or an OBJ, and export OBJ.' },
      { title: 'Textures & palettes', text: 'Retexture sections, replace or create textures from PNG/JPG/BMP/WebP, build palettes from images and import textures from other packages.' },
      { title: 'Animation Editor', text: 'Create, duplicate, rename and retime character clips and key bones on any skinned character; weapons mount and fire their muzzle effects.' },
      { title: 'Asset Viewer', text: 'A file manager for .ZDB / .CDB / .ZAR / .ZED archives with thumbnails and previews of models, textures, palettes, GEO scenes, RDR screens and AI maps.' },
      { title: 'Cross-game import', text: 'Import models from other packages, and convert SOCOM 3 / Combined Assault and GTA: San Andreas models to SOCOM II.' },
    ],
    shots: [
      { file: 'map-builder/map.png', caption: 'Map Builder: the composed map with props and clutter' },
      { file: 'map-builder/character.png', caption: 'Model Editor: a skinned character' },
      { file: 'map-builder/weapon.png', caption: 'Model Editor: a weapon' },
      { file: 'map-builder/anim.png', caption: 'Animation Editor' },
      { file: 'map-builder/asset.png', caption: 'Asset Viewer' },
      { file: 'map-builder/raygun.png', caption: 'A custom weapon' },
      { file: 'map-builder/hummer.png', caption: 'Combined Assault Humvee in SOCOM II' },
      { file: 'map-builder/tank.png', caption: 'A tank model' },
      { file: 'map-builder/ca_char.png', caption: 'Combined Assault character' },
      { file: 'map-builder/ca_map.png', caption: 'Combined Assault map' },
      { file: 'map-builder/gta_cj.png', caption: 'GTA: San Andreas character' },
    ],
  },
];

export function projectBySlug(slug: string | null): Project | undefined {
  return PROJECTS.find(p => p.slug === slug);
}

export function projectByRepo(repo: string): Project | undefined {
  return PROJECTS.find(p => p.repo === repo);
}
