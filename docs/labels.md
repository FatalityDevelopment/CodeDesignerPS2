[← Back to Documentation Index](README.md)

# Labels — User Guide

Labels are CodeDesigner's persistent annotation system for PS2 memory addresses. They map virtual EE addresses to human-readable names, types, and structured metadata. Every label is stored in SQL Server, survives across sessions, and feeds directly into the disassembly view, CDS scripts, and AI agent workflows.

---

## Concepts

### Labels vs. Label Sets

A **label** is a single annotation: an address, a name, a type, and optional metadata.

A **label set** is a named collection of labels scoped to a specific game disc (identified by serial, e.g. `SCUS-97130`). One disc can have many sets:

- **Built-in set** — ships with CodeDesigner; contains known PS2 SDK functions and system addresses. Treated as read-only.
- **User sets** — created freely (e.g. `ai-analysis`, `functions-pass1`, `socom2-structs`). Always use a dedicated set rather than writing into the built-in set.

At runtime the disassembler **merges** the built-in set with whichever user set is currently active, so both appear in the disassembly view simultaneously.

### Scoping

Labels are scoped to the exact disc release, not just the game title. `DbLabelRepository` resolves a game identifier in this order:

1. Numeric `GameDisc.Id`
2. PS2 serial (`SCUS-97130` or `SCUS_97130` — normalised automatically)
3. ISO path suffix
4. `Game.Title` exact match → first disc for that game

### Address Format

Root labels use a real PS2 EE virtual address, e.g. `0x001C8A00`.

Child labels (fields within a pointer or struct) use a **synthetic path key** that encodes the parent address and the byte offset: `0x001C8A00:+0x0010`. The actual runtime address is resolved by following the parent pointer chain at query time.

---

## Label Types

Each label has a `type` field that controls how it is displayed in the disassembly view, what extra metadata is relevant, and how the live value is rendered when `dynamic = true`.

| Type | Value | Description |
|------|-------|-------------|
| `Address` | 0 | Plain address marker. The name appears at that address in the disassembly. Default when no type is given. |
| `Struct` | 1 | The address holds a 32-bit pointer; the dereferenced value is the struct base. Child entries describe named byte-offset fields within the struct. |
| `Function` | 2 | Function entry point. Shown as `FNC_Name` in disassembly. Supports `argRegs` and `returnRegs` to annotate the calling convention. |
| `Array` | 3 | Pointer to a contiguous array. Expanded to `Name[0]`, `Name[1]`, … using `elementStride` and `elementCount`. |
| `ArrayOfPointers` | 4 | Pointer to an array of pointers. The base address is labelled; individual element pointers are not expanded statically. |
| `LinkedList` | 5 | First node of a linked list. Runtime traversal via `label_resolve_linked_list` walks the chain using `nodeNextOffset` and `nodeSize`, assigning indexed labels to each node. |
| `String` | 6 | Pointer to a null-terminated ASCII string. Runtime resolution via `label_resolve_strings` reads the content from live PCSX2 memory and displays it alongside the label. |
| `Pointer` | 7 | The address holds a 32-bit pointer whose dereferenced value is a base address. Child entries hang off that base address using `offset`. Live display shows the current dereferenced value. |
| `Float` | 8 | Single-precision IEEE-754 float. Live display renders the 32-bit memory content as a decimal float. |
| `Value` | 9 | Generic integer. Live display renders the 32-bit content as an unsigned decimal. |

---

## Label Fields

| Field | Type | Description |
|-------|------|-------------|
| `address` | string | Virtual EE address as hex (`0x001C8A00`) or decimal. For children, the synthetic path key. |
| `name` | string | Human-readable identifier used in the disassembly view and CDS source. |
| `comment` | string? | Optional tooltip/description. |
| `type` | LabelType | One of the types above. Defaults to `Address`. |
| `valueType` | string? | Data-type annotation: `u8`, `u16`, `u32`, `u64`, `s8`, `s16`, `s32`, `s64`, `float`, `double`, `pointer`. |
| `dynamic` | bool | When `true`, the live memory value at this address is polled and shown in the label manager table. |
| `hidden` | bool | When `true`, the entry is hidden from the disassembler and structs panels. |
| `argRegs` | string[]? | For `Function`: named argument registers, e.g. `["a0=playerPtr", "a1=flags"]`. |
| `returnRegs` | string[]? | For `Function`: named return registers, e.g. `["v0=result"]`. |
| `elementStride` | int? | For `Array`: byte stride between elements. |
| `elementCount` | int? | For `Array`: total number of elements (omit if unknown). |
| `nodeNextOffset` | int? | For `LinkedList`: byte offset within each node where the next-node pointer lives. |
| `nodeSize` | int? | For `LinkedList`: total size of one node in bytes (informational). |
| `parentAddress` | string? | Address of the parent label. Set automatically for child entries. |
| `offset` | int? | Byte offset from the parent pointer's dereferenced value. Set automatically for child entries. |

