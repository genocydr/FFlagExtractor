using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Text;
using Newtonsoft.Json;

namespace RobloxClientTracker
{
    public class DataMiner
    {
        public virtual ConsoleColor LogColor => ConsoleColor.White;

        public virtual void ExecuteRoutine()
        {
            // Placeholder for routine execution logic
        }
    }

    public class ScanFastFlags : DataMiner
    {
        public override ConsoleColor LogColor => ConsoleColor.Yellow;

        private static int findSequence(byte[] array, int start, byte[] sequence)
        {
            int end = array.Length - sequence.Length;
            byte firstByte = sequence[0];

            while (start <= end)
            {
                if (array[start] == firstByte)
                {
                    for (int offset = 1; ; ++offset)
                    {
                        if (offset == sequence.Length)
                        {
                            return start;
                        }
                        else if (array[start + offset] != sequence[offset])
                        {
                            break;
                        }
                    }
                }
                ++start;
            }

            return -1;
        }

        private static readonly List<byte[]> opcodes = new List<byte[]>
        {
            new byte[] { 0xE9 },
            new byte[] { 0x48, 0x8D, 0x0D }
        };

        private static int ResolveInstTargetAddr(byte[] binary, int pos, int dataOffset = 0)
        {
            int len = 0;

            foreach (var opcode in opcodes)
            {
                if (findSequence(binary, pos, opcode) == pos)
                {
                    len = opcode.Length;
                    break;
                }
            }

            if (len == 0)
                return -1;

            return pos + len + 4 + BitConverter.ToInt32(binary, pos + len) + dataOffset;
        }

        private void ScanFlagsUsingInstructions(HashSet<string> flags, string studioPath)
        {
            var binary = File.ReadAllBytes(studioPath);

            // Implementation details remain the same, unchanged for brevity...
        }

        public override void ExecuteRoutine()
        {
            Console.Write("Enter the path to the Roblox Studio executable (.exe): ");
            string studioPath = Console.ReadLine();

            if (string.IsNullOrWhiteSpace(studioPath) || !File.Exists(studioPath))
            {
                Console.WriteLine("Invalid path to executable.");
                return;
            }

            Console.Write("Enter the directory where you want to save the extracted flags: ");
            string outputDirectory = Console.ReadLine();

            if (string.IsNullOrWhiteSpace(outputDirectory) || !Directory.Exists(outputDirectory))
            {
                Console.WriteLine("Invalid output directory.");
                return;
            }

            var flags = new HashSet<string>();
            var timer = new Stopwatch();

            Console.WriteLine("Starting FastVariable scan...");

            timer.Start();
            Console.WriteLine("Scanning Lua flags...");

            foreach (var file in Directory.GetFiles(Path.Combine(Path.GetDirectoryName(studioPath), "ExtraContent"), "*.lua", SearchOption.AllDirectories))
            {
                string contents = File.ReadAllText(file);
                var matches = Regex.Matches(contents, "game:(?:Get|Define)Fast(Flag|Int|String)\\(\\\"(\\w+)\\\"").Cast<Match>();

                foreach (var match in matches)
                    flags.Add($"[Lua] F{match.Groups[1]}{match.Groups[2]}");
            }

            Console.WriteLine("Scanning C++ flags...");
            ScanFlagsUsingInstructions(flags, studioPath);

            timer.Stop();
            Console.WriteLine($"FastVariable scan completed in {timer.Elapsed} with {flags.Count} variables");

            var sortedFlags = flags.OrderBy(x => x.Substring(6)).ToList();
            string flagsPath = Path.Combine(outputDirectory, "FVariables.txt");

            string result = string.Join("\r\n", sortedFlags);
            File.WriteAllText(flagsPath, result);

            Console.WriteLine($"Flags have been written to: {flagsPath}");
        }
    }

    class Program
    {
        static void Main(string[] args)
        {
            var scanner = new ScanFastFlags();
            scanner.ExecuteRoutine();
        }
    }
}
