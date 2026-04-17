using System.Text;

namespace CodeDesigner.Ps2.Pis2Map
{
    public sealed class PisConvertResult
    {
        public bool Success { get; init; }
        public int LabelCount { get; init; }
        public string Error { get; init; } = string.Empty;
    }

    public static class PisConverter
    {
        public static PisConvertResult Convert(string pisPath, string mapPath)
        {
            byte[] pisdata;
            try { pisdata = File.ReadAllBytes(pisPath); }
            catch (Exception ex) { return Fail($"Cannot read '{pisPath}': {ex.Message}"); }

            if (pisdata.Length < 4 ||
                pisdata[0] != 0x50 || pisdata[1] != 0x49 ||
                pisdata[2] != 0x53 || pisdata[3] != 0x1F)
                return Fail("File is not a valid .pis symbol file.");

            int foundit = 0;
            for (int i = pisdata.Length - 8; i > 0 && foundit == 0; i--)
            {
                if (pisdata[i]   == 0x11 && pisdata[i+1] == 0x11 && pisdata[i+2] == 0x11 && pisdata[i+3] == 0x11 &&
                    pisdata[i+4] == 0x11 && pisdata[i+5] == 0x11 && pisdata[i+6] == 0x11 && pisdata[i+7] == 0x11)
                    foundit = i + 8;
            }

            if (foundit <= 0) return Fail("Label block not found in .pis file.");

            int s = foundit + 8; // skip lblSize (4) + unknown (4)
            int lblCount = pisdata[s] | (pisdata[s+1] << 8) | (pisdata[s+2] << 16) | (pisdata[s+3] << 24);
            s += 4;

            var labels   = new byte[lblCount][];
            var lblAddrs = new byte[lblCount][];
            for (int x = 0; x < lblCount; x++) { labels[x] = new byte[255]; lblAddrs[x] = new byte[8]; }

            ExtractLabels(pisdata, s, lblCount, labels, lblAddrs);

            return WriteMap(mapPath, labels, lblAddrs, lblCount)
                ? new PisConvertResult { Success = true, LabelCount = lblCount }
                : Fail($"Could not write map file '{mapPath}'.");
        }

        private static void ExtractLabels(byte[] data, int start, int count, byte[][] labels, byte[][] addrs)
        {
            int s = start;
            for (int i = 0; i < count; i++)
            {
                uint addr = (uint)(data[s] | (data[s+1] << 8) | (data[s+2] << 16) | (data[s+3] << 24));
                addrs[i] = Encoding.ASCII.GetBytes(addr.ToString("X8"));
                s += 4;
                int len = data[s++];
                for (int j = 0; j < len; j++) labels[i][j] = data[s + j];
                labels[i][len] = 0;
                s += len;
            }
        }

        private static bool WriteMap(string path, byte[][] labels, byte[][] addrs, int count)
        {
            byte[] crlf = { 0x0D, 0x0A };
            try
            {
                using var f = File.Open(path, FileMode.Create, FileAccess.Write);
                f.Write(Encoding.ASCII.GetBytes("# pis2map v1.0")); f.Write(crlf);
                f.Write(Encoding.ASCII.GetBytes("# Created by: Gtlcpimp")); f.Write(crlf);
                for (int i = 0; i < count; i++)
                {
                    f.Write(addrs[i]); f.Write(Encoding.ASCII.GetBytes(":")); f.Write(crlf);
                    f.Write(Encoding.ASCII.GetBytes(".code\t"));
                    int len = 0; while (len < 255 && labels[i][len] != 0) len++;
                    f.Write(labels[i], 0, len); f.Write(crlf);
                }
                return true;
            }
            catch { return false; }
        }

        private static PisConvertResult Fail(string error) =>
            new PisConvertResult { Success = false, Error = error };
    }
}
