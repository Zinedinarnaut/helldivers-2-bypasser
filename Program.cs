using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace HD2_GGKiller
{
    internal class Program
    {
        [DllImport("kernel32.dll", EntryPoint = "OpenProcess")]
        public static extern IntPtr OpenProcess(int dwDesiredAccess, bool bInheritHandle, int dwProcessId);

        [DllImport("kernel32.dll", EntryPoint = "ReadProcessMemory")]
        public static extern bool ReadProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, IntPtr lpBuffer, int nSize, IntPtr lpNumberOfBytesRead);

        [DllImport("kernel32.dll", EntryPoint = "WriteProcessMemory")]
        public static extern bool WriteProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, int nSize, IntPtr lpNumberOfBytesWritten);

        [DllImport("kernel32.dll")]
        private static extern void CloseHandle(IntPtr hObject);

        static readonly string game_name = "helldivers2";
        static readonly string steam_app_id = "553850";
        static readonly int gg_init_error_offset = 0x49EAE;
        static readonly int gg_stdown_error_offset = 0x49F52;
        static readonly byte[] check_mem = new byte[] { 0x0F, 0x85, 0xC1, 0x00, 0x00, 0x00 };
        static readonly byte[] gg_init_error_mem = new byte[] { 0x90, 0x90, 0x90, 0x90, 0x90, 0x90 };
        static readonly byte[] gg_stdown_error_mem = new byte[] { 0x90, 0x90 };

        static readonly int[] gg_check_offsets = { 0x49679, 0x496C7, 0x49F48, 0x761FF };
        static readonly byte[] gg_check_mem = new byte[] { 0xB8, 0x55, 0x07, 0x00, 0x00 };

        static void Main(string[] args)
        {
            string gameDirectory = FindGameDirectory();
            if (string.IsNullOrEmpty(gameDirectory))
            {
                Console.WriteLine("Could not find the game directory.");
                return;
            }

            Console.WriteLine($"Found game directory: {gameDirectory}");

            #region Find Process
            Console.WriteLine("===Find Process===");
            Console.WriteLine("Searching for the game process");
            Process[] processes = Process.GetProcessesByName(game_name);
            if (processes.Length > 0)
            {
                Console.WriteLine("Game process detected, terminating the game");
                processes[0].Kill();
                Console.WriteLine("Termination successful");
            }
            else Console.WriteLine("Game process not started");
            #endregion

            #region Start Process
            Console.WriteLine("===Start Process===");
            Console.WriteLine("Starting the process via Steam");
            StartSteamGame();
            Console.WriteLine("Start request sent");
            processes = Process.GetProcessesByName(game_name);
            Console.WriteLine("Waiting for the game to start");
            while (processes.Length <= 0)
            {
                Thread.Sleep(200);
                processes = Process.GetProcessesByName(game_name);
            }
            #endregion

            #region Check Memory
            Console.WriteLine("===Check Memory===");
            while (!CheckHD2Memory())
            {
                Console.WriteLine("Memory mismatch, waiting for next comparison");
                Thread.Sleep(200);
            };
            #endregion

            #region Modify Memory
            Console.WriteLine("===Remove GG Check===");
            WriteHD2Memory(gg_init_error_offset, gg_init_error_mem);
            Console.WriteLine("\n===Remove Shutdown Check===");
            WriteHD2Memory(gg_stdown_error_offset, gg_stdown_error_mem);
            Console.WriteLine("\n===Remove GG Detection===");
            foreach (int offset in gg_check_offsets)
                WriteHD2Memory(offset, gg_check_mem);
            #endregion

            Console.WriteLine("Modification successful, exiting soon");
            Thread.Sleep(3000);

#if DEBUG
            while (true) Thread.Sleep(2000);
#endif
        }

        static string FindGameDirectory()
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (drive.DriveType == DriveType.Fixed || drive.DriveType == DriveType.Removable)
                {
                    string gamePath = Path.Combine(drive.RootDirectory.FullName, "SteamLibrary", "steamapps", "common", "Helldivers 2", "bin");
                    if (Directory.Exists(gamePath))
                    {
                        return gamePath;
                    }
                }
            }
            return null;
        }

        static void StartSteamGame()
        {
            string steamPath = FindSteamExecutable();
            if (string.IsNullOrEmpty(steamPath))
            {
                Console.WriteLine("Could not find Steam executable.");
                return;
            }

            try
            {
                Process.Start(steamPath, $"-applaunch {steam_app_id}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error starting Steam game: {ex.Message}");
            }
        }

        static string? FindSteamExecutable()
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (drive.DriveType == DriveType.Fixed || drive.DriveType == DriveType.Removable)
                {
                    string steamPath = Path.Combine(drive.RootDirectory.FullName, "Program Files (x86)", "Steam", "steam.exe");
                    if (File.Exists(steamPath))
                    {
                        return steamPath;
                    }
                }
            }
            return null;
        }

        static bool CheckHD2Memory()
        {
            byte[] buffer = new byte[check_mem.Length];
            while (!ReadMemory(game_name, gg_init_error_offset, buffer, buffer.Length))
            {
                Thread.Sleep(200);
                continue;
            }

            Console.WriteLine("Current <--> Target");
            for (int i = 0; i < buffer.Length; i++)
            {
                Console.WriteLine("{0:X2} <--> {1:X2}", buffer[i], check_mem[i]);
                if (buffer[i] != check_mem[i])
                    return false;
            }
            Console.WriteLine("Memory match successful\n");

            return true;
        }

        static void WriteHD2Memory(int offset, byte[] target)
        {
            byte[] buffer = new byte[target.Length];
            while (!ReadMemory(game_name, offset, buffer, buffer.Length))
            {
                Thread.Sleep(200);
                continue;
            }

            Console.Write("Current memory: ");
            foreach (byte b in buffer)
            {
                Console.Write("0x{0:X2} ", b);
            }

            Console.WriteLine("\n");

            while (!WriteMemory(game_name, offset, target))
            {
                Thread.Sleep(200);
                continue;
            }
            Console.WriteLine("Modification successful\n");

            while (!ReadMemory(game_name, offset, buffer, buffer.Length))
            {
                Thread.Sleep(200);
                continue;
            }

            Console.Write("Current memory: ");
            foreach (byte b in buffer)
            {
                Console.Write("0x{0:X2} ", b);
            }
            Console.WriteLine("\n");
        }

        static bool ReadMemory(string processName, int offsetAddress, byte[] memoryBytes, int size)
        {
            Process[] processes = Process.GetProcessesByName(processName);
            int processId;
            if (processes.Length > 0)
            {
                processId = processes[0].Id;
            }
            else
            {
                Console.WriteLine($"Process \"{processName}\" not started, waiting to start {DateTime.Now}");
                return false;
            }

            byte[] buffer = new byte[size];
            try
            {
                IntPtr byteAddress = Marshal.UnsafeAddrOfPinnedArrayElement(memoryBytes, 0);
                IntPtr hProcess = OpenProcess(0x1F0FFF, false, processId);
                IntPtr baseAddress = processes[0].MainModule.BaseAddress;
                IntPtr address = baseAddress + offsetAddress;
                bool result = ReadProcessMemory(hProcess, address, byteAddress, size, IntPtr.Zero);
                CloseHandle(hProcess);

                return result;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error reading memory: {ex.Message}");
                return false;
            }
        }

        static bool WriteMemory(string processName, int offsetAddress, byte[] memoryBytes)
        {
            Process[] processes = Process.GetProcessesByName(processName);
            int processId;
            if (processes.Length > 0)
            {
                processId = processes[0].Id;
            }
            else
            {
                Console.WriteLine($"Process \"{processName}\" not started, waiting to start {DateTime.Now}");
                return false;
            }

            try
            {
                IntPtr hProcess = OpenProcess(0x1F0FFF, false, processId);
                IntPtr baseAddress = processes[0].MainModule.BaseAddress;
                IntPtr address = baseAddress + offsetAddress;
                bool result = WriteProcessMemory(hProcess, address, memoryBytes, memoryBytes.Length, IntPtr.Zero);
                CloseHandle(hProcess);
                return result;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error writing memory: {ex.Message}");
                return false;
            }
        }
    }
}
