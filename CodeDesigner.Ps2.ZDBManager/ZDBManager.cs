using CodeDesigner.Ps2.ZDBManager;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System;
using System.Collections.Generic;
using System.Text;

namespace CodeDesigner.Ps2.ZDBManager
{
    public class Program
    {
        static ZDBArchive zdb = new ZDBArchive();
        static string currentDir = "z:/";

        public static void Main(string[] args)
        {
            if (args.Length > 0 && args[0].StartsWith("-"))
            {
                RunOperation(args);
                return;
            }

            Console.WriteLine("=================================================================");
            Console.WriteLine("#  _________  ____    __  __                                    #");
            Console.WriteLine("# |__  /  _ \\| __ )  |  \\/  | __ _ _ __   __ _  __ _  ___ _ __  #");
            Console.WriteLine("#   / /| | | |  _ \\  | |\\/| |/ _` | '_ \\ / _` |/ _` |/ _ \\ '__| #");
            Console.WriteLine("#  / /_| |_| | |_) | | |  | | (_| | | | | (_| | (_| |  __/ |    #");
            Console.WriteLine("# /____|____/|____/  |_|  |_|\\__,_|_| |_|\\__,_|\\__, |\\___|_|    #");
            Console.WriteLine("#                                              |___/            #");
            Console.WriteLine("#   Created by: Gtlcpimp                                        #");
            Console.WriteLine("=================================================================");

            Console.WriteLine("Type 'help' for commands.\n");

            while (true)
            {
                Console.Write($"{currentDir}> ");
                string input = Console.ReadLine();
                if (string.IsNullOrWhiteSpace(input))
                    continue;

                string[] cmd = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                RunInteractive(cmd);
            }
        }

        static void RunInteractive(string[] cmd)
        {
            switch (cmd[0])
            {
                case "help":
                    PrintHelp(0);
                    break;

                case "open":
                    if (cmd.Length < 2) { Console.WriteLine("Usage: open <archive>"); return; }
                    if (zdb.OpenArchive(cmd[1]) >= 0)
                        Console.WriteLine("Archive opened.");
                    else
                        Console.WriteLine("Failed to open archive.");
                    break;

                case "ls":
                    PrintDirectoryListing(0);
                    break;

                case "cd":
                    if (cmd.Length < 2) { Console.WriteLine("Usage: cd <dir>"); return; }
                    ChangeDir(cmd[1]);
                    break;

                case "info":
                    if (cmd.Length < 2) { Console.WriteLine("Usage: info <file>"); return; }
                    ShowInfo(cmd[1]);
                    break;

                case "extract":
                    if (cmd.Length < 3) { Console.WriteLine("Usage: extract <file> <dest>"); return; }
                    ExtractFile(cmd[1], cmd[2]);
                    break;

                case "add":
                    if (cmd.Length < 2) { Console.WriteLine("Usage: add <source_file>"); return; }
                    AddFile(cmd[1]);
                    break;

                case "addfolder":
                    if (cmd.Length < 2) { Console.WriteLine("Usage: addfolder <folder>"); return; }
                    AddFolder(cmd[1]);
                    break;

                case "replace":
                    if (cmd.Length < 3) { Console.WriteLine("Usage: replace <file> <source_file>"); return; }
                    ReplaceFile(cmd[1], cmd[2]);
                    break;

                case "del":
                    if (cmd.Length < 2) { Console.WriteLine("Usage: del <file>"); return; }
                    DeleteFile(cmd[1]);
                    break;

                case "deldir":
                    if (cmd.Length < 2) { Console.WriteLine("Usage: deldir <dir>"); return; }
                    DeleteDir(cmd[1]);
                    break;

                case "save":
                    if (cmd.Length < 2) { Console.WriteLine("Usage: save <dest>"); return; }
                    if (zdb.SaveArchive(cmd[1]))
                        Console.WriteLine("Archive saved.");
                    else
                        Console.WriteLine("Save failed.");
                    break;

                case "exit":
                    Environment.Exit(0);
                    break;

                default:
                    Console.WriteLine("Unknown command.");
                    break;
            }
        }

