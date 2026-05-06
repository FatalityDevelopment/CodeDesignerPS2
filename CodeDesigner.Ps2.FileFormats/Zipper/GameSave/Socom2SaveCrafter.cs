namespace CodeDesigner.Ps2.Save
{
    // -------------------------------------------------------------------------
    //  Save crafting helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Utility methods for crafting modified SOCOM II saves.
    /// Mirrors the socom2_craft.py workflow.
    ///
    /// <code>
    ///   // Load template, modify title, write back
    ///   var save = Socom2Save.Load("SaveGame0");
    ///   var active = Socom2SaveCrafter.MakeActiveSave(save);
    ///   active.Save("SaveGame0_active");
    ///
    ///   // Probe for title display buffer overflow
    ///   var probe = Socom2SaveCrafter.MakeOverflowProbe(save, 200);
    ///   probe.Save("SaveGame1_overflow_200");
    /// </code>
    /// </summary>
    public static class Socom2SaveCrafter
    {
        // Observed value offsets from the data record scan (decrypted space)
        public const int ChosenLevelOffset = 0x0158;
        public const int DifficultyOffset = 0x0160;
        public const int AiDifficultyOffset = 0x0168;
        public const int ChoseRankOffset = 0x0170;
        public const int SaveStateOffset = 0x0008;  // 0=empty/dummy, 1=active

        /// <summary>
        /// Clone <paramref name="template"/> and mark it as an active save
        /// with state=1 and a custom title.
        /// </summary>
        public static Socom2Save MakeActiveSave(Socom2Save template, string title = "SOCOM Revival Save")
        {
            var save = Socom2Save.FromDecryptedBytes(template.Decrypted.ToArray());
            save.WriteInt32(SaveStateOffset, 1);
            save.Title = title;
            return save;
        }

        /// <summary>
        /// Clone <paramref name="template"/> and set the title to
        /// <paramref name="titleLength"/> repeated 'A' characters.
        /// Use to probe for display buffer overflows on the load-game screen.
        /// </summary>
        public static Socom2Save MakeOverflowProbe(Socom2Save template, int titleLength)
        {
            var save = Socom2Save.FromDecryptedBytes(template.Decrypted.ToArray());
            save.Title = new string('A', titleLength);
            return save;
        }

        /// <summary>
        /// Clone <paramref name="template"/> and write extreme integer values
        /// into known field offsets to probe for out-of-bounds indexing.
        /// </summary>
        public static Socom2Save MakeExtremeIntSave(Socom2Save template,
            string title = "EXTREME INT TEST")
        {
            var save = Socom2Save.FromDecryptedBytes(template.Decrypted.ToArray());
            save.Title = title;

            if (save.Size > ChoseRankOffset + 4)
            {
                save.WriteInt32(ChosenLevelOffset, int.MaxValue); // 0x7FFFFFFF OOB index
                save.WriteInt32(DifficultyOffset, 0xFF);         // OOB difficulty
                save.WriteInt32(AiDifficultyOffset, 0xFF);         // OOB AI difficulty
                save.WriteInt32(ChoseRankOffset, int.MaxValue); // 0x7FFFFFFF OOB rank
            }

            return save;
        }

        /// <summary>
        /// Batch-generate all four probe saves from the same template file.
        /// Output files are written alongside <paramref name="templatePath"/>.
        /// </summary>
        public static void GenerateAllProbes(string templatePath)
        {
            var template = Socom2Save.Load(templatePath);
            string dir = Path.GetDirectoryName(templatePath) ?? ".";

            void Write(Socom2Save s, string name)
            {
                string path = Path.Combine(dir, name);
                s.Save(path);
                Console.WriteLine($"Wrote {s.Size} bytes -> {path}");
                // Quick roundtrip sanity check
                byte[] raw = File.ReadAllBytes(path);
                var back = Socom2Save.FromBytes(raw);
                Console.WriteLine($"  Title roundtrip: {back.Title}");
            }

            Write(MakeActiveSave(template), "SaveGame0_active");
            Write(MakeOverflowProbe(template, 200), "SaveGame1_overflow_200");
            Write(MakeOverflowProbe(template, 72), "SaveGame2_overflow_72");
            Write(MakeExtremeIntSave(template), "SaveGame3_extreme_ints");

            Console.WriteLine("\nDone. Copy saves to MC via CodeDesigner mc_import to test in PCSX2.");
        }
    }
}