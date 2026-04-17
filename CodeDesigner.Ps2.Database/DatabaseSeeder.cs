using System.Reflection;
using System.Text.Json;
using CodeDesigner.Ps2.Database.Entities;
using Microsoft.EntityFrameworkCore;

namespace CodeDesigner.Ps2.Database;

/// <summary>
/// Seeds the <see cref="CodeDesignerDbContext"/> with initial data required for
/// the application to operate, such as MCP tool descriptions stored in
/// <see cref="McpConfig"/>.
/// </summary>
/// <remarks>
/// Run at application startup after <c>database.EnsureCreated()</c> or after
/// applying migrations.  The seeder is idempotent — it inserts a row only when
/// no row for that key already exists, so re-running it is safe.
/// </remarks>
public class DatabaseSeeder
{
    private readonly CodeDesignerDbContext _db;

    public DatabaseSeeder(CodeDesignerDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Seeds all tool descriptions into <see cref="McpConfig"/> if they are not
    /// already present.  Call after ensuring the schema is up to date.
    /// </summary>
    /// <param name="archiveRoot">
    /// Optional path to the root of the CheatCodeArchive checkout.
    /// When supplied and the <c>Database/</c> sub-folder exists, the bulk cheat
    /// importer runs to pull in the latest cheat data.  When omitted or when the
    /// directory does not exist the step is silently skipped.
    /// </param>
    public async Task SeedAsync(string? archiveRoot = null, CancellationToken ct = default)
    {
        await SeedToolDescriptionsAsync(ct);
        await SeedSettingsAsync(ct);
        await SeedGameDataAsync(ct);
        await SeedLabelSetsAsync(ct);

        if (!string.IsNullOrWhiteSpace(archiveRoot))
        {
            var dbFolder = Path.Combine(archiveRoot, "Database");
            if (Directory.Exists(dbFolder))
            {
                Console.WriteLine($"Importing cheats from CheatCodeArchive at: {archiveRoot}");
                var bulkCheatParser = new BulkCheatParser(_db, archiveRoot);
                await bulkCheatParser.RunAsync(ct);
            }
            else
            {
                Console.WriteLine($"[BulkCheatParser] Archive Database folder not found, skipping: {dbFolder}");
            }
        }

        await _db.SaveChangesAsync(ct);
    }

    // ── Tool descriptions ──────────────────────────────────────────────────────

    private async Task SeedToolDescriptionsAsync(CancellationToken ct)
    {
        foreach (var (key, description) in ToolDescriptions)
            await UpsertConfigAsync(key, description, "MCP tool description", ct);
    }

    private async Task UpsertConfigAsync(
        string key, string value, string? description, CancellationToken ct)
    {
        var existing = await _db.McpConfigs.FirstOrDefaultAsync(c => c.Key == key, ct);
        if (existing is null)
        {
            _db.McpConfigs.Add(new McpConfig
            {
                Key = key,
                Value = value,
                Description = description,
                UpdatedAt = DateTime.UtcNow,
            });
        }
    }

    // ── Settings ───────────────────────────────────────────────────────────────

    private async Task SeedSettingsAsync(CancellationToken ct)
    {
        foreach (var (key, defaultValue, description) in DefaultSettings)
            await UpsertSettingAsync(key, defaultValue, description, ct);
    }

    private async Task UpsertSettingAsync(
        string key, string? defaultValue, string? description, CancellationToken ct)
    {
        var existing = await _db.Settings.FindAsync([key], ct);
        if (existing is null)
        {
            _db.Settings.Add(new Setting
            {
                Key         = key,
                Value       = defaultValue,
                Description = description,
                UpdatedAt   = DateTime.UtcNow,
            });
        }
    }

    // ── Game data ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Seeds the canonical SOCOM II game, its NTSC-U region, and the SCUS-97275
    /// disc entry.  All three rows are inserted only when they don't already exist.
    /// </summary>
    private async Task SeedGameDataAsync(CancellationToken ct)
    {
        // GameRegion — NTSC-U
        var ntscU = await _db.GameRegions.FirstOrDefaultAsync(r => r.Name == "NTSC-U", ct);
        if (ntscU is null)
        {
            ntscU = new GameRegion { Name = "NTSC-U" };
            _db.GameRegions.Add(ntscU);
            await _db.SaveChangesAsync(ct);
        }

        // Game — SOCOM II: U.S. Navy SEALs
        // Note: Game.Title uses the official colon-separated title.
        // LabelSet.GameId (used in SeedLabelSetsAsync) is derived from the ISO
        // filename ("SOCOM II - U.S. Navy SEALs") and is intentionally different.
        const string socomGameTitle = "SOCOM II: U.S. Navy SEALs";
        var socomGame = await _db.Games.FirstOrDefaultAsync(g => g.Title == socomGameTitle, ct);
        if (socomGame is null)
        {
            socomGame = new Game
            {
                Title       = socomGameTitle,
                Description = "PlayStation 2 online third-person shooter published by Sony Computer Entertainment America (SCEA). NTSC-U, serial SCUS-97275.",
            };
            _db.Games.Add(socomGame);
            await _db.SaveChangesAsync(ct);
        }

        // GameDisc — SCUS-97275 (CRC left blank for the user to fill in later)
        const string socomSerial = "SCUS-97275";
        bool discExists = await _db.GameDiscs.AnyAsync(
            d => d.Serial == socomSerial && d.GameId == socomGame.Id, ct);

        if (!discExists)
        {
            _db.GameDiscs.Add(new GameDisc
            {
                GameId       = socomGame.Id,
                GameRegionId = ntscU.Id,
                Serial       = socomSerial,
                CRC          = null,   // populated by the user once known
                IsoPath      = null,
            });
            await _db.SaveChangesAsync(ct);
        }
    }

    // ── Label sets ────────────────────────────────────────────────────────────

    /// <summary>
    /// Seeds the two default label sets for "SOCOM II - U.S. Navy SEALs":
    /// <list type="bullet">
    ///   <item><description>
    ///     <b>CodeDesigner labels</b> — the standard PS2 system register / memory-map
    ///     symbols from the built-in label database.
    ///   </description></item>
    ///   <item><description>
    ///     <b>patch_labels</b> — SOCOM II–specific reverse-engineering labels
    ///     (functions, globals, AES/ZDB crypto symbols, and struct definitions).
    ///   </description></item>
    /// </list>
    /// Rows are only inserted when the set does not already exist for the game.
    /// </summary>
    private async Task SeedLabelSetsAsync(CancellationToken ct)
    {
        const string socomGameId = "SOCOM II - U.S. Navy SEALs";

        await SeedLabelSetFromResourceAsync(
            gameId:      socomGameId,
            setName:     "CodeDesigner labels",
            resourceSuffix: "codedesigner_labels.json",
            ct);

        await SeedLabelSetFromResourceAsync(
            gameId:      socomGameId,
            setName:     "patch_labels",
            resourceSuffix: "socom_patch_labels.json",
            ct);
    }

    /// <summary>
    /// Inserts a <see cref="LabelSet"/> and its child <see cref="Label"/> /
    /// <see cref="LabelMember"/> rows by reading a JSON seed file embedded in
    /// this assembly under <c>Resources/SeedData/&lt;resourceSuffix&gt;</c>.
    /// </summary>
    /// <remarks>
    /// The JSON format supports two optional top-level arrays:
    /// <list type="bullet">
    ///   <item><description>
    ///     <c>"symbol"</c> — flat address labels
    ///     (<c>{ address, label, comment? }</c>).
    ///   </description></item>
    ///   <item><description>
    ///     <c>"structs"</c> — struct definitions
    ///     (<c>{ name, baseAddress, members: [{ offset, name }] }</c>).
    ///   </description></item>
    /// </list>
    /// </remarks>
    private async Task SeedLabelSetFromResourceAsync(
        string gameId, string setName, string resourceSuffix, CancellationToken ct)
    {
        // Skip when the set already exists — idempotent.
        bool exists = await _db.LabelSets.AnyAsync(
            ls => ls.GameId == gameId && ls.SetName == setName, ct);
        if (exists) return;

        // Load the embedded JSON resource.
        var asm          = Assembly.GetExecutingAssembly();
        var resourceName = asm.GetManifestResourceNames()
                              .FirstOrDefault(n => n.EndsWith(resourceSuffix,
                                  StringComparison.OrdinalIgnoreCase));
        if (resourceName is null)
            return; // resource missing — skip silently

        await using var stream = asm.GetManifestResourceStream(resourceName)!;
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var root = doc.RootElement;

        var labelSet = new LabelSet
        {
            SetName   = setName,
            GameId    = gameId,
            IsBuiltIn = false,
        };
        _db.LabelSets.Add(labelSet);
        await _db.SaveChangesAsync(ct); // obtain labelSet.Id

        // ── symbol array ──────────────────────────────────────────────────────
        if (root.TryGetProperty("symbol", out var symbolArr))
        {
            foreach (var sym in symbolArr.EnumerateArray())
            {
                var address = sym.TryGetProperty("address", out var ap) ? ap.GetString() : null;
                var name    = sym.TryGetProperty("label",   out var lp) ? lp.GetString() : null;
                var comment = sym.TryGetProperty("comment", out var cp)
                              && cp.ValueKind == JsonValueKind.String
                              ? cp.GetString() : null;

                if (string.IsNullOrWhiteSpace(address) || string.IsNullOrWhiteSpace(name))
                    continue;

                _db.Labels.Add(new Label
                {
                    LabelSetId = labelSet.Id,
                    Address    = address,
                    Name       = name,
                    Comment    = comment,
                    Type       = LabelType.Address,
                });
            }
        }

        // ── structs array ─────────────────────────────────────────────────────
        if (root.TryGetProperty("structs", out var structsArr))
        {
            foreach (var st in structsArr.EnumerateArray())
            {
                var structName    = st.TryGetProperty("name",        out var np) ? np.GetString() : null;
                var baseAddress   = st.TryGetProperty("baseAddress", out var bp) ? bp.GetString() : null;

                if (string.IsNullOrWhiteSpace(structName) || string.IsNullOrWhiteSpace(baseAddress))
                    continue;

                var structLabel = new Label
                {
                    LabelSetId = labelSet.Id,
                    Address    = baseAddress,
                    Name       = structName,
                    Type       = LabelType.Struct,
                };
                _db.Labels.Add(structLabel);
                await _db.SaveChangesAsync(ct); // obtain structLabel.Id

                if (!st.TryGetProperty("members", out var membersArr)) continue;

                foreach (var m in membersArr.EnumerateArray())
                {
                    int offset     = m.TryGetProperty("offset", out var offProp) ? offProp.GetInt32() : 0;
                    var memberName = m.TryGetProperty("name",   out var mnp)     ? mnp.GetString()    : null;

                    if (string.IsNullOrWhiteSpace(memberName)) continue;

                    // Members are now stored as child Label entries with ParentId pointing to the struct label.
                    var syntheticAddr = baseAddress + ":+0x" + offset.ToString("X4").ToUpperInvariant();
                    _db.Labels.Add(new Label
                    {
                        LabelSetId = labelSet.Id,
                        ParentId   = structLabel.Id,
                        Offset     = offset,
                        Address    = syntheticAddr,
                        Name       = memberName,
                        Type       = LabelType.Address,
                    });
                }
            }
        }

        await _db.SaveChangesAsync(ct);
    }

    // ── Seed data ──────────────────────────────────────────────────────────────

    /// <summary>
    /// All MCP tool description entries keyed by the string passed to
    /// <c>[DynamicDescription("key")]</c>.
    /// </summary>
    public static readonly IReadOnlyList<(string Key, string Description)> ToolDescriptions =
    [
        ("ping_desc",
            "Test connectivity to the MIPS64 Emotion Engine emulator backend. " +
            "Returns PONG when the socket server is reachable on localhost:8765."),

        ("session_start_desc",
            "CALL THIS FIRST at the beginning of every session. " +
            "Returns the full Code Designer operational guidelines so the agent is properly " +
            "configured before taking any other action. " +
            "Also lists all existing progress reports so previous session work can be resumed. " +
            "This tool is the primary way to load guidelines — do not rely on prompts/get."),

        ("disassemble_desc",
            "Disassemble PS2 Emotion Engine MIPS64 machine-code opcodes into assembly text. " +
            "Provide 32-bit hex opcodes separated by spaces or newlines (e.g. \"27BDFFD0 AFBF001C\"). " +
            "Optionally supply a virtual start address so labels and branch targets are resolved correctly."),

        ("assemble_desc",
            "Assemble PS2 Emotion Engine MIPS64 assembly instructions into machine-code hex opcodes. " +
            "Provide one instruction per line. " +
            "Optionally supply a virtual start address so branches and labels are encoded correctly."),

        ("compile_cds_desc",
            "Compile a Code Designer Script (.cds) source file into PS2 GameShark/Action Replay " +
            "cheat codes. The script uses a structured language that maps address/value assignments " +
            "to the appropriate cheat code type."),

        ("read_memory_desc",
            "Read raw bytes from the live PS2 emulator memory and return them as hex. " +
            "Requires the emulator to be running with the socket server enabled. " +
            "Address may be supplied in hex (0x prefix) or decimal."),

        ("write_memory_desc",
            "Write arbitrary bytes to live PS2 emulator memory. " +
            "Bytes are supplied as a hex string (e.g. '27BDFFD0'). " +
            "Requires the emulator to be running with the socket server enabled."),

        ("load_bin_desc",
            "Load a binary file from disk directly into live emulator memory at a specified address. " +
            "Useful for injecting custom code or data patches into a running game."),

        ("disassemble_memory_desc",
            "Read bytes from the live PS2 emulator and disassemble them to MIPS64 EE assembly text. " +
            "Combines read_memory and disassemble in a single call."),

        ("apply_cheats_desc",
            "Apply PS2 GameShark/Action Replay cheat codes to the live emulator memory. " +
            "Parse and execute a block of cheat codes against the running game."),

        ("port_cheats_desc",
            "Port PS2 GameShark cheat codes from one game revision to another by scanning two " +
            "memory dumps for matching byte patterns and recalculating addresses."),

        ("get_pc_desc",
            "Get the current program counter (PC) of the PS2 EE CPU and the emulator pause state. " +
            "Useful for determining where execution is currently suspended."),

        ("get_registers_desc",
            "Get a full register dump of the PS2 EE CPU: all 32 general-purpose registers (GPRs), " +
            "HI/LO, COP0 system registers, COP1 FPU registers, and VU0 registers."),

        ("set_register_desc",
            "Write a new value to a named EE CPU register. " +
            "RegisterName: GPR (requires RegisterIndex 0–31), HI, LO, PC, or COP0_x/COP1_x."),

        ("get_labels_desc",
            "Return all symbol labels (with type badges) from the active label set. " +
            "Useful for reviewing previously annotated addresses before continuing analysis."),

        ("get_function_labels_desc",
            "Return only Function-typed labels from the active label set, including named argument " +
            "and return register annotations for each function."),

        ("add_label_desc",
            "Insert or update a label entry in the active label set. " +
            "Supports all label types: Address, Function, Struct, Array, LinkedList, String. " +
            "Data-type annotations (u8, u16, u32, u64, s8, s16, s32, s64, float, double, pointer) can be set via the valueType field."),

        ("delete_label_desc",
            "Remove a symbol label from the active label set. " +
            "Changes are persisted to disk immediately."),

        ("resolve_strings_desc",
            "Read each String-typed label's memory content from live PCSX2 (PINE mode) and update " +
            "the label value with the null-terminated ASCII string found at that address."),

        ("resolve_linked_list_desc",
            "Walk a linked list in live PCSX2 memory starting from the given address, using node " +
            "size and next-pointer offset from the corresponding LinkedList label."),

        ("scan_strings_desc",
            "Scan a region of live PS2 emulator memory for null-terminated ASCII strings. " +
            "Returns strings that meet the minimum length threshold, with their addresses."),

        ("scan_functions_desc",
            "Scan a region of live PS2 emulator memory and detect likely MIPS function entry points. " +
            "Looks for ADDIU $sp, $sp, -N prologues and other calling-convention patterns."),

        ("scan_pointers_desc",
            "Scan a region of live PS2 emulator memory for 4-byte aligned values that are confirmed " +
            "EE RAM pointers (in the range 0x00000000–0x01FFFFFF or 0x20000000–0x21FFFFFF)."),

        ("scan_floats_desc",
            "Scan a region of live PS2 emulator memory for plausible IEEE-754 single-precision floats. " +
            "Returns addresses and float values that fall within a reasonable numeric range."),

        ("scan_integers_desc",
            "Scan a region of live PS2 emulator memory for non-zero 4-byte aligned 32-bit integers. " +
            "Useful for locating counters, health values, and game state variables."),

        ("get_strings_desc",
            "Scan base64-encoded binary data for null-terminated printable ASCII strings of at least " +
            "the specified minimum length. Returns strings and their byte offsets."),

        ("get_functions_desc",
            "Scan base64-encoded binary data for MIPS function entry points. " +
            "Detects ADDIU $sp, $sp, -N prologues and classic PS2 function patterns."),

        ("get_pointers_desc",
            "Scan base64-encoded binary data for 4-byte aligned values that are confirmed EE memory " +
            "pointers. Returns offsets and pointer values."),

        ("get_floats_desc",
            "Scan base64-encoded binary data for plausible IEEE-754 single-precision floats. " +
            "Filters out NaN, Inf, subnormals, and values outside a configurable range."),

        ("get_integers_desc",
            "Scan base64-encoded binary data for non-zero 4-byte aligned 32-bit integers. " +
            "Applies an optional value range filter."),

        ("search_desc",
            "Search live PS2 emulator memory or a disassembly listing for a text, regex, bytes, or " +
            "string pattern. Returns all matching addresses and surrounding context."),

        ("pause_emulator_desc",
            "Pause PS2 emulator execution. " +
            "The emulator must be running via `dotnet run -- --server` or PCSX2 via PINE."),

        ("resume_emulator_desc",
            "Resume PS2 emulator execution after a pause or breakpoint hit. " +
            "If stopped at a breakpoint, execution continues until the next breakpoint or pause."),

        ("step_desc",
            "Execute exactly one EE instruction while keeping the emulator paused. " +
            "The emulator must be paused before calling this tool."),

        ("restart_emulator_desc",
            "Perform a full hardware reset of the PS2 emulator without dropping the socket " +
            "connection. Reloads the BIOS and restarts game execution from the beginning."),

        ("set_breakpoint_desc",
            "Set a breakpoint at a virtual address in the PS2 emulator. " +
            "Execution pauses when the EE program counter reaches this address."),

        ("clear_breakpoint_desc",
            "Remove a previously set breakpoint at a virtual address."),

        ("clear_all_breakpoints_desc",
            "Remove all breakpoints currently set in the PS2 emulator."),

        ("get_threads_desc",
            "Enumerate PS2 EE OS threads by walking the kernel thread control block (TCB) pool. " +
            "Returns thread ID, state, priority, entry point, and stack pointer for each thread."),

        ("snapshot_desc",
            "Save a full memory snapshot of the running PS2 emulator to a directory on disk. " +
            "Snapshots can be compared later to detect memory changes across game states."),

        ("load_snapshot_desc",
            "Reload a previously saved memory snapshot as a save state into the running PS2 emulator. " +
            "Restores EE and IOP RAM to the state at snapshot time."),

        ("list_disc_elfs_desc",
            "Open a PS2 disc image (ISO/BIN or ZIP containing one) and list all ELF and IRX files " +
            "found on the disc. Returns file names and sizes."),

        ("recompile_elf_desc",
            "Extract a named ELF from a PS2 disc image and statically recompile it to C++ source " +
            "code. Translates MIPS64 EE instructions to equivalent C++ statements."),

        ("pcsx2_status_desc",
            "Query the running PCSX2 emulator status via the PINE IPC socket. " +
            "Returns Running/Paused state and whether a game is loaded."),

        ("pcsx2_game_info_desc",
            "Query the running game's title, serial ID, UUID, and version from PCSX2 via the PINE " +
            "IPC socket. Only available when PCSX2 is running with PINE enabled."),

        ("detect_cheat_format_desc",
            "Heuristically detect the encryption format of a block of PS2 cheat codes. " +
            "Returns the most likely format (Raw, CodeBreaker, GameShark, etc.)."),

        ("convert_cheat_desc",
            "Convert PS2 cheat codes between the unencrypted raw format and a device-specific " +
            "encrypted format (CodeBreaker, GameShark, Action Replay)."),

        ("reset_emulator_desc",
            "Send a CPU reset signal to the PS2 emulator (PINE) or reset the internal scheduler. " +
            "Restarts game execution without reloading the BIOS ROM."),

        ("step_into_desc",
            "Step into the next PS2 EE instruction (follows calls into subroutines). " +
            "In PINE/PCSX2 mode this issues a CPU step via the PINE debug interface."),

        ("step_over_desc",
            "Step over the next PS2 EE instruction (skips into subroutines, executing them fully). " +
            "Sets a temporary breakpoint at PC+8 (after the delay slot) and resumes."),

        ("step_out_desc",
            "Step out of the current PS2 EE subroutine, running until the next return instruction " +
            "(JR $ra) or equivalent."),

        ("get_fps_desc",
            "Read the current emulated framerate from PCSX2 via the PINE socket. " +
            "Only available in PINE mode with a game running."),

        ("set_limiter_mode_desc",
            "Set the PCSX2 speed limiter mode via the PINE socket. " +
            "mode: 0=Nominal (default 60 fps), 1=Limit, 2=Turbo, 3=Slow-motion."),

        ("frame_advance_desc",
            "Advance the PCSX2 emulator by a fixed number of video frames then pause. " +
            "Useful for frame-by-frame analysis of game animations or timing-sensitive behaviour."),

        ("list_breakpoints_desc",
            "List all active PC breakpoints set in PCSX2 via the PINE socket. " +
            "cpuSel: 0=EE only (default), 1=IOP only, 2=both."),

        ("get_symbol_desc",
            "Look up the EE function symbol name for a given virtual address via PINE. " +
            "Only available when PCSX2 has a symbol database loaded."),

        ("get_symbol_by_name_desc",
            "Look up an EE symbol address and size by its demangled name via PINE. " +
            "Only available when PCSX2 has a symbol database loaded."),

        ("get_call_stack_desc",
            "Walk the EE call stack and return each frame's entry point, current PC, stack pointer, " +
            "and resolved symbol name (if available)."),

        ("get_iop_threads_desc",
            "List all IOP (I/O Processor) OS threads via the PINE socket. " +
            "Returns thread ID, status, priority, and entry point for each thread."),

        ("get_modules_desc",
            "List all loaded IOP modules (IRX files) via the PINE socket. " +
            "Returns name, version, start address, and size for each module."),

        ("pine_get_register_desc",
            "Read any EE or IOP CPU register by category and index via the PINE socket. " +
            "Returns the 64-bit or 128-bit value depending on register type."),

        ("pine_set_register_desc",
            "Write any EE or IOP CPU register by category and index via the PINE socket. " +
            "The value is supplied as a 64-bit unsigned integer."),

        ("list_functions_desc",
            "Retrieve a paginated list of EE function symbols from the PCSX2 symbol database via PINE. " +
            "Returns function name, address, and size."),

        ("list_globals_desc",
            "Retrieve a paginated list of EE global variable symbols from the PCSX2 symbol database " +
            "via PINE. Returns variable name, address, and size."),

        ("get_locals_desc",
            "List the parameters and local variables for the EE function that contains a given " +
            "virtual address. Uses DWARF debug information loaded by PCSX2."),

        ("pine_disassemble_desc",
            "Disassemble instructions at a given address on EE or IOP via the PINE live-memory " +
            "interface. Returns a block of disassembled instructions."),

        ("save_state_file_desc",
            "Save the current PCSX2 emulator state to a named file path via the PINE socket. " +
            "The resulting .p2s file can be loaded with load_state_file."),

        ("load_state_file_desc",
            "Load a PCSX2 save-state file (.p2s) into the running emulator via the PINE socket. " +
            "Restores CPU registers, memory, and hardware state."),

        ("extract_p2s_desc",
            "Extract the memory dump files (EEmem.bin, IOPmem.bin) from a PCSX2 .p2s save-state " +
            "archive to a directory for offline analysis."),

        ("pine_version_desc",
            "Query the PINE IPC protocol version string from PCSX2. " +
            "Useful for checking that the connected PCSX2 build supports required features."),

        ("pine_save_state_slot_desc",
            "Save the current PCSX2 emulator state to a numbered save-state slot (0–9) via PINE. " +
            "Slots are persistent across PCSX2 sessions."),

        ("pine_load_state_slot_desc",
            "Load a PCSX2 save-state from a numbered slot (0–9) via PINE. " +
            "Unlike load_state_file, slot-based saves are managed entirely by PCSX2."),

        ("mc_list_desc",
            "List all save-game directories on a PS2 memory card image file. " +
            "Returns save name, product code, description, and size for each entry."),

        ("mc_export_desc",
            "Export a single save-game directory from a PS2 memory card image to a folder on disk. " +
            "Creates one file per memory card file within the save directory."),

        ("mc_import_desc",
            "Import a save-game directory into a PS2 memory card image. " +
            "The save directory must have been exported with mc_export."),

        ("mc_delete_desc",
            "Delete a single save-game directory from a PS2 memory card image. " +
            "This operation cannot be undone."),

        ("mc_format_desc",
            "Create a new blank PS2 memory card image file (8 MB). " +
            "If a file already exists at the path it is overwritten."),

        ("mc_read_file_desc",
            "Read the raw bytes of a specific file inside a PS2 memory card save directory. " +
            "Returns the file contents as a base64-encoded string."),

        ("hdd_list_desc",
            "Open a PS2 HDD image file and list all APA partitions on it. " +
            "Returns partition ID, name, type, and size for each partition."),

        ("hdd_list_files_desc",
            "List all files in a specific partition on a PS2 HDD image. " +
            "Returns file name, size, and inode information."),

        ("hdd_export_desc",
            "Export a single file from a PS2 HDD partition to a directory on disk. " +
            "Does not require the emulator to be running."),

        ("hdd_import_desc",
            "Import a file into a specific partition on a PS2 HDD image. " +
            "Does not require the emulator to be running."),

        ("hdd_delete_desc",
            "Delete a partition from a PS2 HDD image. " +
            "This operation cannot be undone."),

        ("hdd_create_desc",
            "Create a new blank PS2 HDD image file with an initialised APA partition table. " +
            "If the file already exists it is overwritten."),

        ("iso_list_files_desc",
            "Open a PS2 disc image (ISO/BIN or ZIP containing one) and list all files and " +
            "directories on the disc in ISO-9660 format."),

        ("iso_read_file_desc",
            "Extract a file from a PS2 disc image to disk or return its contents as base64. " +
            "Use the path returned by iso_list_files to identify the target file."),

        ("iso_get_info_desc",
            "Get metadata about a PS2 disc image: format (cooked/raw), sector count, size, and " +
            "the primary volume descriptor title."),

        ("list_progress_reports_desc",
            "List all progress reports stored in the database. " +
            "Call this at the start of a session to check for reports from previous sessions."),

        ("get_progress_report_desc",
            "Read a previously saved progress report from the database. " +
            "Load this at the start of a session to resume work from where it was left off."),

        ("save_progress_report_desc",
            "Create or overwrite a progress report in the database. " +
            "The name should be a short identifier for the current task (e.g. 'god-of-war-health')."),

        ("delete_progress_report_desc",
            "Delete a progress report from the database. " +
            "Use only when a task is fully complete and the report is no longer needed."),

        ("get_setting_desc",
            "Read a single application setting from the database by key. " +
            "Well-known keys include 'BiosPath', 'DiscPath', and 'ui.selectedGame'."),

        ("set_setting_desc",
            "Insert or update an application setting in the database. " +
            "Pass an empty string as value to clear the setting without deleting its key."),

        ("get_all_settings_desc",
            "Return all application settings from the database as a key=value list. " +
            "Optionally supply a prefix (e.g. 'ui.') to filter to a specific category."),

        ("delete_setting_desc",
            "Remove an application setting from the database by key."),

        ("get_snapshots_desc",
            "List all saved memory snapshots for a game, looked up by ISO filename or game title. " +
            "Returns snapshot titles, paths, and timestamps so you can choose one to load with load_snapshot."),

        ("get_structs_desc",
            "Return all user-defined PS2 memory struct definitions stored on the server. " +
            "Each struct has a name, base address, and a list of typed member fields."),

        ("add_struct_desc",
            "Create or update a named PS2 memory struct definition with a base address. " +
            "Use add_struct_member to add fields after creating the struct."),

        ("delete_struct_desc",
            "Remove a named PS2 memory struct definition. " +
            "Works in both internal emulator and PINE/PCSX2 modes."),

        ("add_struct_member_desc",
            "Add or update a named member field in an existing PS2 memory struct definition. " +
            "Members have an offset, a data type, and an optional count for array fields."),

        ("get_nav_history_desc",
            "Return the server-side navigation history: the list of EE addresses recently visited " +
            "by navigate_to calls during the current session."),

        ("clear_nav_history_desc",
            "Clear the server-side navigation history. " +
            "Works in both internal emulator and PINE/PCSX2 modes."),

        ("navigate_to_desc",
            "Tell the VS Code disasm editor to jump to a specific EE virtual address. " +
            "When the editor is open, the view scrolls to and highlights the given address."),

        ("get_address_snapshot_history_desc",
            "Read the historical 4-byte value at a specific EE virtual address across every memory " +
            "snapshot saved to the snapshot directory. Useful for tracking value changes over time."),

        ("scan_value_desc",
            "Scan EE main RAM (or a custom address range) for a specific value of a given data type " +
            "(u8, u16, u32, u64, f32). Returns all addresses where the value matches."),

        ("read_memory_region_desc",
            "Read a contiguous region of EE RAM in one call and optionally search for a byte " +
            "pattern within it. Returns the region as base64 and any pattern match offsets."),

        ("scan_pattern_desc",
            "Scan live EE RAM (or IOP RAM) for a byte pattern and return all matching addresses. " +
            "Supports wildcards in the pattern for flexible signature scanning."),

        ("set_memory_watchpoint_desc",
            "Set a write watchpoint on a range of EE RAM. In PINE mode (PCSX2): a software watchpoint " +
            "fires when any byte in the range is written. Returns a watchpoint ID."),

        ("clear_memory_watchpoint_desc",
            "Remove a previously registered memory watchpoint by its ID (e.g. 'wp-0'). " +
            "Use 'clear_all_memory_watchpoints' to remove all watchpoints at once."),

        ("clear_all_memory_watchpoints_desc",
            "Remove every registered memory watchpoint."),

        ("list_memory_watchpoints_desc",
            "List all currently active memory watchpoints: ID, address, size, and access type."),

        ("get_watchpoint_hit_desc",
            "Check whether any registered memory watchpoint has fired and return the details. " +
            "Returns the watchpoint ID, address, and value written when triggered."),

        ("trace_calls_desc",
            "Step the emulator instruction-by-instruction from the current PC to end_pc and record " +
            "every JAL/JALR call encountered. Returns a call trace with addresses and symbol names."),

        ("run_until_pc_desc",
            "Resume the emulator and block until the EE program counter reaches target_pc, then " +
            "pause. Returns the number of instructions executed and the final CPU state."),

        ("run_until_watchpoint_desc",
            "Resume PCSX2 and block until any registered memory watchpoint fires (value at the " +
            "watched address changes), then pause and return the watchpoint hit details."),
    ];

    /// <summary>
    /// Well-known application settings seeded with empty defaults so the DB
    /// contains the schema even before the user configures anything.
    /// Rows are only inserted when no row for that key already exists.
    /// </summary>
    public static readonly IReadOnlyList<(string Key, string? DefaultValue, string Description)> DefaultSettings =
    [
        // ── UI selections ──────────────────────────────────────────────────────
        ("ui.selectedEmulator",
            null,
            "Absolute path of the currently selected emulator executable (e.g. pcsx2-qtx64-dbg.exe or the CodeDesigner bridge binary)."),

        ("ui.selectedBios",
            null,
            "Absolute path of the currently selected PS2 BIOS image (e.g. SCPH-70012_BIOS_V12_USA_200.bin)."),

        ("ui.selectedGame",
            null,
            "Absolute path of the currently selected game disc image (ISO, ZIP, or BIN/CUE)."),

        // ── Emulator setting arrays (JSON-serialised) ──────────────────────────
        ("emu.bios",
            "[]",
            "JSON array of available BIOS entries ({ name, path }) shown in the BIOS dropdown."),

        ("emu.games",
            "[]",
            "JSON array of available game entries ({ name, path }) shown in the game dropdown."),

        ("emu.emulators",
            "[]",
            "JSON array of available emulator entries ({ type, path }) shown in the emulator dropdown."),

        // ── PCSX2 PINE connection ──────────────────────────────────────────────
        ("emu.pcsx2.host",
            null,
            "Hostname or IP address for the PCSX2 PINE socket connection (default: localhost)."),

        ("emu.pcsx2.slot",
            null,
            "PINE slot number used when connecting to PCSX2 (default: 28011)."),
    ];
}
