[← Back to Documentation Index](README.md)

# HDD Manager — User Guide

CodeDesigner can read and write PS2 Hard Disk Drive images in the raw format used by PCSX2 (typically `HDD.raw`). The `Ps2HddImage` library understands the **APA** (Adaptec Partition Allocation) partition table and the **PFS** (PlayStation File System) used within each partition.

---

## Background

### APA — Partition Table

The PS2 HDD uses a proprietary APA partition table stored at the start of the raw image. Each partition header contains:

- **ID** — internal system identifier (e.g. `__common`, `__system`, `PP.SCUS-97130`)
- **Name** — human-readable display name
- **Start sector** — byte offset = `sector × 512`
- **Length** — in 512-byte sectors
- **Type** — see Partition Types below
- **Created** timestamp, sub-partition count, linked-list next/prev pointers

The first sector (the MBR) is a special header; the linked list of partition headers starts from the first partition after the MBR.

### PFS — PlayStation File System

Each GAME-type partition contains a PFS filesystem. PFS is loosely inode-based:

- **Superblock** at block 1 — records zone size, number of sub-partitions, and locations of the log and root inodes.
- **Inodes** — 1024 bytes each; store file size, timestamps, mode flags, and up to 114 direct block pointers.
- **Block size** — 8192 bytes (16 sectors).

### Partition Types

| TypeName | Hex | Description |
|----------|-----|-------------|
| `MBR` | `0x0001` | Master Boot Record / partition table header |
| `SWAP` | `0x0082` | Linux swap (rare on PS2) |
| `LINUX` | `0x0083` | Linux data partition |
| `GAME` | `0x0100` | PS2 game save / application data (PFS formatted) |
| `MBR-SPECIAL` | `0x1337` | CodeDesigner-specific marker |

### Default System Partitions

A freshly created HDD image always contains these built-in partitions (each 128 MB):

| Partition ID | Purpose |
|-------------|---------|
| `__common` | Shared game data |
| `__net` | Network configuration |
| `__system` | System settings |
| `__sysconf` | System configuration |
| `__contents` | Content catalogue |

Game installs add additional partitions prefixed with `PP.` followed by the disc serial, e.g. `PP.SCUS-97130`.

---

## PCSX2 Setup

PCSX2 requires a `.raw` HDD image in its storage path. To configure it:

1. **Settings → Storage Devices → Hard Drive** — tick "Enable"
2. Point the image path at your `.raw` file (or use `hdd_create` to generate a new one)
3. The image must not be open in another process while PCSX2 is running

> The HDD Manager opens the image with exclusive `FileShare.None` access, so PCSX2 must be closed before using any HDD tool other than read-only operations.

---

## Tools

### `hdd_list` — List all partitions

Opens a PS2 HDD image and lists all APA partitions on it, including the file count for each PFS-formatted partition.

```
hdd_list hddPath="C:\\Users\\tholu\\Desktop\\HDD.raw"
```

Response fields per partition:

| Field | Description |
|-------|-------------|
| `Id` | Internal APA partition ID |
| `Name` | Display name |
| `StartSector` | Starting 512-byte sector |
| `TotalBytes` | Total partition size in bytes |
| `TypeName` | `MBR`, `GAME`, etc. |
| `FileCount` | Number of files in the PFS filesystem |
| `Created` | Creation timestamp (`yyyy-MM-dd HH:mm:ss`) |
| `Files` | Array of file entries (name, size, isDirectory, modified) |

---

### `hdd_list_files` — List files in a partition

Lists all files and directories within a specific PFS partition.

```
hdd_list_files hddPath="C:\\Users\\tholu\\Desktop\\HDD.raw" partitionId="PP.SCUS-97130"
```

Response fields per file:

| Field | Description |
|-------|-------------|
| `Name` | File or directory name |
| `Size` | Size in bytes |
| `IsDirectory` | `true` for subdirectories |
| `Modified` | Last modified timestamp |

