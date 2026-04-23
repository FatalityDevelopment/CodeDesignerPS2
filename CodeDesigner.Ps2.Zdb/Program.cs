//using CodeDesigner.Ps2.ZDBManager;
//using System.Diagnostics;
//using System.Runtime.InteropServices;
//using System;
//using System.Collections.Generic;
//using System.Text;
//using CodeDesigner.Ps2.Compression;

//namespace CodeDesigner.Ps2.ZDBManager
//{
//    class Program
//    {
//        static ZdbArchive zdb = new ZdbArchive();
//        static string currentDir = "z:/";

//        static void Main(string[] args)
//        {
//            if (args.Length > 0 && args[0].StartsWith("-"))
//            {
//                RunOperation(args);
//                return;
//            }

//            Console.WriteLine("=================================================================");
//            Console.WriteLine("#  _________  ____    __  __                                    #");
//            Console.WriteLine("# |__  /  _ \\| __ )  |  \\/  | __ _ _ __   __ _  __ _  ___ _ __  #");
//            Console.WriteLine("#   / /| | | |  _ \\  | |\\/| |/ _` | '_ \\ / _` |/ _` |/ _ \\ '__| #");
//            Console.WriteLine("#  / /_| |_| | |_) | | |  | | (_| | | | | (_| | (_| |  __/ |    #");
//            Console.WriteLine("# /____|____/|____/  |_|  |_|\\__,_|_| |_|\\__,_|\\__, |\\___|_|    #");
//            Console.WriteLine("#                                              |___/            #");
//            Console.WriteLine("#   Created by: mbacker80 & TylerDev888                          #");
//            Console.WriteLine("=================================================================");

//            Console.WriteLine("Type 'help' for commands.\n");

//            while (true)
//            {
//                Console.Write($"{currentDir}> ");
//                string input = Console.ReadLine() ?? string.Empty;
//                if (string.IsNullOrWhiteSpace(input))
//                    continue;

//                string[] cmd = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
//                RunInteractive(cmd);
//            }
//        }

//        static void RunInteractive(string[] cmd)
//        {
//            switch (cmd[0])
//            {
//                case "help":
//                    PrintHelp(0);
//                    break;

//                case "open":
//                    zdb.ExtractAll("C:\\Users\\tholu\\Desktop\\Patch");
//                    break;

//                case "exit":
//                    Environment.Exit(0);
//                    break;

//                default:
//                    Console.WriteLine("Unknown command.");
//                    break;
//            }
//        }



//        static void PrintHelp(int mode)
//        {
//            if (mode == 0)
//            {
//                Console.WriteLine("\n");
//                Console.WriteLine("Commands:");
//                Console.WriteLine("    help                        - Displays the help page");
//                Console.WriteLine("    open <archive>              - Opens archive for browsing");
//                Console.WriteLine("    ls                          - Lists the directory contents of the current zdb directory");
//                Console.WriteLine("    info <file>                 - Shows information about a file in the current directory");
//                Console.WriteLine("    cd <dir>                    - Changes current zdb directory (e.g. cd z:/folder/)");
//                Console.WriteLine("    extract <file> <dest>       - Extracts file from current zdb directory to external destination");
//                Console.WriteLine("    add <source_file>           - Injects file into the current zdb directory");
//                Console.WriteLine("    addfolder <source_folder>   - Injects an entire folder tree into the current zdb directory");
//                Console.WriteLine("    replace <file> <source>     - Replaces a zdb file with an external file");
//                Console.WriteLine("    del <file>                  - Deletes a file from the current zdb directory");
//                Console.WriteLine("    deldir <dir>                - Deletes a directory (and its contents) from the archive");
//                Console.WriteLine("    save <dest>                 - Saves the archive to the given destination path");
//                Console.WriteLine("    exit                        - Exits the program");
//                Console.WriteLine("\n");
//            }
//            else if (mode == 1)
//            {
//                Console.WriteLine("Commands:");
//                Console.WriteLine("    -h                                  - Displays the help page");
//                Console.WriteLine("    -ls <archive>                       - Lists the entire directory tree of the archive");
//                Console.WriteLine("    -ls -i <archive> <archive_file>     - Shows information about a specific file in the archive");
//                Console.WriteLine("    -e <archive> <archive_file> <dest>  - Extracts a file from the archive");
//                Console.WriteLine("    -e -all <archive> <dest_dir>        - Extracts all files from the archive");
//                Console.WriteLine("    -a <archive> <source_file> <zdb_dir>- Adds a file into a specific directory in the archive");
//                Console.WriteLine("    -r <archive> <zdb_file> <source>    - Replaces a zdb file with an external file");
//                Console.WriteLine("    -d <archive> <zdb_file>             - Deletes a file from the archive");
//                Console.WriteLine("    -d -f <archive> <zdb_folder>        - Deletes a folder (and its contents) from the archive");
//            }
//        }
//    }
//}
