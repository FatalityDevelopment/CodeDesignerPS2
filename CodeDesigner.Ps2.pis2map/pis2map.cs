using CodeDesigner.Ps2.pis2map;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace CodeDesigner.Ps2.pis2map
{
    class Program
    {
        static int Main(string[] args)
        {
            int i = 0;
            int i2 = 0;
            int fpis_sz;
            int foundit = 0;
            int s;
            int lblCount;
            int lblSize;

            if (args.Length < 2)
            {
                Console.Write("Input and output files needed.");
            }
            if (args.Length >= 2)
            {
                FileStream fpis = null;
                try { fpis = File.Open(args[0], FileMode.Open, FileAccess.Read); } catch { }

                if (fpis != null)
                {
                    Console.WriteLine($"Reading [{args[0]}]...");

                    fpis_sz = (int)fpis.Length;
                    byte[] pisdata = new byte[fpis_sz];

                    i = 0;
                    i2 = 0;
                    while (i2 < fpis_sz)
                    {
                        i = fpis.Read(pisdata, i2, fpis_sz - i2);
                        i2 += i;
                        if (i <= 0) break;
                    }
                    fpis.Close();

                    if ((pisdata[0] != 0x50 || pisdata[1] != 0x49) || (pisdata[2] != 0x53 || pisdata[3] != 0x1F))
                    {
                        Console.WriteLine("File not a .pis");
                        return -1;
                    }
                    Console.WriteLine($".pis loaded; Size: {fpis_sz:X8}");

                    Console.WriteLine("Scanning for label definition...");

                    foundit = 0;
                    i = fpis_sz - 1;
                    while (i > 0 && foundit == 0)
                    {
                        if ((pisdata[i] == 0x11) && (pisdata[i + 1] == 0x11) && (pisdata[i + 2] == 0x11) && (pisdata[i + 3] == 0x11) &&
                            (pisdata[i + 4] == 0x11) && (pisdata[i + 5] == 0x11) && (pisdata[i + 6] == 0x11) && (pisdata[i + 7] == 0x11))
                        {
                            Console.WriteLine($"Found Label Padding at {(i):X8}");
                            foundit = i + 8;
                        }
                        i--;
                    }

                    if (foundit <= 0)
                    {
                        Console.WriteLine("Did not locate anything");
                        return -1;
                    }

                    s = foundit;
                    lblSize = (int)((int)pisdata[s] + 
                                    ((int)pisdata[s + 1] * 0x100) + 
                                    ((int)pisdata[s + 2] * 0x10000) + 
                                    ((int)pisdata[s + 3] * 0x1000000));
                    s += 8;
                    lblCount = (int)((int)pisdata[s] + 
                                    ((int)pisdata[s + 1] * 0x100) + 
                                    ((int)pisdata[s + 2] * 0x10000) + 
                                    ((int)pisdata[s + 3] * 0x1000000));
                    s += 4;


                    Console.WriteLine($"Label Size: {lblSize:X8}\nLabel Count: {lblCount:X8}");

                    byte[][] labels = new byte[lblCount][];
                    for (int x = 0; x < lblCount; x++) labels[x] = new byte[255];
                    byte[][] lblAddrs = new byte[lblCount][];
                    for (int x = 0; x < lblCount; x++) lblAddrs[x] = new byte[8];

                    ExtractLabels(pisdata, s, lblCount, lblSize, labels, lblAddrs);

                    Console.Write("\b\b\b\b\b\b\b\b\b\b\b\b\b\b\bExtraction complete\n");
                    Console.WriteLine("Writing map file...");

                    i = WriteMAP(args[1], labels, lblAddrs, lblCount);
                    if (i > 0)
                        Console.WriteLine("Map file saved successfully");
                    else
                        Console.WriteLine("Map file save error.");
                }
                if (fpis == null)
                {
                    Console.Write($"Error opening {args[0]}");
                }
            }

            return 0;
        }

        static void ExtractLabels(byte[] pisdata, int lstart, int lcount, int lsize, byte[][] labels, byte[][] lblAddrs)
        {
            int lblSize, lblCount;
            int s = lstart;

            lblSize = lsize;
            lblCount = lcount;

            for (int i = 0; i < lblCount; i++)
            {
                uint addr = (uint)(pisdata[s] +
                                  (pisdata[s + 1] * 0x100) +
                                  (pisdata[s + 2] * 0x10000) +
                                  (pisdata[s + 3] * 0x1000000));
                lblAddrs[i] = addr.ToString("X8").Select(c => (byte)c).ToArray();

                s += 4;

                lblSize = pisdata[s];

                s++;

                CopyLabelText(pisdata, s, labels[i], lblSize);

                s += lblSize;

                Console.Write($"\r{i}/{lblCount}");
            }
        }

        static int WriteMAP(string mapName, byte[][] labels, byte[][] lblAddrs, int lc)
        {
            byte[] rtline = new byte[] { 0x0D, 0x0A };

            FileStream fmap = null;
            try { fmap = File.Open(mapName, FileMode.Create, FileAccess.Write); } catch { }

            if (fmap != null)
            {
                fmap.Write(Encoding.ASCII.GetBytes("# pis2map v1.0"), 0, 14);
                fmap.Write(rtline, 0, 2);
                fmap.Write(Encoding.ASCII.GetBytes("# Created by: Gtlcpimp"), 0, 22);
                fmap.Write(rtline, 0, 2);

                for (int i = 0; i < lc; i++)
                {
                    fmap.Write(lblAddrs[i], 0, 8);
                    fmap.Write(Encoding.ASCII.GetBytes(":"), 0, 1);
                    fmap.Write(rtline, 0, 2);
                    fmap.Write(Encoding.ASCII.GetBytes(".code\t"), 0, 6);
                    int lblLen = 0;
                    while (lblLen < 255 && labels[i][lblLen] != 0x00) lblLen++;
                    fmap.Write(labels[i], 0, lblLen);
                    fmap.Write(rtline, 0, 2);
                }

                fmap.Close();
                return 1;
            }
            return 0;
        }

        static void CopyLabelText(byte[] from, int offset, byte[] to, int c)
        {
            for (int i = 0; i < c; i++)
                to[i] = from[offset + i];
            to[c] = 0x00;
        }

    }
}