---

## Label Set Management

### Listing sets

```
label_list_sets gameId="SCUS-97130"
→ Built-in
→ ai-analysis
→ functions-pass1
```

### Creating a set

```
label_create_set gameId="SCUS-97130" setName="socom2-structs"
```

Always create a dedicated set per analysis task. Never write into the built-in set.

### Activating a set

```
label_set_active gameId="SCUS-97130" setName="socom2-structs"
```

The disassembler merges the active user set with the built-in set. Pass `"Built-in"` to clear any active user set.

### Deleting a set

```
label_delete_set gameId="SCUS-97130" setName="old-pass"
```

This permanently removes the set and all its entries. The built-in set cannot be deleted.

---

## Working with Labels

### Adding a single label

```
label_add
  gameId="SCUS-97130"
  labelSet="socom2-structs"
  address="0x001C8A00"
  name="ApplyDamage"
  type="Function"
  comment="Subtracts damage from target health. Called from combat loop."
  argRegs="a0=targetPtr,a1=damageAmount"
  returnRegs="v0=newHealth"
```

### Adding many labels at once

`label_add_batch` accepts a JSON array and is far more efficient than calling `label_add` repeatedly:

```json
[
  { "address": "0x001C8A00", "name": "ApplyDamage",    "type": "Function", "argRegs": "a0=targetPtr,a1=damageAmount" },
  { "address": "0x001A3F28", "name": "PlayerUpdate",   "type": "Function" },
  { "address": "0x00352F4C", "name": "player_health",  "type": "Value",    "valueType": "s32", "dynamic": true },
  { "address": "0x00352F50", "name": "player_armor",   "type": "Value",    "valueType": "s32", "dynamic": true },
  { "address": "0x00352F54", "name": "player_ammo",    "type": "Value",    "valueType": "u32", "dynamic": true }
]
```

Call: `label_add_batch gameId="SCUS-97130" labelSet="socom2-structs" entries='[...]'`

### Deleting a label

```
label_delete gameId="SCUS-97130" labelSet="socom2-structs" address="0x001C8A00"
```

### Listing labels

```
label_list                   → all labels in the active set
label_list_functions         → only Function-typed labels
```

---

## Pointer Labels and Child Fields

A `Pointer` label models a variable that holds a pointer to a data structure. Child entries describe fields at byte offsets from the dereferenced pointer value.

### Creating a pointer and its fields

```
# 1. Create the pointer root (the variable that holds the pointer)
label_add_pointer
  pointerName="PlayerState"
  baseAddress="0x00352F40"

# 2. Add member fields at offsets from [*0x00352F40]
label_add_pointer_member pointerName="PlayerState" memberName="health"    offset=0   valueType="s32"
label_add_pointer_member pointerName="PlayerState" memberName="armor"     offset=4   valueType="s32"
label_add_pointer_member pointerName="PlayerState" memberName="ammo"      offset=8   valueType="u32"
label_add_pointer_member pointerName="PlayerState" memberName="position"  offset=12  valueType="float"
label_add_pointer_member pointerName="PlayerState" memberName="flags"     offset=16  valueType="u32"

# 3. View all defined pointers
label_get_pointers
```

The disassembly view expands the pointer inline, showing each named field at its resolved address alongside the live memory value when polling is enabled.

### Removing a pointer

```
label_delete_pointer pointerName="PlayerState"
```

---

## Runtime Resolution

Two label types support **live resolution** against the running emulator (requires PCSX2 PINE mode):

### String labels

`label_resolve_strings` reads each `String`-typed label's memory content from PCSX2 and updates the label value with the null-terminated ASCII string found at that address:

