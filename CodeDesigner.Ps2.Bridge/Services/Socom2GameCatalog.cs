using CodeDesigner.Ps2.Multiplayer.Protocol;

namespace CodeDesigner.Ps2.Bridge.Services
{
    /// <summary>
    /// SOCOM II game messages and DME object classes, embedded from Mods/SOCOM2/packets/packets.json
    /// and objects.json (the files the Python bots and server scripts read). Installs them as the
    /// packet dissector's <see cref="PacketDissector.GameCatalog"/> and exposes the full
    /// <see cref="MessageCatalog"/> built from them plus the server's enums.
    /// </summary>
    internal static class Socom2GameCatalog
    {
        private static readonly Lazy<GameMessageCatalog> s_game = new(Load);
        private static readonly Lazy<IReadOnlyList<CatalogEntry>> s_entries = new(() => MessageCatalog.Build(Game));

        public static GameMessageCatalog Game => s_game.Value;

        /// <summary>Every message the multiplayer server defines (see <see cref="MessageCatalog"/>).</summary>
        public static IReadOnlyList<CatalogEntry> Entries => s_entries.Value;

        /// <summary>Make the dissector label SOCOM II DME traffic. Safe to call more than once.</summary>
        public static void Install() => PacketDissector.GameCatalog = Game;

        private static GameMessageCatalog Load()
        {
            var asm = typeof(Socom2GameCatalog).Assembly;
            using var packets = asm.GetManifestResourceStream("Socom2.packets.json");
            using var objects = asm.GetManifestResourceStream("Socom2.objects.json");
            try
            {
                return GameMessageCatalog.Load(packets, objects);
            }
            catch (Exception ex)
            {
                // A malformed edit to the JSON shouldn't take packet capture down with it.
                Console.WriteLine($"[Network] SOCOM II packet catalog failed to load: {ex.Message}");
                return GameMessageCatalog.Empty;
            }
        }
    }
}