        static void RunOperation(string[] args)
        {
            string cmd = args[0];

            if (cmd == "-h")
            {
                PrintHelp(1);
                return;
            }

            if (cmd == "-ls" && args.Length == 2)
            {
                if (zdb.OpenArchive(args[1]) < 0)
                {
                    Console.WriteLine("Failed to open archive.");
                    return;
                }

                PrintDirectoryListing(1);
                return;
            }

            if (cmd == "-ls" && args.Length == 4 && args[1] == "-i")
            {
                if (zdb.OpenArchive(args[2]) >= 0)
                    ShowInfo(args[3]);
                return;
            }

            if (cmd == "-e" && args.Length == 4)
            {
                if (zdb.OpenArchive(args[1]) >= 0)
                    ExtractFile(args[2], args[3]);
                return;
            }

            if (cmd == "-e" && args.Length == 4 && args[1] == "-all")
            {
                if (zdb.OpenArchive(args[2]) >= 0)
                    ExtractAll(args[3]);
                return;
            }

            if (cmd == "-a" && args.Length == 4)
            {
                if (zdb.OpenArchive(args[1]) >= 0)
                {
                    AddFileTo(args[2], args[3]);
                    zdb.SaveArchive(args[1]);
                }
            return;
            }

            if (cmd == "-r" && args.Length == 4)
            {
                if (zdb.OpenArchive(args[1]) >= 0)
                {
                    ReplaceFile(args[2], args[3]);
                    zdb.SaveArchive(args[1]);
                }
                return;
            }

            if (cmd == "-d" && args.Length == 3)
            {
                if (zdb.OpenArchive(args[1]) >= 0)
                {
                    DeleteFile(args[2]);
                    zdb.SaveArchive(args[1]);
                }
                return;
            }

            if (cmd == "-d" && args.Length == 4 && args[1] == "-f")
            {
                if (zdb.OpenArchive(args[2]) >= 0)
                {
                    DeleteDir(args[3]);
                    zdb.SaveArchive(args[1]);
                }
                return;
            }

            Console.WriteLine("Invalid command.");
        }

        static void PrintHelp(int mode)
        {
            if (mode == 0)
            {
                Console.WriteLine("\n");
                Console.WriteLine("Commands:");
                Console.WriteLine("    help                        - Displays the help page");
                Console.WriteLine("    open <archive>              - Opens archive for browsing");
                Console.WriteLine("    ls                          - Lists the directory contents of the current zdb directory");
                Console.WriteLine("    info <file>                 - Shows information about a file in the current directory");
                Console.WriteLine("    cd <dir>                    - Changes current zdb directory (e.g. cd z:/folder/)");
                Console.WriteLine("    extract <file> <dest>       - Extracts file from current zdb directory to external destination");
                Console.WriteLine("    add <source_file>           - Injects file into the current zdb directory");
                Console.WriteLine("    addfolder <source_folder>   - Injects an entire folder tree into the current zdb directory");
                Console.WriteLine("    replace <file> <source>     - Replaces a zdb file with an external file");
                Console.WriteLine("    del <file>                  - Deletes a file from the current zdb directory");
                Console.WriteLine("    deldir <dir>                - Deletes a directory (and its contents) from the archive");
                Console.WriteLine("    save <dest>                 - Saves the archive to the given destination path");
                Console.WriteLine("    exit                        - Exits the program");
                Console.WriteLine("\n");
            }
            else if (mode == 1)
            {
                Console.WriteLine("Commands:");
                Console.WriteLine("    -h                                  - Displays the help page");
                Console.WriteLine("    -ls <archive>                       - Lists the entire directory tree of the archive");
                Console.WriteLine("    -ls -i <archive> <archive_file>     - Shows information about a specific file in the archive");
                Console.WriteLine("    -e <archive> <archive_file> <dest>  - Extracts a file from the archive");
                Console.WriteLine("    -e -all <archive> <dest_dir>        - Extracts all files from the archive");
                Console.WriteLine("    -a <archive> <source_file> <zdb_dir>- Adds a file into a specific directory in the archive");
                Console.WriteLine("    -r <archive> <zdb_file> <source>    - Replaces a zdb file with an external file");
                Console.WriteLine("    -d <archive> <zdb_file>             - Deletes a file from the archive");
                Console.WriteLine("    -d -f <archive> <zdb_folder>        - Deletes a folder (and its contents) from the archive");
            }
        }

        static void ChangeDir(string dir)
        {
            // Normalize slashes
            dir = dir.Replace("\\", "/");

            // Stay in current directory
            if (dir == ".")
                return;

            // Go up one directory
            if (dir == "..")
            {
                if (currentDir == "z:/")
                    return;

                string tmp = currentDir.TrimEnd('/');
                int lastSlash = tmp.LastIndexOf('/');

                if (lastSlash <= 1)
                    currentDir = "z:/";
                else
                    currentDir = tmp.Substring(0, lastSlash) + "/";

                return;
            }

            // Absolute path
            if (dir.StartsWith("z:/"))
            {
                if (!dir.EndsWith("/"))
                    dir += "/";

                currentDir = dir;
                return;
            }

            // Relative path
            if (!currentDir.EndsWith("/"))
                currentDir += "/";

            string newPath = currentDir + dir;
            newPath = newPath.Replace("//", "/");

            if (!newPath.EndsWith("/"))
                newPath += "/";

            currentDir = newPath;
        }