```
label_resolve_strings
→ Resolved 3 string labels:
    0x0031A200  mapName       = "Abandoned Village"
    0x0031A280  playerName    = "PAPA SMRF"
    0x0031A300  gameModeName  = "DEATHMATCH"
```

### Linked list labels

`label_resolve_linked_list` walks a linked list in live PCSX2 memory, starting from a given address, and assigns indexed labels to each node:

```
label_resolve_linked_list address="0x00360000" nextOffset=0
→ Node [0]  0x00360000
→ Node [1]  0x00360084
→ Node [2]  0x00360108
→ Node [3]  0x0036018C
```

---

## SQL Access

Labels are stored in the `Labels` and `LabelSets` tables. You can query them directly with `query_db`:

```sql
-- All labels for a specific disc
SELECT l.Address, l.Name, l.Type, l.Comment
FROM Labels l
JOIN LabelSets ls ON ls.Id = l.LabelSetId
JOIN GameDiscs d  ON d.Id  = ls.GameDiscId
WHERE d.Serial = 'SCUS-97130'
  AND ls.IsBuiltIn = 0
ORDER BY l.Address

-- All Function labels across all sets for a game
SELECT ls.SetName, l.Address, l.Name, l.ArgRegsJson
FROM Labels l
JOIN LabelSets ls ON ls.Id = l.LabelSetId
JOIN GameDiscs d  ON d.Id  = ls.GameDiscId
WHERE d.Serial = 'SCUS-97130'
  AND l.Type = 2  -- Function

-- Child (member) labels
SELECT l.Address, l.Name, l.Offset, l.ValueType, p.Name AS ParentName
FROM Labels l
JOIN Labels p ON p.Id = l.ParentId
WHERE l.ParentId IS NOT NULL

-- Dynamic labels (live-polled values)
SELECT l.Address, l.Name, l.Type, l.ValueType
FROM Labels l
JOIN LabelSets ls ON ls.Id = l.LabelSetId
WHERE l.Dynamic = 1
```

---

## Integration with the Disassembler

When code at a labelled address is displayed in the disassembler panel:

- **Address labels** show the name in the left margin.
- **Function labels** show `FNC_Name` and, if `argRegs`/`returnRegs` are set, the calling convention is annotated below the function header.
- **Pointer and Struct labels** expand inline to show each named child field with its current value if `dynamic = true`.
- **Value and Float labels** show the live-polled decimal value in the label manager column.

Labels are also available in CDS scripts by name — you can reference `ApplyDamage` directly rather than hardcoding `0x001C8A00`.

---

## Integration with the AI Agent Team

The [Fatality] team uses labels as its shared knowledge base across sessions:

- **Sub-Zero** (Memory Scanner) — promotes confirmed candidates to addresses, hands off to Kitana.
- **Kitana** (Structure Architect) — owns `label_add_pointer` / `label_add_pointer_member`; builds the full struct layout.
- **Scorpion** (Call Graph Hunter) — uses `label_list_functions` to find known hooks; adds new function labels after tracing.
- **Liu Kang** (Persistence Curator) — runs `label_add_batch` for bulk imports, deduplicates, and syncs seed files.
- **Raiden** (Lead) — calls `label_list` at the start of each session to review what has already been identified.

Rules:
- Always write labels to a **user set**, never the built-in set.
- Label only **verified** addresses — do not promote speculation.
- Keep names concise and consistent with existing naming conventions in the set.
- Add comments with evidence (e.g. `"Confirmed: watchpoint fired on health deduction at 0x001C8A00"`).

---

## Quick Reference

| Goal | Tool |
|------|------|
| See all sets for a game | `label_list_sets` |
| Create a new set | `label_create_set` |
| Switch active set | `label_set_active` |
| Delete a set | `label_delete_set` |
| List all labels | `label_list` |
| List only functions | `label_list_functions` |
| Add one label | `label_add` |
| Add many labels | `label_add_batch` |
| Remove a label | `label_delete` |
| Create a pointer entry | `label_add_pointer` |
| Add a field to a pointer | `label_add_pointer_member` |
| List all pointers | `label_get_pointers` |
| Remove a pointer | `label_delete_pointer` |
| Read string values live | `label_resolve_strings` |
| Walk a linked list live | `label_resolve_linked_list` |
| Raw SQL query on labels | `query_db` |