---

### `hdd_export` — Export a file from a partition

Extracts a single file from a PFS partition to a directory on the host machine.

```
hdd_export
  hddPath="C:\\Users\\tholu\\Desktop\\HDD.raw"
  partitionId="PP.SCUS-97130"
  fileName="BADATA.DAT"
  exportDir="C:\\Exports\\socom2-saves"
```

The file is written to `exportDir\fileName`. The full path of the exported file is returned in the response `Data` field.

---

### `hdd_import` — Import a file into a partition

> **Not yet implemented.** Direct PFS write support (inode creation, allocation bitmap management, directory entry creation) is pending. Use PCSX2's built-in HDD tools or the `pfsshell` utility for write operations.

---

### `hdd_delete` — Delete a partition

Removes a partition from the APA table. **This operation cannot be undone** — the partition data is not zero-filled but the APA linked list is updated to skip over it.

```
hdd_delete hddPath="C:\\Users\\tholu\\Desktop\\HDD.raw" partitionId="PP.SCUS-97130"
```

---

### `hdd_create` — Create a new HDD image

Creates a new blank PS2 HDD raw image with an initialised APA partition table and the five default system partitions. The image is sparse on NTFS and ext4 so it does not consume its full size on disk immediately.

```
hdd_create hddPath="C:\\Users\\tholu\\Desktop\\HDD.raw" sizeGb=40
```

| Parameter | Default | Description |
|-----------|---------|-------------|
| `hddPath` | required | Full path where the `.raw` file will be created |
| `sizeGb` | `40` | Total image size in gigabytes |

The new image contains the five default system partitions (`__common`, `__net`, `__system`, `__sysconf`, `__contents`), each 128 MB, with minimal PFS superblocks.

---

## Common Workflows

### Inspect an existing PCSX2 HDD image

```
# 1. List all partitions and their file counts
hdd_list hddPath="C:\\path\\to\\HDD.raw"

# 2. Browse files inside a specific game partition
hdd_list_files hddPath="C:\\path\\to\\HDD.raw" partitionId="PP.SCUS-97130"

# 3. Export a save file for inspection
hdd_export
  hddPath="C:\\path\\to\\HDD.raw"
  partitionId="PP.SCUS-97130"
  fileName="BADATA.DAT"
  exportDir="C:\\Temp\\socom2-hdd-export"
```

### Create a fresh HDD image for PCSX2

```
# Create a 40 GB image (default)
hdd_create hddPath="C:\\PCSX2\\memcards\\HDD.raw"

# Point PCSX2 at it: Settings → Storage Devices → Hard Drive → enable → set path
```

### Back up before destructive operations

```
# Always copy the .raw file before hdd_delete operations
Copy-Item "HDD.raw" "HDD.backup.raw"

hdd_delete hddPath="C:\\path\\to\\HDD.raw" partitionId="PP.SLUS-20147"
```

---

## Technical Notes

- **Sector size** — 512 bytes throughout (both APA and PFS).
- **APA header size** — 1024 bytes (2 sectors). The checksum covers the entire header and is recomputed on every write.
- **PFS block size** — 8192 bytes (16 sectors). All inode and data allocations are block-aligned.
- **Sub-partitions** — large partitions span multiple physical APA extents. The APA header contains an array of sub-partition start sectors.
- **Exclusive access** — `Ps2HddImage.Open()` uses `FileShare.None`. PCSX2 must be closed before opening the image.
- **Import limitation** — write operations require updating the PFS inode tree, allocation bitmap, and directory entries. This is pending implementation; use `pfsshell` or PCSX2's HDD utilities for importing files.

---

## VS Code Panel

The VS Code extension includes an HDD overlay panel accessible from the **Disasm Editor**. It provides a graphical interface to the same operations (`hdd_list`, `hdd_list_files`, `hdd_export`) without needing the MCP server or socket commands directly.
