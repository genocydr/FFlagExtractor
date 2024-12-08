using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace RobloxClientTracker
{
    // Placeholder class for DataMiner - adjust as needed
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

        private const string SHOW_EVENT = "StudioNoSplashScreen";
        private const string START_EVENT = "ClientTrackerFlagScan";

        /// <summary>Looks for the next occurrence of a sequence in a byte array</summary>
        private static int findSequence(byte[] array, int start, byte[] sequence)
        {
            int end = array.Length - sequence.Length; // past here no match is possible
            byte firstByte = sequence[0]; // cached to tell compiler there's no aliasing

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

        private void ScanFlagsUsingInstructions(HashSet<string> flags)
        {
            // Set the path to your Roblox Studio executable here
            string studioPath = @"S:\downloadhacks2\common-WindowsStudio64-version-6b2b01086f654c24\RobloxStudioBeta - Copy.exe";
            var binary = File.ReadAllBytes(studioPath);

            List<int> knownAddresses = new List<int>
    {
        findSequence(binary, 0, Encoding.UTF8.GetBytes("DebugDisplayFPS")),
        findSequence(binary, 0, Encoding.UTF8.GetBytes("DebugGraphicsPreferVulkan")),
        findSequence(binary, 0, Encoding.UTF8.GetBytes("DebugGraphicsPreferD3D11"))
    };

            knownAddresses = knownAddresses.Where(x => x != -1).ToList();

            if (knownAddresses.Count < 2)
                throw new Exception("Could not find address(es) of known flag");

            int position = 0;
            var possibleOffsets = new Dictionary<int, int>();
            int knownLeaInstAddr = 0;

            while (position < binary.Length)
            {
                int leaInstAddr = findSequence(binary, position, new byte[] { 0x48, 0x8D, 0x0D });

                if (leaInstAddr == -1)
                    break;

                if (binary[leaInstAddr + 7] != 0xE9)
                {
                    position = leaInstAddr + 3;
                    continue;
                }

                int leaTargetAddr = ResolveInstTargetAddr(binary, leaInstAddr);

                for (int i = 0; i > -0xFF00; i -= 0x0100)
                {
                    foreach (int knownAddress in knownAddresses)
                    {
                        if (leaTargetAddr + i != knownAddress)
                            continue;

                        if (possibleOffsets.ContainsKey(i))
                            possibleOffsets[i]++;
                        else
                            possibleOffsets.Add(i, 1);

                        if (knownLeaInstAddr == 0)
                            knownLeaInstAddr = leaInstAddr;
                    }
                }

                position = leaInstAddr + 3;
            }

            Console.WriteLine("Finished scanning binary");

            var validOffsets = possibleOffsets.Where(x => x.Value == knownAddresses.Count);

            if (validOffsets.Count() != 1)
                throw new Exception("Could not find correct data address offset");

            int dataAddrOffset = validOffsets.First().Key;

            int jmpTargetAddr = ResolveInstTargetAddr(binary, knownLeaInstAddr + 7);

            var typeAddresses = new Dictionary<int, string>
    {
        { jmpTargetAddr,                   "FFlag"   },
        { jmpTargetAddr + 0x40,            "SFFlag"  },
        { jmpTargetAddr + 0x40 + 0x20,     "FInt"    },
        { jmpTargetAddr + 0x40 + 0x20 * 2, "FLog"    },
        { jmpTargetAddr + 0x40 + 0x20 * 3, "FString" },
    };

            position = 0;
            while (position < binary.Length)
            {
                int jmpInstAddr = findSequence(binary, position, new byte[] { 0xE9 });

                if (jmpInstAddr == -1)
                    break;

                jmpTargetAddr = ResolveInstTargetAddr(binary, jmpInstAddr);

                if (!typeAddresses.TryGetValue(jmpTargetAddr, out string flagType))
                {
                    position = jmpInstAddr + 1;
                    continue;
                }

                int targetLeaAddress = ResolveInstTargetAddr(binary, jmpInstAddr - 7, dataAddrOffset);

                string flagName = "";

                if (flagType != "SFFlag" && binary[jmpInstAddr - 18] == 0x2)
                    flagName += 'D';

                flagName += flagType;

                for (int i = targetLeaAddress; binary[i] != 0; i++)
                {
                    if (binary[i] < 0x20 || binary[i] > 0x7F)
                        throw new Exception("Encountered invalid data");

                    flagName += Convert.ToChar(binary[i]);
                }

                flags.Add($"[C++] {flagName}");

                position = jmpInstAddr + 1;
            }

            foreach (string flagType in typeAddresses.Values)
            {
                if (!flags.Where(x => x.StartsWith($"[C++] {flagType}")).Any())
                {
                    Console.WriteLine($"No C++ {flagType}s were detected.");
                }
            }
        }


        public override void ExecuteRoutine()
        {
            string extraContent = @"pathtostudiohere\RobloxStudioBeta.exe";

            var flags = new HashSet<string>();
            var timer = new Stopwatch();

            Console.WriteLine("Starting FastVariable scan...");

            timer.Start();
            Console.WriteLine("Scanning Lua flags...");

            foreach (var file in Directory.GetFiles(extraContent, "*.lua", SearchOption.AllDirectories))
            {
                string contents = File.ReadAllText(file);
                var matches = Regex.Matches(contents, "game:(?:Get|Define)Fast(Flag|Int|String)\\(\\\"(\\w+)\\\"").Cast<Match>();

                foreach (var match in matches)
                    flags.Add(string.Format("[Lua] F{0}{1}", match.Groups[1], match.Groups[2]));
            }

            Console.WriteLine("Scanning C++ flags...");
            ScanFlagsUsingInstructions(flags);

            timer.Stop();
            Console.WriteLine($"FastVariable scan completed in {timer.Elapsed} with {flags.Count} variables");

            var sortedFlags = flags.OrderBy(x => x.Substring(6)).ToList();
            string flagsPath = @"pathtofvariableshere\FVariables.txt";

            string result = string.Join("\r\n", sortedFlags);
            File.WriteAllText(flagsPath, result);
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
