[← Back to Documentation Index](README.md)

# Memory Card Manager

`CodeDesigner.Ps2.MCManager` reads and writes PS2 memory card images, with the same core operations as mymc: list saves, export, import, delete, read a file, and create a blank card. It handles both card types PCSX2 uses:

- **File cards** — a single image (`.ps2`, 8 MB by default), opened with `Ps2MemoryCard`.
- **Folder cards** — a directory with one sub-folder per save (PCSX2's "folder memory card"), read with `FolderMemoryCard`. PCSX2's `_pcsx2_*` bookkeeping files are ignored.

---

## In VS Code

**Emulator → Tools → Mem Card** opens the Memory Card browser:

- Open a card, or create a **new** one.
- The save list shows each save's animated 3D icon (parsed from `icon.sys` and the icon files, rendered with WebGL), name, size and date. Pick an icon slot (normal, copy, delete) to preview.
- **Import** a save folder, **Export** the selected save, **Delete** it, and view its files.

The browser reads and writes through the Bridge.

---

## From the Bridge and MCP

| MCP tool | Does |
|---|---|
| `mc_list` | List the saves on a card |
| `mc_read_file` | Read one file from a save |
| `mc_export` | Export a save to a host folder |
| `mc_import` | Import a host folder as a save |
| `mc_delete` | Delete a save |
| `mc_format` | Create a blank card |

---

## Library

```csharp
using CodeDesigner.Ps2.MCManager;

Ps2MemoryCard.Create("blank.ps2");                     // 8 MB by default

using var card = Ps2MemoryCard.Open("Mcd001.ps2");
foreach (var save in card.ListSaves())
    Console.WriteLine(save.Name);

card.ExportSave("BASCUS-97275SOCOM2", @"C:\saves");     // writes every file of the save
card.ImportSave(@"C:\saves\BASCUS-97275SOCOM2");        // save name = folder name
byte[] icon = card.ReadFile("BASCUS-97275SOCOM2", "icon.sys");
card.DeleteSave("BASCUS-97275SOCOM2");
```

`Open(path, readOnly: true)` opens a card without write access. For folder cards use the static `FolderMemoryCard.ListSaves(dir)`, `ExportSave(dir, name, outputDir)` and `ReadFile(dir, name, file)`.

---

## Tests

`CodeDesigner.Ps2.TestProject/MemoryCardManager/`.