        static void PrintDirectoryListing(int mode)
        {
            string dir = (mode == 0 ? currentDir : "z:/");
            string listing = zdb.GetDIRListing(dir);
            string[] lines = listing.Split('\n');

            Console.WriteLine("");
            foreach (string raw in lines)
            {
                string line = raw.Trim();
                if (line.Length == 0)
                    continue;

                if (line.StartsWith("[FILE]"))
                {
                    string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 3)
                    {
                        string indexHex = parts[1];
                        string name = parts[2];

                        int index = Convert.ToInt32(indexHex, 16);

                        uint size = 0;
                        if (zdb.GetFileBytes(index, out var data))
                            size = (uint)data.Length;

                        string sizeStr = FormatSize(size);

                        Console.WriteLine($"{parts[0],-6} {indexHex,-6} {name,-30} {sizeStr,12}");
                    }
                    else
                    {
                        Console.WriteLine(line);
                    }
                }
                else
                {
                    Console.WriteLine(line);
                }
            }
            Console.WriteLine("");
        }


        static string FormatSize(uint size)
        {
            double s = size;

            if (s < 1024)
                return $"{s:0} B";

            s /= 1024;
            if (s < 1024)
                return $"{s:0.0} KB";

            s /= 1024;
            if (s < 1024)
                return $"{s:0.0} MB";

            s /= 1024;
            return $"{s:0.0} GB";
        }


        static void ShowInfo(string file)
        {
            string listing = zdb.GetDIRListing(currentDir);
            string[] lines = listing.Split('\n');

            foreach (string line in lines)
            {
                if (line.Contains(file))
                {
                    string hexIndex = line.Split(' ')[1];
                    int index = Convert.ToInt32(hexIndex, 16);
                    Console.WriteLine("");
                    Console.WriteLine(zdb.GetFileDetails(index));
                    return;
                }
            }

            Console.WriteLine("File not found.");
        }

        static void ExtractFile(string file, string dest)
        {
            string listing = zdb.GetDIRListing(currentDir);
            string[] lines = listing.Split('\n');

            foreach (string line in lines)
            {
                if (line.Contains(file))
                {
                    string hexIndex = line.Split(' ')[1];
                    int index = Convert.ToInt32(hexIndex, 16);
                    if (zdb.ExtractFile(dest, index))
                        Console.WriteLine("Extracted.");
                    else
                        Console.WriteLine("Failed.");
                    return;
                }
            }

            Console.WriteLine("File not found.");
        }

        static void ExtractAll(string destDir)
        {
            Directory.CreateDirectory(destDir);

            int count = zdb.GetFileCount();
            for (int i = 0; i < count; i++)
            {
                string name = zdb.GetFileName(i);
                string outPath = Path.Combine(destDir, name);
                zdb.ExtractFile(outPath, i);
            }

            Console.WriteLine("All files extracted.");
        }

        static void AddFile(string source)
        {
            string fileName = Path.GetFileName(source);

            string archivePath = currentDir.Replace("\\", "/");
            if (!archivePath.EndsWith("/"))
                archivePath += "/";

            archivePath += fileName;

            int ret = zdb.AddFile(source, archivePath, 0);

            if (ret == 0)
                Console.WriteLine("File added.");
            else if (ret == -1)
                Console.WriteLine("No archive is open.");
            else if (ret == -2)
                Console.WriteLine($"Failed to access '{source}'.");
        }


        static void AddFileTo(string source, string zdbDir)
        {
            string fileName = Path.GetFileName(source);

            string archivePath = zdbDir.Replace("\\", "/");
            if (!archivePath.EndsWith("/"))
                archivePath += "/";

            archivePath += fileName;

            int ret = zdb.AddFile(source, archivePath, 0);

            if (ret == 0)
                Console.WriteLine("File added.");
            else if (ret == -1)
                Console.WriteLine("No archive is open.");
            else if (ret == -2)
                Console.WriteLine($"Failed to access '{source}'.");
        }


        static void AddFolder(string folder)
        {
            foreach (string file in Directory.GetFiles(folder, "*", SearchOption.AllDirectories))
            {
                string rel = file.Substring(folder.Length).Replace("\\", "/");
                string zpath = currentDir + rel;
                zdb.AddFile(file, zpath, 0);
            }

            Console.WriteLine("Folder added.");
        }

        static void ReplaceFile(string file, string source)
        {
            string listing = zdb.GetDIRListing(currentDir);
            string[] lines = listing.Split('\n');

            foreach (string line in lines)
            {
                if (line.Contains(file))
                {
                    string hexIndex = line.Split(' ')[1];
                    int index = Convert.ToInt32(hexIndex, 16);

                    byte[] data = File.ReadAllBytes(source);
                    zdb.SetFileBytes(index, ref data);

                    Console.WriteLine("Replaced.");
                    return;
                }
            }

            Console.WriteLine("File not found.");
        }

        static void DeleteFile(string file)
        {
            zdb.DeleteFile(currentDir, file);
            Console.WriteLine("Deleted.");
        }

        static void DeleteDir(string dir)
        {
            zdb.DeleteFile(dir, "");
            Console.WriteLine("Directory deleted.");
        }
    }
}
