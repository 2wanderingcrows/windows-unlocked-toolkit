// Windows Unlocked Toolkit  (tray app)
// ---------------------------------------------------------------------------
// One icon in the system tray for every tool in the toolkit:
//   - left-click: a small menu with quick switches (Leanback mode, controller mouse, Media Tab Jump, glow, AutoZoom)
//   - "Open settings": a window with a page per tool, plus appearance and updates
//
// The tools keep running as their own small background apps, so a problem in one can't take down the
// others. This app only reads their status files and edits their settings files:
//   Leanback Mode    %LOCALAPPDATA%\LeanbackMode\settings.ini (it reloads on save), status.ini (it writes)
//   Controller mouse %LOCALAPPDATA%\antimicrox\profiles\controller-mouse.amgp (AntiMicroX layout)
//   Media Tab Jump   %LOCALAPPDATA%\MediaTabJump\settings.ini and glow-off.txt
//   AutoZoom         %LOCALAPPDATA%\AutoZoom\settings.ini (it reloads on save)
// Its own settings and log are in %LOCALAPPDATA%\WindowsUnlockedToolkit.
// Nothing here goes online. "Get the latest version" and the GitHub buttons just open your browser.
//
// Built on your PC by Install.ps1 with the C# compiler that ships with Windows (.NET Framework 4.x, C# 5).
// The window layouts (Theme.xaml, MainWindow.xaml, Flyout.xaml) are embedded with /resource.
//
// Options:
//   --settings            start and open the settings window (used right after install)
//   --exit                close the running copy (used by the installer and uninstaller)
//   --selftest <folder> [layout.amgp]   render every page to PNG with sample data and check bindings, then exit
// ---------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml;
using Microsoft.Win32;
using WF = System.Windows.Forms;
using SD = System.Drawing;

namespace UnlockedToolkit
{
    // ------------------------------------------------------------------ entry point

    static class Program
    {
        public const string Version = "1.2";
        public const string Site = "https://2wanderingcrows.github.io/windows-unlocked-toolkit/";
        public const string Repo = "https://github.com/2wanderingcrows/windows-unlocked-toolkit";
        public const string ShowEventName = "Local\\WindowsUnlockedToolkit_Show";
        public const string ExitEventName = "Local\\WindowsUnlockedToolkit_Exit";

        [STAThread]
        static int Main(string[] args)
        {
            string first = args.Length > 0 ? args[0].ToLowerInvariant() : "";
            if (first == "--selftest") return SelfTest.Run(args.Length > 1 ? args[1] : ".", args.Length > 2 ? args[2] : null);
            if (first == "--exit") return AskToExit();

            bool created;
            var mutex = new Mutex(true, "Local\\WindowsUnlockedToolkit", out created);
            if (!created)
            {
                Signal(ShowEventName); // already running: open its settings window instead
                return 0;
            }
            try
            {
                var app = new Application();
                app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                app.DispatcherUnhandledException += delegate(object s, DispatcherUnhandledExceptionEventArgs e)
                {
                    Log.Write("error: " + e.Exception);
                    e.Handled = true;
                };
                Ui.LoadTheme(app);
                var shell = new Shell(false);
                shell.Start(first == "--settings");
                app.Run();
            }
            catch (Exception ex)
            {
                Log.Write("fatal: " + ex);
                WF.MessageBox.Show("Windows Unlocked Toolkit couldn't start:\n\n" + ex.Message, "Windows Unlocked Toolkit");
                return 1;
            }
            finally { GC.KeepAlive(mutex); }
            return 0;
        }

        static void Signal(string name)
        {
            try
            {
                EventWaitHandle h;
                if (EventWaitHandle.TryOpenExisting(name, out h)) { h.Set(); h.Dispose(); }
            }
            catch { }
        }

        static int AskToExit()
        {
            Signal(ExitEventName);
            int self = Process.GetCurrentProcess().Id;
            var deadline = DateTime.Now.AddSeconds(8);
            while (DateTime.Now < deadline)
            {
                bool any = false;
                foreach (var p in Process.GetProcessesByName("WindowsUnlockedToolkit"))
                {
                    if (p.Id != self) any = true;
                    p.Dispose();
                }
                if (!any) return 0;
                Thread.Sleep(200);
            }
            return 1;
        }
    }

    // ------------------------------------------------------------------ files, processes, startup

    static class P
    {
        public static readonly string Local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        public static string Data { get { return Path.Combine(Local, "WindowsUnlockedToolkit"); } }
        public static string Settings { get { return Path.Combine(Data, "settings.ini"); } }
        public static string LogFile { get { return Path.Combine(Data, "log.txt"); } }
        public static string Parked { get { return Path.Combine(Data, "turned-off"); } } // startup entries of tools you turned off

        public static string LeanDir { get { return Path.Combine(Local, "LeanbackMode"); } }
        public static string LeanExe { get { return Path.Combine(LeanDir, "LeanbackMode.exe"); } }
        public static string LeanSettings { get { return Path.Combine(LeanDir, "settings.ini"); } }
        public static string LeanStatus { get { return Path.Combine(LeanDir, "status.ini"); } }

        public static string MtjDir { get { return Path.Combine(Local, "MediaTabJump"); } }
        public static string MtjExe { get { return Path.Combine(MtjDir, "MediaTabJump.exe"); } }
        public static string MtjSettings { get { return Path.Combine(MtjDir, "settings.ini"); } }
        public static string MtjGlowOff { get { return Path.Combine(MtjDir, "glow-off.txt"); } }

        public static string AzDir { get { return Path.Combine(Local, "AutoZoom"); } }
        public static string AzExe { get { return Path.Combine(AzDir, "AutoZoom.exe"); } }
        public static string AzSettings { get { return Path.Combine(AzDir, "settings.ini"); } }
    }

    static class Log
    {
        static readonly object gate = new object();
        public static void Write(string text)
        {
            lock (gate)
            {
                try
                {
                    Directory.CreateDirectory(P.Data);
                    var fi = new FileInfo(P.LogFile);
                    if (fi.Exists && fi.Length > 256 * 1024) { File.Delete(P.LogFile + ".old"); File.Move(P.LogFile, P.LogFile + ".old"); }
                    File.AppendAllText(P.LogFile, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + "  " + text + Environment.NewLine);
                }
                catch { }
            }
        }
    }

    // Simple key=value files. Update() changes only the keys given and keeps comments and everything else.
    static class Ini
    {
        public static Dictionary<string, string> Read(string path)
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (!File.Exists(path)) return d;
                foreach (var raw in File.ReadAllLines(path))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line[0] == '#' || line[0] == ';' || line[0] == '[') continue;
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    d[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
                }
            }
            catch { }
            return d;
        }

        public static Dictionary<string, string> Pairs(params string[] kv)
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i + 1 < kv.Length; i += 2) d[kv[i]] = kv[i + 1];
            return d;
        }

        public static bool Update(string path, Dictionary<string, string> values)
        {
            if (Shell.Demo) return true;
            for (int attempt = 0; attempt < 6; attempt++)
            {
                try
                {
                    var lines = File.Exists(path) ? new List<string>(File.ReadAllLines(path)) : new List<string>();
                    var done = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    for (int i = 0; i < lines.Count; i++)
                    {
                        string t = lines[i].Trim();
                        if (t.StartsWith("#") || t.StartsWith(";")) continue;
                        int eq = t.IndexOf('=');
                        if (eq <= 0) continue;
                        string k = t.Substring(0, eq).Trim(), v;
                        if (values.TryGetValue(k, out v)) { lines[i] = k + "=" + v; done.Add(k); }
                    }
                    foreach (var kv in values) if (!done.Contains(kv.Key)) lines.Add(kv.Key + "=" + kv.Value);
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    File.WriteAllText(path, string.Join(Environment.NewLine, lines) + Environment.NewLine);
                    return true;
                }
                catch (IOException) { Thread.Sleep(80); }
                catch (UnauthorizedAccessException) { Thread.Sleep(80); }
            }
            Log.Write("couldn't save " + path);
            return false;
        }

        public static string Get(Dictionary<string, string> d, string key, string fallback)
        {
            string v;
            return d.TryGetValue(key, out v) && v.Length > 0 ? v : fallback;
        }

        public static double Num(Dictionary<string, string> d, string key, double fallback)
        {
            double v;
            return double.TryParse(Get(d, key, ""), NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : fallback;
        }

        public static bool Bool(Dictionary<string, string> d, string key, bool fallback)
        {
            string v = Get(d, key, "").ToLowerInvariant();
            if (v == "") return fallback;
            return !(v == "0" || v == "false" || v == "off" || v == "no");
        }
    }

    static class Procs
    {
        public static bool Running(string name)
        {
            var list = Process.GetProcessesByName(name);
            foreach (var p in list) p.Dispose();
            return list.Length > 0;
        }

        public static void Kill(string name)
        {
            if (Shell.Demo) return;
            foreach (var p in Process.GetProcessesByName(name))
            {
                try { p.Kill(); p.WaitForExit(3000); } catch { }
                finally { p.Dispose(); }
            }
        }

        public static string Start(string exe, string args)
        {
            if (Shell.Demo) return null;
            try
            {
                var psi = new ProcessStartInfo(exe, args ?? "");
                psi.WorkingDirectory = Path.GetDirectoryName(exe);
                psi.UseShellExecute = false;
                Process.Start(psi).Dispose();
                return null;
            }
            catch (Exception ex) { Log.Write("couldn't start " + exe + ": " + ex.Message); return ex.Message; }
        }

        // Runs exe with args and waits (on a background thread; callers pass a "done" to come back to the UI).
        public static void RunAndWait(string exe, string args, int timeoutMs)
        {
            if (Shell.Demo) return;
            try
            {
                var psi = new ProcessStartInfo(exe, args ?? "");
                psi.WorkingDirectory = Path.GetDirectoryName(exe);
                psi.UseShellExecute = false;
                using (var p = Process.Start(psi)) p.WaitForExit(timeoutMs);
            }
            catch (Exception ex) { Log.Write("couldn't run " + exe + " " + args + ": " + ex.Message); }
        }

        public static void Open(string target)
        {
            if (Shell.Demo) return;
            try { var psi = new ProcessStartInfo(target); psi.UseShellExecute = true; Process.Start(psi).Dispose(); }
            catch (Exception ex) { Log.Write("couldn't open " + target + ": " + ex.Message); }
        }

        public static void Background(Action work, Action done)
        {
            var d = Dispatcher.CurrentDispatcher;
            ThreadPool.QueueUserWorkItem(delegate
            {
                try { work(); } catch (Exception ex) { Log.Write("error: " + ex.Message); }
                if (done != null) d.BeginInvoke(done);
            });
        }
    }

    // Turning a tool off parks its startup entry in %LOCALAPPDATA%\WindowsUnlockedToolkit\turned-off,
    // so it stays off after a restart; turning it on puts the entry back. Uninstall.ps1 puts all of them back.
    static class Startup
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        static string Folder { get { return Environment.GetFolderPath(Environment.SpecialFolder.Startup); } }

        public static bool ShortcutParked(string name) { return File.Exists(Path.Combine(P.Parked, name)); }

        public static void SetShortcut(string name, bool on)
        {
            if (Shell.Demo) return;
            string live = Path.Combine(Folder, name), parked = Path.Combine(P.Parked, name);
            try
            {
                if (on && File.Exists(parked))
                {
                    if (File.Exists(live)) File.Delete(parked); else File.Move(parked, live);
                }
                else if (!on && File.Exists(live))
                {
                    Directory.CreateDirectory(P.Parked);
                    if (File.Exists(parked)) File.Delete(parked);
                    File.Move(live, parked);
                }
            }
            catch (Exception ex) { Log.Write("couldn't change startup entry " + name + ": " + ex.Message); }
        }

        public static bool RunParked(string name) { return File.Exists(Path.Combine(P.Parked, name + ".run.txt")); }

        public static void SetRun(string name, bool on)
        {
            if (Shell.Demo) return;
            string parked = Path.Combine(P.Parked, name + ".run.txt");
            try
            {
                using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (on && File.Exists(parked))
                    {
                        k.SetValue(name, File.ReadAllText(parked).Trim());
                        File.Delete(parked);
                    }
                    else if (!on)
                    {
                        var cmd = k.GetValue(name) as string;
                        if (cmd == null) return;
                        Directory.CreateDirectory(P.Parked);
                        File.WriteAllText(parked, cmd);
                        k.DeleteValue(name, false);
                    }
                }
            }
            catch (Exception ex) { Log.Write("couldn't change startup entry " + name + ": " + ex.Message); }
        }

        public static string GetRunValue(string name)
        {
            try { using (var k = Registry.CurrentUser.OpenSubKey(RunKey)) return k == null ? null : k.GetValue(name) as string; }
            catch { return null; }
        }

        public static void SetRunValue(string name, string cmd)
        {
            if (Shell.Demo) return;
            try
            {
                using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (cmd == null) k.DeleteValue(name, false); else k.SetValue(name, cmd);
                }
            }
            catch (Exception ex) { Log.Write("couldn't change startup entry " + name + ": " + ex.Message); }
        }
    }

    // ------------------------------------------------------------------ screens
    // The connected screens, named the way Windows Settings names them ("LG TV SSCR2", "Built-in display").
    // The same code is in Leanback Mode, AutoZoom and the toolkit app, so all of them name screens alike.
    // Settings refer to a screen by Name; "Main" (or nothing) always means the main display.

    class ScreenInfo
    {
        public string Name = "";     // friendly name, made unique ("DELL P2419H", "DELL P2419H 2")
        public string Device = "";   // Windows' own name for it, e.g. \\.\DISPLAY2 (can change between restarts)
        public bool Primary;
        public int Left, Top, Right, Bottom;   // physical pixels
        public IntPtr Handle;        // HMONITOR
        public int Width { get { return Right - Left; } }
        public int Height { get { return Bottom - Top; } }
    }

    static class Screens
    {
        [StructLayout(LayoutKind.Sequential)] struct SRECT { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct MONITORINFOEX
        {
            public int cbSize; public SRECT rcMonitor; public SRECT rcWork; public uint dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szDevice;
        }
        [StructLayout(LayoutKind.Sequential)] struct SLUID { public uint Low; public int High; }
        [StructLayout(LayoutKind.Sequential)] struct SHEADER { public int type; public int size; public SLUID adapterId; public uint id; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct SOURCE_NAME { public SHEADER h; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string gdiName; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct TARGET_NAME
        {
            public SHEADER h; public uint flags; public uint outputTechnology; public ushort edidManufactureId; public ushort edidProductCodeId;
            public uint connectorInstance;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string monitorFriendlyDeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string monitorDevicePath;
        }
        delegate bool MonitorEnumProc(IntPtr monitor, IntPtr dc, ref SRECT rect, IntPtr data);

        [DllImport("user32.dll")] static extern bool EnumDisplayMonitors(IntPtr dc, IntPtr clip, MonitorEnumProc proc, IntPtr data);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFOEX info);
        [DllImport("user32.dll")] static extern int GetDisplayConfigBufferSizes(uint flags, out uint numPaths, out uint numModes);
        [DllImport("user32.dll")] static extern int QueryDisplayConfig(uint flags, ref uint numPaths, IntPtr paths, ref uint numModes, IntPtr modes, IntPtr topology);
        [DllImport("user32.dll")] static extern int DisplayConfigGetDeviceInfo(ref SOURCE_NAME p);
        [DllImport("user32.dll")] static extern int DisplayConfigGetDeviceInfo(ref TARGET_NAME p);

        public const string Main = "Main";

        public static bool IsMain(string name) { return string.IsNullOrEmpty(name) || name.Trim().Equals(Main, StringComparison.OrdinalIgnoreCase); }

        // Main display first, then left to right.
        public static List<ScreenInfo> List()
        {
            var names = FriendlyNames();
            var list = new List<ScreenInfo>();
            MonitorEnumProc proc = delegate(IntPtr h, IntPtr dc, ref SRECT r, IntPtr data)
            {
                var mi = new MONITORINFOEX();
                mi.cbSize = Marshal.SizeOf(typeof(MONITORINFOEX));
                if (!GetMonitorInfo(h, ref mi)) return true;
                var s = new ScreenInfo();
                s.Handle = h;
                s.Device = mi.szDevice ?? "";
                s.Primary = (mi.dwFlags & 1) != 0;
                s.Left = mi.rcMonitor.Left; s.Top = mi.rcMonitor.Top; s.Right = mi.rcMonitor.Right; s.Bottom = mi.rcMonitor.Bottom;
                string n;
                s.Name = names.TryGetValue(s.Device, out n) ? n : "";
                list.Add(s);
                return true;
            };
            try { EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, proc, IntPtr.Zero); } catch { }
            GC.KeepAlive(proc);
            list.Sort(delegate(ScreenInfo a, ScreenInfo b)
            {
                if (a.Primary != b.Primary) return a.Primary ? -1 : 1;
                return a.Left != b.Left ? a.Left.CompareTo(b.Left) : a.Top.CompareTo(b.Top);
            });
            var used = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < list.Count; i++)
            {
                string n = list[i].Name.Trim();
                if (n.Length == 0) n = "Screen " + (i + 1);
                if (n.Equals(Main, StringComparison.OrdinalIgnoreCase)) n = n + " screen";
                int count;
                used.TryGetValue(n, out count);
                used[n] = count + 1;
                list[i].Name = count == 0 ? n : n + " " + (count + 1);
            }
            return list;
        }

        // The screen a setting names. "Main" or empty -> the main display. Returns null if that screen isn't connected.
        public static ScreenInfo Find(List<ScreenInfo> list, string name)
        {
            if (IsMain(name))
            {
                foreach (var s in list) if (s.Primary) return s;
                return list.Count > 0 ? list[0] : null;
            }
            foreach (var s in list) if (s.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase)) return s;
            return null;
        }

        public static ScreenInfo FromHandle(List<ScreenInfo> list, IntPtr monitor)
        {
            foreach (var s in list) if (s.Handle == monitor) return s;
            return null;
        }

        // \\.\DISPLAY1 -> "LG ULTRAGEAR". Built-in laptop panels have no name of their own, so they're "Built-in display".
        static Dictionary<string, string> FriendlyNames()
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                const uint QDC_ONLY_ACTIVE_PATHS = 2;
                const int PathSize = 72, ModeSize = 64;
                uint np, nm;
                if (GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, out np, out nm) != 0 || np == 0) return d;
                IntPtr paths = Marshal.AllocHGlobal((int)np * PathSize), modes = Marshal.AllocHGlobal((int)Math.Max(nm, 1) * ModeSize);
                try
                {
                    if (QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, ref np, paths, ref nm, modes, IntPtr.Zero) != 0) return d;
                    for (int i = 0; i < np; i++)
                    {
                        IntPtr p = new IntPtr(paths.ToInt64() + i * PathSize);
                        var sn = new SOURCE_NAME();
                        sn.h.type = 1; sn.h.size = Marshal.SizeOf(typeof(SOURCE_NAME));
                        sn.h.adapterId.Low = (uint)Marshal.ReadInt32(p, 0); sn.h.adapterId.High = Marshal.ReadInt32(p, 4);
                        sn.h.id = (uint)Marshal.ReadInt32(p, 8);
                        if (DisplayConfigGetDeviceInfo(ref sn) != 0 || string.IsNullOrEmpty(sn.gdiName) || d.ContainsKey(sn.gdiName)) continue;
                        var tn = new TARGET_NAME();
                        tn.h.type = 2; tn.h.size = Marshal.SizeOf(typeof(TARGET_NAME));
                        tn.h.adapterId.Low = (uint)Marshal.ReadInt32(p, 20); tn.h.adapterId.High = Marshal.ReadInt32(p, 24);
                        tn.h.id = (uint)Marshal.ReadInt32(p, 28);
                        if (DisplayConfigGetDeviceInfo(ref tn) != 0) continue;
                        string name = (tn.monitorFriendlyDeviceName ?? "").Trim();
                        uint tech = tn.outputTechnology;
                        if (name.Length == 0 && (tech == 0x80000000u || tech == 11 || tech == 13)) name = "Built-in display";
                        d[sn.gdiName] = name;
                    }
                }
                finally { Marshal.FreeHGlobal(paths); Marshal.FreeHGlobal(modes); }
            }
            catch { }
            return d;
        }
    }

    // ------------------------------------------------------------------ the tools

    static class Leanback
    {
        public const string Shortcut = "Leanback Mode.lnk";
        const string ToggleEventName = "Local\\LeanbackMode_Toggle";

        public static bool Installed { get { return File.Exists(P.LeanExe); } }
        public static bool Enabled { get { return !Startup.ShortcutParked(Shortcut); } }

        public static void Start() { Procs.Start(P.LeanExe, ""); }

        // "--exit" switches back to desktop mode first, then closes.
        public static void Exit() { Procs.RunAndWait(P.LeanExe, "--exit", 15000); }

        public static bool Toggle()
        {
            if (Shell.Demo) return true;
            try
            {
                EventWaitHandle h;
                if (!EventWaitHandle.TryOpenExisting(ToggleEventName, out h)) return false;
                h.Set();
                h.Dispose();
                return true;
            }
            catch { return false; }
        }

        public static void ShowOwnIcon(bool show)
        {
            if (!File.Exists(P.LeanSettings)) return;
            Ini.Update(P.LeanSettings, Ini.Pairs("ShowTrayIcon", show ? "true" : "false"));
        }
    }

    static class Amx
    {
        public const string Shortcut = "AntiMicroX (controller mouse).lnk";

        public static string Exe
        {
            get
            {
                return Ini.Get(Ini.Read(P.LeanSettings), "AntiMicroXPath",
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"AntiMicroX\bin\antimicrox.exe"));
            }
        }

        public static string Profile
        {
            get { return Ini.Get(Ini.Read(P.LeanSettings), "AntiMicroXProfile", Path.Combine(P.Local, @"antimicrox\profiles\controller-mouse.amgp")); }
        }

        public static bool Installed { get { return File.Exists(Exe); } }
        public static bool Enabled { get { return !Startup.ShortcutParked(Shortcut); } }
        public static void Start() { Procs.Start(Exe, "--tray --profile \"" + Profile + "\""); }
        public static void Stop() { Procs.Kill("antimicrox"); }
    }

    static class Mtj
    {
        public const string RunName = "MediaTabJump";
        public static bool Installed { get { return File.Exists(P.MtjExe); } }
        public static bool Enabled { get { return !Startup.RunParked(RunName); } }
        public static void Start() { Procs.Start(P.MtjExe, ""); }
        public static void Stop() { Procs.Kill("MediaTabJump"); }
    }

    static class Az
    {
        public const string RunName = "AutoZoom";
        public static bool Installed { get { return File.Exists(P.AzExe); } }
        public static bool Enabled { get { return !Startup.RunParked(RunName); } }
        public static void Start() { Procs.Start(P.AzExe, ""); }
        public static void Stop() { Procs.Kill("AutoZoom"); }
    }

    // What the tools are doing right now. Built by Shell.Refresh about once a second.
    class Snap
    {
        public bool LeanInstalled, LeanRunning, LeanEnabled;
        public bool AmxInstalled, AmxRunning, AmxEnabled;
        public bool MtjInstalled, MtjRunning, MtjEnabled, GlowOn;
        public bool AzInstalled, AzRunning, AzEnabled;
        public Dictionary<string, string> Status = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public bool Tv { get { return LeanRunning && Ini.Get(Status, "Mode", "") == "TV"; } }
        public string Mouse { get { return Ini.Get(Status, "Mouse", ""); } }

        public string Key()
        {
            var sb = new StringBuilder();
            sb.Append(LeanInstalled).Append(LeanRunning).Append(LeanEnabled).Append(AmxInstalled).Append(AmxRunning).Append(AmxEnabled)
              .Append(MtjInstalled).Append(MtjRunning).Append(MtjEnabled).Append(GlowOn)
              .Append(AzInstalled).Append(AzRunning).Append(AzEnabled);
            foreach (var kv in Status) sb.Append('|').Append(kv.Key).Append('=').Append(kv.Value);
            return sb.ToString();
        }
    }

    // ------------------------------------------------------------------ controller mouse layout (AntiMicroX)

    class MouseAction
    {
        public string Id, Label, Mode, Code;
        public MouseAction(string id, string label, string mode, string code) { Id = id; Label = label; Mode = mode; Code = code; }
    }

    // Reads and edits the AntiMicroX layout (.amgp, XML). Codes are Qt key codes, mouse buttons 1-5.
    class MouseLayout
    {
        public static readonly MouseAction[] Actions =
        {
            new MouseAction("left", "Left click", "mousebutton", "1"),
            new MouseAction("right", "Right click", "mousebutton", "3"),
            new MouseAction("middle", "Middle click", "mousebutton", "2"),
            new MouseAction("enter", "Enter", "keyboard", "0x1000004"),
            new MouseAction("esc", "Escape", "keyboard", "0x1000000"),
            new MouseAction("space", "Space", "keyboard", "0x20"),
            new MouseAction("backspace", "Backspace", "keyboard", "0x1000003"),
            new MouseAction("tab", "Tab", "keyboard", "0x1000001"),
            new MouseAction("win", "Windows key", "keyboard", "0x1000022"),
            new MouseAction("back", "Browser back", "keyboard", "0x1000061"),
            new MouseAction("forward", "Browser forward", "keyboard", "0x1000062"),
            new MouseAction("refresh", "Refresh", "keyboard", "0x1000064"),
            new MouseAction("f11", "Full screen (F11)", "keyboard", "0x100003a"),
            new MouseAction("play", "Play / pause", "keyboard", "0x1000080"),
            new MouseAction("volup", "Volume up", "keyboard", "0x1000072"),
            new MouseAction("voldown", "Volume down", "keyboard", "0x1000070"),
            new MouseAction("mute", "Mute", "keyboard", "0x1000071"),
            new MouseAction("none", "Nothing", "", "")
        };

        // Shown name, AntiMicroX element. LT/RT are triggers 5/6; buttons use SDL numbering (1-based).
        public static readonly string[][] Buttons =
        {
            new[] { "A", "button:1" }, new[] { "B", "button:2" }, new[] { "X", "button:3" },
            new[] { "LB", "button:10" }, new[] { "RB", "button:11" }, new[] { "LT", "trigger:5" }, new[] { "RT", "trigger:6" },
            new[] { "Back", "button:5" }, new[] { "Start", "button:7" }, new[] { "LS", "button:8" }, new[] { "RS", "button:9" }
        };

        public string FilePath;
        public bool Found;
        public double Speed = 45, Fast = 2.6, Scroll = 15;
        public bool Curve = true;
        public Dictionary<string, string> Map = new Dictionary<string, string>(); // button -> action id ("custom" if unknown)
        XmlDocument doc;

        public static MouseLayout Load(string path)
        {
            var m = new MouseLayout();
            m.FilePath = path;
            try
            {
                if (path == null || !File.Exists(path)) return m;
                m.doc = new XmlDocument();
                m.doc.PreserveWhitespace = true;
                m.doc.Load(path);
                m.Found = true;
                var speed = m.doc.SelectSingleNode("//set[@index='1']/stick[@index='1']/stickbutton[@index='1']/mousespeedx");
                if (speed != null) m.Speed = ParseNum(speed.InnerText, 45);
                var accel = m.doc.SelectSingleNode("//set[@index='1']/stick[@index='1']/stickbutton[@index='1']/mouseacceleration");
                m.Curve = accel == null || accel.InnerText.Trim() != "linear";
                var fast = m.doc.SelectSingleNode("//set[@index='1']/button[@index='4']/slots/slot[mode='mousespeedmod']/code");
                if (fast != null) m.Fast = ParseNum(fast.InnerText, 260) / 100.0;
                var wheel = m.doc.SelectSingleNode("//set[@index='1']/stick[@index='2']/stickbutton[@index='1']/wheelspeedy");
                if (wheel != null) m.Scroll = ParseNum(wheel.InnerText, 15);
                foreach (var b in Buttons) m.Map[b[0]] = m.Identify(m.Slots(b[1], false));
            }
            catch (Exception ex) { Log.Write("couldn't read the controller layout: " + ex.Message); m.Found = false; }
            return m;
        }

        static double ParseNum(string s, double fallback)
        {
            double v;
            return double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : fallback;
        }

        static long Code(string s)
        {
            s = (s ?? "").Trim();
            long v;
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return long.TryParse(s.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out v) ? v : -1;
            return long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v) ? v : -1;
        }

        string Identify(XmlElement slots)
        {
            if (slots == null) return "none";
            var list = slots.SelectNodes("slot");
            if (list.Count == 0) return "none";
            if (list.Count > 1) return "custom";
            var mode = list[0].SelectSingleNode("mode");
            var code = list[0].SelectSingleNode("code");
            if (mode == null || code == null) return "custom";
            foreach (var a in Actions)
                if (a.Mode == mode.InnerText.Trim() && a.Code.Length > 0 && Code(a.Code) == Code(code.InnerText)) return a.Id;
            return "custom";
        }

        XmlElement El(XmlNode parent, string name, string index, bool create)
        {
            var n = parent.SelectSingleNode(name + "[@index='" + index + "']") as XmlElement;
            if (n != null || !create) return n;
            n = doc.CreateElement(name);
            n.SetAttribute("index", index);
            parent.AppendChild(n);
            return n;
        }

        // The <slots> element of a button ("button:3") or trigger ("trigger:5").
        XmlElement Slots(string where, bool create)
        {
            var set = doc.SelectSingleNode("/gamecontroller/sets/set[@index='1']") as XmlElement;
            if (set == null) return null;
            string[] parts = where.Split(':');
            XmlElement owner = parts[0] == "trigger"
                ? El(El(set, "trigger", parts[1], create) ?? set, "triggerbutton", "2", create)
                : El(set, "button", parts[1], create);
            if (owner == null) return null;
            var slots = owner.SelectSingleNode("slots") as XmlElement;
            if (slots == null && create) { slots = doc.CreateElement("slots"); owner.AppendChild(slots); }
            return slots;
        }

        static void SetChild(XmlElement parent, string name, string value)
        {
            var n = parent.SelectSingleNode(name);
            if (n == null)
            {
                n = parent.OwnerDocument.CreateElement(name);
                // AntiMicroX expects settings before <slots>
                var slots = parent.SelectSingleNode("slots");
                if (slots != null) parent.InsertBefore(n, slots); else parent.AppendChild(n);
            }
            n.InnerText = value;
        }

        public bool Save()
        {
            if (!Found || doc == null) return false;
            try
            {
                foreach (XmlElement sb in doc.SelectNodes("//set[@index='1']/stick[@index='1']/stickbutton"))
                {
                    SetChild(sb, "mousespeedx", inv(Math.Round(Speed)));
                    SetChild(sb, "mousespeedy", inv(Math.Round(Speed)));
                    SetChild(sb, "mouseacceleration", Curve ? "power" : "linear");
                    SetChild(sb, "mousesensitivity", Curve ? "1.2" : "1");
                }
                foreach (XmlElement sb in doc.SelectNodes("//set[@index='1']/stick[@index='2']/stickbutton[@index='1' or @index='5']"))
                    SetChild(sb, "wheelspeedy", inv(Math.Round(Scroll)));
                var fastSlots = Slots("button:4", true);
                if (fastSlots != null)
                {
                    fastSlots.RemoveAll();
                    fastSlots.AppendChild(MakeSlot(((int)Math.Round(Fast * 100)).ToString(CultureInfo.InvariantCulture), "mousespeedmod"));
                }
                foreach (var b in Buttons)
                {
                    string id;
                    if (!Map.TryGetValue(b[0], out id) || id == "custom") continue;
                    var slots = Slots(b[1], true);
                    if (slots == null) continue;
                    slots.RemoveAll();
                    foreach (var a in Actions)
                        if (a.Id == id && a.Mode.Length > 0) slots.AppendChild(MakeSlot(a.Code, a.Mode));
                }
                if (Shell.Demo) return true;
                if (!File.Exists(FilePath + ".before-toolkit")) File.Copy(FilePath, FilePath + ".before-toolkit"); // your original, once
                doc.Save(FilePath);
                return true;
            }
            catch (Exception ex) { Log.Write("couldn't save the controller layout: " + ex.Message); return false; }
        }

        static string inv(double d) { return d.ToString("0.##", CultureInfo.InvariantCulture); }

        XmlElement MakeSlot(string code, string mode)
        {
            var slot = doc.CreateElement("slot");
            var c = doc.CreateElement("code"); c.InnerText = code; slot.AppendChild(c);
            var m = doc.CreateElement("mode"); m.InnerText = mode; slot.AppendChild(m);
            return slot;
        }
    }

    // ------------------------------------------------------------------ look (theme + accent)

    static class Theme
    {
        public static string Mode = "System";   // System | Light | Dark
        public static string AccentKey = "Windows"; // Windows | #RRGGBB
        public static string Icon = "Lock";     // Lock | Dot | Mode
        public static bool Motion = true;
        public static bool Dark;
        public static Color Accent = Color.FromRgb(0x3B, 0x6C, 0xF6);
        public static event Action Changed;

        public static readonly string[][] Swatches =
        {
            new[] { "Windows accent", "Windows" }, new[] { "Blue", "#3B6CF6" }, new[] { "Violet", "#7C5CFF" }, new[] { "Rose", "#E5487A" },
            new[] { "Coral", "#F2643D" }, new[] { "Amber", "#D98E04" }, new[] { "Mint", "#16A37B" }, new[] { "Teal", "#0E8FA3" }, new[] { "Graphite", "#4B5563" }
        };

        public static void Load()
        {
            var d = Ini.Read(P.Settings);
            Mode = Ini.Get(d, "Theme", "System");
            AccentKey = Ini.Get(d, "Accent", "Windows");
            Icon = Ini.Get(d, "TrayIcon", "Lock");
            Motion = Ini.Bool(d, "Motion", true);
        }

        public static void Save()
        {
            Ini.Update(P.Settings, Ini.Pairs("Theme", Mode, "Accent", AccentKey, "TrayIcon", Icon, "Motion", Motion ? "true" : "false"));
        }

        public static Color Parse(string hex, Color fallback)
        {
            try { return (Color)ColorConverter.ConvertFromString(hex); } catch { return fallback; }
        }

        public static string Hex(Color c) { return "#" + c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2"); }

        public static Color WindowsAccent()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM"))
                {
                    object v = k == null ? null : k.GetValue("AccentColor");
                    if (v is int)
                    {
                        uint abgr = unchecked((uint)(int)v);
                        return Color.FromRgb((byte)(abgr & 0xFF), (byte)((abgr >> 8) & 0xFF), (byte)((abgr >> 16) & 0xFF));
                    }
                }
            }
            catch { }
            return Color.FromRgb(0x3B, 0x6C, 0xF6);
        }

        static bool RegLight(string name)
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    object v = k == null ? null : k.GetValue(name);
                    return !(v is int) || (int)v != 0;
                }
            }
            catch { return true; }
        }

        public static bool SystemDark() { return !RegLight("AppsUseLightTheme"); }
        public static bool TaskbarDark() { return !RegLight("SystemUsesLightTheme"); }

        public static Color AccentFor(string key)
        {
            if (key == null || key == "Windows") return WindowsAccent();
            return Parse(key, Color.FromRgb(0x3B, 0x6C, 0xF6));
        }

        public static double Luma(Color c) { return (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255.0; }

        public static SolidColorBrush Brush(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }
        public static SolidColorBrush Brush(string hex) { return Brush(Parse(hex, Colors.Magenta)); }

        public static LinearGradientBrush Gradient(Color a, Color b)
        {
            var g = new LinearGradientBrush(a, b, new Point(0, 0), new Point(1, 1));
            g.Freeze();
            return g;
        }

        public static Color Mix(Color a, Color b, double t)
        {
            return Color.FromRgb((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));
        }

        public static void Apply()
        {
            Dark = Mode == "Dark" || (Mode == "System" && SystemDark());
            Accent = AccentFor(AccentKey);
            if (Dark && Luma(Accent) < 0.18) Accent = Mix(Accent, Colors.White, 0.3); // keep dark accents visible on dark
            var r = Application.Current.Resources;
            string[] keys = { "BgPage", "BgWin", "BgSide", "BgCard", "Line", "Ink", "Ink2", "Mut", "Soft", "Off" };
            string[] light = { "#F9F9FB", "#F4F4F7", "#ECECF1", "#FFFFFF", "#E3E3EA", "#1B1B21", "#3B3B45", "#6B6B76", "#EEEEF3", "#C9C9D2" };
            string[] dark = { "#26262D", "#1C1C21", "#222228", "#2B2B32", "#3A3A43", "#F2F2F5", "#D2D2DA", "#9C9CA8", "#35353D", "#56565F" };
            for (int i = 0; i < keys.Length; i++) r[keys[i]] = Brush(Dark ? dark[i] : light[i]);
            r["Accent"] = Brush(Accent);
            r["AccentInk"] = Brush(Luma(Accent) > 0.62 ? "#1B1B21" : "#FFFFFF");
            if (Changed != null) Changed();
        }
    }

    static class Ui
    {
        public static object LoadXaml(string name)
        {
            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
            {
                if (s == null) throw new FileNotFoundException("The app was built without " + name + ".");
                return XamlReader.Load(s);
            }
        }

        public static void LoadTheme(Application app)
        {
            Theme.Load();
            app.Resources.MergedDictionaries.Add((ResourceDictionary)LoadXaml("Theme.xaml"));
            Theme.Apply();
        }

        // Lets queued layout and rendering finish (used by the self-test).
        public static void Settle()
        {
            Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ApplicationIdle, new Action(delegate { }));
        }
    }

    // ------------------------------------------------------------------ view-model plumbing

    abstract class Obs : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        protected void Raise([CallerMemberName] string name = null)
        {
            var h = PropertyChanged;
            if (h != null) h(this, new PropertyChangedEventArgs(name));
        }

        public void RaiseAll()
        {
            var h = PropertyChanged;
            if (h != null) h(this, new PropertyChangedEventArgs(string.Empty));
        }

        protected bool Set<T>(ref T field, T value, [CallerMemberName] string name = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            Raise(name);
            return true;
        }
    }

    class Cmd : ICommand
    {
        readonly Action run;
        public Cmd(Action run) { this.run = run; }
        public event EventHandler CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object p) { return true; }
        public void Execute(object p) { run(); }
    }

    // Runs an action once things have been quiet for a moment (sliders send many changes while dragging).
    class Debounce
    {
        readonly DispatcherTimer timer;
        readonly Action action;
        public Debounce(int ms, Action action)
        {
            this.action = action;
            timer = new DispatcherTimer();
            timer.Interval = TimeSpan.FromMilliseconds(ms);
            timer.Tick += delegate { timer.Stop(); action(); };
        }
        public void Kick() { timer.Stop(); timer.Start(); }
        public void Flush() { if (timer.IsEnabled) { timer.Stop(); action(); } }
    }

    // A pickable item (glow style, accent swatch).
    class Choice : Obs
    {
        readonly Action<Choice> picked;
        bool selected;
        public string Key { get; private set; }
        public string Label { get; private set; }
        public Brush Swatch { get; set; }
        public Choice(string key, string label, Brush swatch, Action<Choice> picked) { Key = key; Label = label; Swatch = swatch; this.picked = picked; }
        public bool IsSelected
        {
            get { return selected; }
            set { if (Set(ref selected, value) && value && picked != null) picked(this); }
        }
        public void Mark(bool on) { Set(ref selected, on, "IsSelected"); }
    }

    // A screen you can pick (Leanback screen, AutoZoom levels per screen).
    class ScreenChip : Obs
    {
        readonly Action<ScreenChip> picked;
        bool selected;
        public string Key { get; private set; }     // a screen name, "Main", or "" for "all screens"
        public string Label { get; private set; }
        public string Sub { get; private set; }
        public string Group { get; private set; }   // keeps each page's chips a separate radio group
        public bool Off { get; private set; }       // not connected right now
        public double Glyph { get { return Off ? 0.4 : 1.0; } }
        public string Aria { get { return Label + (Sub.Length > 0 ? ", " + Sub : ""); } }
        public ScreenChip(string group, string key, string label, string sub, bool off, Action<ScreenChip> picked)
        {
            Group = group; Key = key; Label = label; Sub = sub ?? ""; Off = off; this.picked = picked;
        }
        public bool IsSelected
        {
            get { return selected; }
            set { if (Set(ref selected, value) && value && picked != null) picked(this); }
        }
        public void Mark(bool on) { Set(ref selected, on, "IsSelected"); }

        public static string Describe(ScreenInfo s)
        {
            return s.Width + " \u00D7 " + s.Height + (s.Primary ? " \u00B7 main" : "");
        }
    }

    // ------------------------------------------------------------------ All tools page

    class ToolCard : Obs
    {
        public string Id, NameText, MonoText, BlurbText, BadgeText;
        public Brush TileBrush;
        public Func<bool> GetInstalled, GetOn, GetRunning;
        public Action<bool> SetOn;
        public string Name { get { return NameText; } }
        public string Mono { get { return MonoText; } }
        public Brush Tile { get { return TileBrush; } }
        public string Blurb { get { return BlurbText; } }
        public bool Installed { get { return GetInstalled != null && GetInstalled(); } }
        public bool NotInstalled { get { return !Installed; } }
        public string Badge { get { return BadgeText ?? "Not installed"; } }
        public double CardOpacity { get { return Installed ? 1.0 : 0.72; } }
        public ICommand Open { get; set; }

        public string Status
        {
            get
            {
                if (GetInstalled == null) return "Coming soon";
                if (!Installed) return "Not installed";
                if (Shell.Paused) return "Paused";
                if (!GetOn()) return "Off";
                return GetRunning() ? "On" : "On, starting\u2026";
            }
        }

        public Brush StatusBrush
        {
            get
            {
                if (!Installed || Shell.Paused || !GetOn()) return (Brush)Application.Current.Resources["Off"];
                return GetRunning() ? Theme.Brush("#16A37B") : Theme.Brush("#D98E04");
            }
        }

        public bool IsOn
        {
            get { return Installed && GetOn() && !Shell.Paused; }
            set { if (Installed && SetOn != null) SetOn(value); RaiseAll(); }
        }
    }

    // ------------------------------------------------------------------ Leanback Mode page

    class Mapping : Obs
    {
        readonly LeanVm owner;
        public string Button { get; private set; }
        public Mapping(LeanVm owner, string button) { this.owner = owner; Button = button; Cycle = new Cmd(Pick); }
        public string Action { get { return owner.ActionLabel(Button); } }
        public string Aria { get { return Button + " button: " + Action + ". Change"; } }
        public ICommand Cycle { get; private set; }
        public void Refresh() { RaiseAll(); }

        void Pick()
        {
            var menu = new ContextMenu();
            string current = owner.ActionId(Button);
            foreach (var a in MouseLayout.Actions)
            {
                var item = new MenuItem();
                item.Header = a.Label;
                item.IsCheckable = true;
                item.IsChecked = a.Id == current;
                string id = a.Id;
                item.Click += delegate { owner.SetAction(Button, id); };
                menu.Items.Add(item);
            }
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
            menu.IsOpen = true;
        }
    }

    class Device : Obs
    {
        public string Name { get; set; }
        public string Meta { get; set; }
        public string ActionText { get; set; }
        public ICommand Act { get; set; }
        public bool HasAction { get { return Act != null; } }
    }

    class LeanVm : Obs
    {
        readonly Shell shell;
        Dictionary<string, string> cfg = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, string> pending = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        readonly Debounce saveCfg, saveLayout;
        MouseLayout layout = new MouseLayout();
        bool capturing;

        public ObservableCollection<Mapping> Mappings { get; private set; }
        public ObservableCollection<Device> Devices { get; private set; }
        public ObservableCollection<ScreenChip> ScreenChips { get; private set; }
        public ICommand ToggleTv { get; private set; }
        public ICommand ChangeHotkey { get; private set; }

        public LeanVm(Shell shell)
        {
            this.shell = shell;
            saveCfg = new Debounce(350, FlushCfg);
            saveLayout = new Debounce(900, FlushLayout);
            Mappings = new ObservableCollection<Mapping>();
            foreach (var b in MouseLayout.Buttons) Mappings.Add(new Mapping(this, b[0]));
            Devices = new ObservableCollection<Device>();
            ScreenChips = new ObservableCollection<ScreenChip>();
            ToggleTv = new Cmd(shell.ToggleTv);
            ChangeHotkey = new Cmd(delegate { Capturing = !Capturing; });
        }

        Snap S { get { return shell.State; } }

        public void LoadSettings()
        {
            cfg = Shell.Demo ? Ini.Pairs("TvScale", "175", "NormalScale", "125", "TvPointerSize", "64", "Hotkey", "Ctrl+Alt+T", "Mode", "Auto", "Screen", "LG TV SSCR2")
                             : Ini.Read(P.LeanSettings);
            foreach (var kv in pending) cfg[kv.Key] = kv.Value; // keep edits that haven't been written yet
            RebuildScreens();
            RaiseAll();
        }

        public void LoadLayout(string path)
        {
            layout = MouseLayout.Load(path ?? Amx.Profile);
            foreach (var m in Mappings) m.Refresh();
            RaiseAll();
        }

        public void StatusChanged()
        {
            RebuildDevices();
            RaiseAll();
        }

        // ---- which screen

        public void ScreensChanged() { RebuildScreens(); RaiseAll(); }

        string ScreenName { get { return C("Screen", Screens.Main); } }

        void RebuildScreens()
        {
            var list = shell.ScreenList;
            string cur = ScreenName;
            var want = new List<ScreenChip>();
            Action<ScreenChip> pick = delegate(ScreenChip c) { Put("Screen", c.Key); };
            var main = Screens.Find(list, Screens.Main);
            want.Add(new ScreenChip("LbScreen", Screens.Main, "Main display", main != null ? main.Name : "", false, pick));
            foreach (var sc in list) if (!sc.Primary) want.Add(new ScreenChip("LbScreen", sc.Name, sc.Name, ScreenChip.Describe(sc), false, pick));
            if (!Screens.IsMain(cur) && Screens.Find(list, cur) == null) want.Add(new ScreenChip("LbScreen", cur, cur, "Not connected", true, pick));
            bool same = want.Count == ScreenChips.Count;
            for (int i = 0; same && i < want.Count; i++) same = want[i].Key == ScreenChips[i].Key && want[i].Sub == ScreenChips[i].Sub;
            if (!same) { ScreenChips.Clear(); foreach (var c in want) ScreenChips.Add(c); }
            foreach (var c in ScreenChips) c.Mark(Screens.IsMain(cur) ? Screens.IsMain(c.Key) : c.Key.Equals(cur, StringComparison.OrdinalIgnoreCase));
        }

        public bool MultiScreen { get { return shell.ScreenList.Count > 1 || !Screens.IsMain(ScreenName); } }

        bool ScreenOff { get { return !Screens.IsMain(ScreenName) && Screens.Find(shell.ScreenList, ScreenName) == null; } }

        public string ScreenHint
        {
            get
            {
                if (ScreenOff) return ScreenName + " isn't connected right now (a TV that's switched off usually isn't). Leanback mode uses it as soon as it's back, and leaves your other screens alone meanwhile.";
                return "The screen that gets bigger. Your other screens stay as they are.";
            }
        }

        public bool MovePointer
        {
            get { return Ini.Bool(cfg, "MovePointerToScreen", true); }
            set { Put("MovePointerToScreen", value ? "true" : "false"); }
        }

        public string DeskHint { get { return MultiScreen ? "Normal scaling of the Leanback screen." : "Your normal Windows scaling."; } }

        public void Flush() { saveCfg.Flush(); saveLayout.Flush(); }

        string C(string key, string fallback) { return Ini.Get(cfg, key, fallback); }

        void Put(string key, string value, [CallerMemberName] string prop = null)
        {
            if (C(key, null) == value) return;
            cfg[key] = value;
            pending[key] = value;
            saveCfg.Kick();
            RaiseAll();
        }

        void FlushCfg()
        {
            if (pending.Count == 0) return;
            var copy = new Dictionary<string, string>(pending, StringComparer.OrdinalIgnoreCase);
            pending.Clear();
            if (!Leanback.Installed && !Shell.Demo) return;
            Ini.Update(P.LeanSettings, copy);
            shell.NoteOwnWrite(P.LeanSettings);
        }

        void FlushLayout()
        {
            if (!layout.Save()) return;
            // AntiMicroX reads the layout when it starts, so restart it (unless it's paused for a game or turned off).
            if (Shell.Demo || !S.AmxRunning) return;
            Procs.Background(delegate { Amx.Stop(); Thread.Sleep(250); Amx.Start(); }, null);
        }

        // ---- hero

        public bool Running { get { return S.LeanInstalled; } }
        bool Tv { get { return S.Tv; } }

        public Brush HeroBrush
        {
            get
            {
                if (!S.LeanRunning) return Theme.Gradient(Theme.Parse("#5B6472", Colors.Gray), Theme.Parse("#2A2F3A", Colors.Black));
                return Tv ? Theme.Gradient(Theme.Accent, Theme.Parse("#22183F", Colors.Black))
                          : Theme.Gradient(Theme.Parse("#3A4256", Colors.Gray), Theme.Parse("#262B38", Colors.Black));
            }
        }

        public Brush ScreenBrush
        {
            get
            {
                return Tv ? Theme.Gradient(Theme.Mix(Theme.Accent, Colors.White, 0.15), Theme.Mix(Theme.Accent, Colors.Black, 0.55))
                          : Theme.Gradient(Theme.Parse("#3B4459", Colors.Gray), Theme.Parse("#1E2230", Colors.Black));
            }
        }

        public double PreviewScale
        {
            get
            {
                if (!Tv) return 1.0;
                double normal = Math.Max(100, Ini.Num(cfg, "NormalScale", 100));
                return Math.Min(1.6, Math.Max(1.15, TvScale / normal));
            }
        }

        public Brush StatusBrush
        {
            get
            {
                if (!S.LeanRunning) return (Brush)Application.Current.Resources["Off"];
                return Tv ? Theme.Brush(Theme.Accent) : Theme.Brush("#16A37B");
            }
        }

        public string StatusText
        {
            get
            {
                if (!S.LeanInstalled) return "Not installed";
                if (Shell.Paused) return "Paused";
                if (!S.LeanRunning) return S.LeanEnabled ? "Starting\u2026" : "Turned off";
                string why = Ini.Get(S.Status, "Why", "");
                string controller = Ini.Get(S.Status, "Controller", "");
                if (Tv && why == "controller asleep") return "Leanback mode \u00B7 controller asleep";
                if (Tv && !Screens.IsMain(ScreenName)) return "Leanback mode \u00B7 on " + ScreenName + (ScreenOff ? " (not connected)" : "");
                if (Tv && controller.Length > 0) return "Leanback mode \u00B7 " + Shell.ShortName(controller);
                return (Tv ? "Leanback mode" : "Desktop mode") + (why == "shortcut" ? " \u00B7 switched by you" : why == "always" ? " \u00B7 always" : "");
            }
        }

        public string CtaText
        {
            get
            {
                if (!S.LeanRunning) return "Start Leanback Mode";
                return Tv ? "Back to desktop" : "Switch to Leanback mode";
            }
        }

        public string HotkeyHint
        {
            get { string k = C("Hotkey", "Ctrl+Alt+T"); return S.LeanRunning && k.Length > 0 ? "or press " + k : ""; }
        }

        public string Problem
        {
            get
            {
                if (!S.LeanInstalled) return "Leanback Mode isn't installed on this PC. Get it from the toolkit website (Updates and about).";
                if (!S.AmxInstalled) return "AntiMicroX isn't installed, so the controller can't move the pointer. Run the Leanback Mode installer again.";
                if (!layout.Found) return "The controller layout file wasn't found, so mouse settings and buttons can't be changed here. Run the Leanback Mode installer again.";
                return "";
            }
        }
        public bool HasProblem { get { return Problem.Length > 0; } }

        // ---- when and how

        string Mode { get { return C("Mode", "Auto"); } }
        public bool ModeAuto { get { return string.Equals(Mode, "Auto", StringComparison.OrdinalIgnoreCase); } set { if (value) Put("Mode", "Auto"); } }
        public bool ModeTv { get { return string.Equals(Mode, "TV", StringComparison.OrdinalIgnoreCase); } set { if (value) Put("Mode", "TV"); } }
        public bool ModeDesk { get { return string.Equals(Mode, "Normal", StringComparison.OrdinalIgnoreCase); } set { if (value) Put("Mode", "Normal"); } }

        public bool StayWhenAsleep
        {
            get { return Ini.Bool(cfg, "StayInLeanbackWhenAsleep", true); }
            set { Put("StayInLeanbackWhenAsleep", value ? "true" : "false"); }
        }
        public string AsleepHint
        {
            get
            {
                double m = Ini.Num(cfg, "AsleepAfterIdleMinutes", 5);
                return "If it switches itself off after " + m.ToString("0.#", CultureInfo.InvariantCulture) + " minutes untouched, stay big. Holding its Home button still goes back to desktop.";
            }
        }

        public double TvScale
        {
            get { return Ini.Num(cfg, "TvScale", 150); }
            set { int v = (int)(Math.Round(value / 25.0) * 25); Put("TvScale", v.ToString(CultureInfo.InvariantCulture)); }
        }
        public string ScaleText { get { return TvScale.ToString("0", CultureInfo.InvariantCulture) + "%"; } }
        public string ScaleHint { get { return "Windows offers steps of 25%."; } }

        public double DeskScale
        {
            get { return Ini.Num(cfg, "NormalScale", 100); }
            set { int v = (int)(Math.Round(value / 25.0) * 25); Put("NormalScale", v.ToString(CultureInfo.InvariantCulture)); }
        }
        public string DeskScaleText { get { return DeskScale.ToString("0", CultureInfo.InvariantCulture) + "%"; } }

        public double TvPointer
        {
            get { return Ini.Num(cfg, "TvPointerSize", 48); }
            set { int v = (int)(Math.Round(value / 8.0) * 8); Put("TvPointerSize", v.ToString(CultureInfo.InvariantCulture)); }
        }
        public string PointerText { get { return TvPointer.ToString("0", CultureInfo.InvariantCulture); } }

        public bool TransSmooth { get { return Ini.Bool(cfg, "SmoothTransition", true); } set { if (value) Put("SmoothTransition", "true"); } }
        public bool TransInstant { get { return !TransSmooth; } set { if (value) Put("SmoothTransition", "false"); } }

        // ---- shortcut

        public bool Capturing
        {
            get { return capturing; }
            set { capturing = value; RaiseAll(); }
        }

        public string[] HotkeyKeys
        {
            get
            {
                if (capturing) return new[] { "\u2026" };
                var parts = C("Hotkey", "Ctrl+Alt+T").Split('+');
                for (int i = 0; i < parts.Length; i++)
                {
                    string p = parts[i].Trim();
                    if (p.Length == 2 && p[0] == 'D' && char.IsDigit(p[1])) p = p.Substring(1);
                    parts[i] = p;
                }
                return parts;
            }
        }
        public string HotkeyNote { get { return capturing ? "Press the new shortcut now. Esc cancels." : "Switches modes from any app."; } }
        public string HotkeyButtonText { get { return capturing ? "Cancel" : "Change"; } }

        // Called by the settings window while Capturing. Returns true if the key was used.
        public bool OfferKey(Key key, ModifierKeys mods)
        {
            if (!capturing) return false;
            if (key == Key.Escape) { Capturing = false; return true; }
            if (key == Key.LeftCtrl || key == Key.RightCtrl || key == Key.LeftAlt || key == Key.RightAlt ||
                key == Key.LeftShift || key == Key.RightShift || key == Key.LWin || key == Key.RWin || key == Key.System) return true;
            if (mods == ModifierKeys.None) return true; // needs at least one of Ctrl / Alt / Shift / Win
            var parts = new List<string>();
            if ((mods & ModifierKeys.Control) != 0) parts.Add("Ctrl");
            if ((mods & ModifierKeys.Alt) != 0) parts.Add("Alt");
            if ((mods & ModifierKeys.Shift) != 0) parts.Add("Shift");
            if ((mods & ModifierKeys.Windows) != 0) parts.Add("Win");
            parts.Add(((WF.Keys)KeyInterop.VirtualKeyFromKey(key)).ToString());
            capturing = false;
            Put("Hotkey", string.Join("+", parts));
            return true;
        }

        // ---- controller mouse

        void Layout(Action change)
        {
            change();
            saveLayout.Kick();
            RaiseAll();
        }

        public double Speed { get { return layout.Speed; } set { Layout(delegate { layout.Speed = Math.Round(value); }); } }
        public string SpeedText { get { return layout.Speed.ToString("0", CultureInfo.InvariantCulture); } }
        public double Fast { get { return layout.Fast; } set { Layout(delegate { layout.Fast = Math.Round(value, 1); }); } }
        public string FastText { get { return layout.Fast.ToString("0.0", CultureInfo.InvariantCulture) + "\u00D7"; } }
        public double Scroll { get { return layout.Scroll; } set { Layout(delegate { layout.Scroll = Math.Round(value); }); } }
        public string ScrollText { get { return layout.Scroll.ToString("0", CultureInfo.InvariantCulture); } }
        public bool Curve { get { return layout.Curve; } set { Layout(delegate { layout.Curve = value; }); } }
        public bool PauseGames { get { return Ini.Bool(cfg, "PauseMouseInGames", true); } set { Put("PauseMouseInGames", value ? "true" : "false"); } }

        public string ActionId(string button)
        {
            string id;
            return layout.Map.TryGetValue(button, out id) ? id : "none";
        }

        public string ActionLabel(string button)
        {
            string id = ActionId(button);
            if (id == "custom") return "Custom";
            foreach (var a in MouseLayout.Actions) if (a.Id == id) return a.Label;
            return "Nothing";
        }

        public void SetAction(string button, string id)
        {
            layout.Map[button] = id;
            saveLayout.Kick();
            foreach (var m in Mappings) if (m.Button == button) m.Refresh();
        }

        // ---- controllers

        public bool DetectAll { get { return C("DetectControllers", "All") != "XInput"; } set { if (value) Put("DetectControllers", "All"); } }
        public bool DetectXInput { get { return C("DetectControllers", "All") == "XInput"; } set { if (value) Put("DetectControllers", "XInput"); } }

        static readonly Regex IdPattern = new Regex(@"\(([0-9A-Fa-f]{4}:[0-9A-Fa-f]{4})\)");

        void RebuildDevices()
        {
            Devices.Clear();
            string now = Ini.Get(S.Status, "Controller", ""), last = Ini.Get(S.Status, "LastController", "");
            string shown = now.Length > 0 ? now : last;
            var ignored = new List<string>();
            foreach (var part in C("IgnoreControllers", "").Split(new[] { ';', ',', ' ' }, StringSplitOptions.RemoveEmptyEntries)) ignored.Add(part.Trim().ToUpperInvariant());

            if (shown.Length > 0)
            {
                var d = new Device();
                d.Name = Shell.ShortName(shown);
                var m = IdPattern.Match(shown);
                string id = m.Success ? m.Groups[1].Value.ToUpperInvariant() : null;
                d.Meta = (now.Length > 0 ? "Connected now" : "Seen last") + (id != null ? " \u00B7 " + id : "");
                if (id != null && !ignored.Contains(id))
                {
                    d.ActionText = "Ignore";
                    d.Act = new Cmd(delegate { ignored.Add(id); Put("IgnoreControllers", string.Join(";", ignored)); RebuildDevices(); });
                }
                Devices.Add(d);
            }
            foreach (var id in ignored)
            {
                string copy = id;
                var d = new Device();
                d.Name = copy;
                d.Meta = "Ignored. It never switches on Leanback mode.";
                d.ActionText = "Stop ignoring";
                d.Act = new Cmd(delegate { ignored.Remove(copy); Put("IgnoreControllers", string.Join(";", ignored)); RebuildDevices(); });
                Devices.Add(d);
            }
            if (Devices.Count == 0)
            {
                var d = new Device();
                d.Name = "No controller seen yet";
                d.Meta = "Turn one on and it shows up here.";
                Devices.Add(d);
            }
        }
    }

    // ------------------------------------------------------------------ Media Tab Jump page

    class MtjVm : Obs
    {
        readonly Shell shell;
        Dictionary<string, string> cfg = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        readonly Debounce save;
        public ObservableCollection<Choice> Styles { get; private set; }

        static readonly string[][] StyleList =
        {
            new[] { "Shift", "Colour shift" }, new[] { "Accent", "Accent" }, new[] { "Sunset", "Sunset" }, new[] { "Ocean", "Ocean" }, new[] { "White", "Soft white" }
        };

        public MtjVm(Shell shell)
        {
            this.shell = shell;
            save = new Debounce(300, Save);
            Styles = new ObservableCollection<Choice>();
            foreach (var s in StyleList) Styles.Add(new Choice(s[0], s[1], StyleBrush(s[0]), delegate(Choice c) { Put("GlowStyle", c.Key); }));
            Theme.Changed += delegate
            {
                foreach (var c in Styles) if (c.Key == "Accent") c.Swatch = StyleBrush("Accent");
                if (Style == "Accent") Put("GlowColor", Theme.Hex(Theme.Accent));
                foreach (var c in Styles) c.RaiseAll();
                RaiseAll();
            };
        }

        Snap S { get { return shell.State; } }

        public void Load()
        {
            cfg = Ini.Read(P.MtjSettings);
            foreach (var c in Styles) c.Mark(c.Key == Style);
            RaiseAll();
        }

        public void Flush() { save.Flush(); }

        void Put(string key, string value)
        {
            if (Ini.Get(cfg, key, null) == value) return;
            cfg[key] = value;
            if (key == "GlowStyle" && value == "Accent") cfg["GlowColor"] = Theme.Hex(Theme.Accent);
            save.Kick();
            RaiseAll();
        }

        void Save()
        {
            if (!Mtj.Installed && !Shell.Demo) return;
            Ini.Update(P.MtjSettings, Ini.Pairs("GlowStyle", Style, "GlowColor", Ini.Get(cfg, "GlowColor", Theme.Hex(Theme.Accent)),
                "GlowBrightness", Brightness.ToString("0", CultureInfo.InvariantCulture), "GlowSize", Size.ToString("0", CultureInfo.InvariantCulture),
                "BringToMyScreen", BringHere ? "true" : "false"));
        }

        static Brush StyleBrush(string key)
        {
            Func<string, Color> c = delegate(string h) { return Theme.Parse(h, Colors.White); };
            GradientStopCollection stops;
            switch (key)
            {
                case "Accent": return Theme.Brush(Theme.Accent);
                case "Sunset": stops = new GradientStopCollection { new GradientStop(c("#FF9A5A"), 0), new GradientStop(c("#FF5E7E"), 0.55), new GradientStop(c("#FFB86B"), 1) }; break;
                case "Ocean": stops = new GradientStopCollection { new GradientStop(c("#5AB8FF"), 0), new GradientStop(c("#6BE3C4"), 0.5), new GradientStop(c("#8D9FFF"), 1) }; break;
                case "White": stops = new GradientStopCollection { new GradientStop(c("#FFFFFF"), 0), new GradientStop(c("#E4EAFF"), 1) }; break;
                default: stops = new GradientStopCollection { new GradientStop(c("#8D9FFF"), 0), new GradientStop(c("#BC82F3"), 0.25), new GradientStop(c("#F5B9EA"), 0.5), new GradientStop(c("#FF6778"), 0.75), new GradientStop(c("#FFBA71"), 1) }; break;
            }
            var g = new LinearGradientBrush(stops, new Point(0, 0), new Point(1, 1));
            g.Freeze();
            return g;
        }

        public string Style { get { return Ini.Get(cfg, "GlowStyle", "Shift"); } }

        public bool MultiScreen { get { return shell.ScreenList.Count > 1 || BringHere; } }
        public bool BringHere
        {
            get { return Ini.Bool(cfg, "BringToMyScreen", false); }
            set { Put("BringToMyScreen", value ? "true" : "false"); }
        }

        public string Problem
        {
            get
            {
                if (!S.MtjInstalled) return "Media Tab Jump isn't installed on this PC. Get it from the toolkit website (Updates and about).";
                if (Shell.Paused) return "The toolkit is paused. Media Tab Jump comes back when you resume.";
                if (!S.MtjEnabled) return "Media Tab Jump is turned off. Turn it on from All tools.";
                return "";
            }
        }
        public bool HasProblem { get { return Problem.Length > 0; } }

        public bool GlowOn
        {
            get { return S.GlowOn; }
            set { shell.SetGlow(value); RaiseAll(); }
        }

        public Brush GlowBrush { get { return StyleBrush(Style); } }
        public double GlowOpacity { get { return GlowOn ? Math.Min(1.0, 0.25 + 0.6 * Brightness / 150.0) : 0.0; } }
        public double GlowBlur { get { return 6 + 16 * Size / 100.0; } }

        public double Brightness
        {
            get { return Math.Max(10, Math.Min(150, Ini.Num(cfg, "GlowBrightness", 100))); }
            set { Put("GlowBrightness", Math.Round(value / 5.0 * 5).ToString("0", CultureInfo.InvariantCulture)); }
        }
        public string BrightnessText { get { return Brightness.ToString("0", CultureInfo.InvariantCulture) + "%"; } }

        public double Size
        {
            get { return Math.Max(50, Math.Min(200, Ini.Num(cfg, "GlowSize", 100))); }
            set { Put("GlowSize", (Math.Round(value / 10.0) * 10).ToString("0", CultureInfo.InvariantCulture)); }
        }
        public string SizeText { get { return Size.ToString("0", CultureInfo.InvariantCulture) + "%"; } }
    }

    // ------------------------------------------------------------------ AutoZoom page

    // One app on the AutoZoom list: how many zoom steps it gets when maximized and when not.
    class AzApp : Obs
    {
        const int Limit = 15;
        static readonly string[][] Tiles =
        {
            new[] { "#D98E04", "#B5452A" }, new[] { "#3B6CF6", "#7C5CFF" }, new[] { "#16A37B", "#0B5F6E" },
            new[] { "#FF8A5B", "#E5487A" }, new[] { "#5AB8FF", "#3B6CF6" }, new[] { "#BC82F3", "#7B4DFF" }
        };

        readonly AzVm owner;
        int max, normal;
        // Levels for particular screens (screen name -> maximized, not maximized). Other screens use max / normal.
        readonly Dictionary<string, int[]> onScreen = new Dictionary<string, int[]>(StringComparer.OrdinalIgnoreCase);
        public string ProcName { get; private set; }
        public string Name { get; private set; }

        public AzApp(AzVm owner, string procName, string name, int max, int normal)
        {
            this.owner = owner;
            ProcName = procName;
            Name = name;
            this.max = Clamp(max);
            this.normal = Clamp(normal);
            MaxDown = new Cmd(delegate { Max = Max - 1; });
            MaxUp = new Cmd(delegate { Max = Max + 1; });
            NormalDown = new Cmd(delegate { Normal = Normal - 1; });
            NormalUp = new Cmd(delegate { Normal = Normal + 1; });
            Remove = new Cmd(delegate { owner.RemoveApp(this); });
            ResetScreen = new Cmd(delegate { if (onScreen.Remove(owner.Editing)) { RaiseAll(); owner.Changed(); } });
        }

        static int Clamp(int v) { return Math.Max(-Limit, Math.Min(Limit, v)); }

        public int BaseMax { get { return max; } }
        public int BaseNormal { get { return normal; } }
        public Dictionary<string, int[]> OnScreen { get { return onScreen; } }
        public void SetOnScreen(string screen, int m, int n) { onScreen[screen] = new[] { Clamp(m), Clamp(n) }; }

        // The levels being edited: the screen picked at the top of the page, or every screen.
        int[] Own
        {
            get
            {
                int[] v;
                return owner.Editing.Length > 0 && onScreen.TryGetValue(owner.Editing, out v) ? v : null;
            }
        }

        void Change(int which, int value)
        {
            int v = Clamp(value);
            if (owner.Editing.Length == 0)
            {
                if (which == 0) { if (v == max) return; max = v; }
                else { if (v == normal) return; normal = v; }
            }
            else
            {
                int[] cur = Own ?? new[] { max, normal };
                if (Own != null && cur[which] == v) return;
                var next = new[] { cur[0], cur[1] };
                next[which] = v;
                onScreen[owner.Editing] = next;
            }
            RaiseAll();
            owner.Changed();
        }

        public int Max { get { var o = Own; return o != null ? o[0] : max; } set { Change(0, value); } }
        public int Normal { get { var o = Own; return o != null ? o[1] : normal; } set { Change(1, value); } }

        public bool HasOwn { get { return Own != null; } }
        public double LevelOpacity { get { return owner.Editing.Length > 0 && Own == null ? 0.55 : 1.0; } }

        public string Meta
        {
            get
            {
                string m = ProcName + ".exe";
                if (owner.Editing.Length > 0) return m + (Own != null ? " \u00B7 own levels on this screen" : " \u00B7 same as all screens");
                if (onScreen.Count > 0) return m + " \u00B7 own levels on " + onScreen.Count + (onScreen.Count == 1 ? " screen" : " screens");
                return m;
            }
        }
        public string Mono { get { return Name.Length > 0 ? Name.Substring(0, 1).ToUpperInvariant() : "?"; } }
        public string MaxText { get { return Steps(Max); } }
        public string NormalText { get { return Steps(Normal); } }
        public string MaxAria { get { return Name + " when maximized: " + Spoken(Max); } }
        public string NormalAria { get { return Name + " when not maximized: " + Spoken(Normal); } }
        public ICommand ResetScreen { get; private set; }

        public Brush Tile
        {
            get
            {
                int h = 0;
                foreach (char c in ProcName.ToLowerInvariant()) h = (h * 31 + c) & 0x7FFFFFFF;
                var t = Tiles[h % Tiles.Length];
                return Theme.Gradient(Theme.Parse(t[0], Colors.Gray), Theme.Parse(t[1], Colors.Black));
            }
        }

        static string Steps(int s) { return s == 0 ? "100%" : s.ToString("+0;-0", CultureInfo.InvariantCulture); }
        static string Spoken(int s) { return s == 0 ? "normal size" : Math.Abs(s) + (Math.Abs(s) == 1 ? " step " : " steps ") + (s > 0 ? "bigger" : "smaller"); }

        public ICommand MaxDown { get; private set; }
        public ICommand MaxUp { get; private set; }
        public ICommand NormalDown { get; private set; }
        public ICommand NormalUp { get; private set; }
        public ICommand Remove { get; private set; }
    }

    class AzVm : Obs
    {
        static readonly HashSet<string> NotApps = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "WindowsUnlockedToolkit", "ApplicationFrameHost", "TextInputHost", "ShellExperienceHost", "StartMenuExperienceHost",
            "SearchHost", "LockApp", "SystemSettings", "explorer", "AutoZoom", "LeanbackMode", "MediaTabJump"
        };

        readonly Shell shell;
        readonly Debounce save;
        readonly List<string> other = new List<string>(); // comments and other settings, kept as they are
        string editing = "";                               // screen whose levels are shown; "" = all screens
        public ObservableCollection<AzApp> Apps { get; private set; }
        public ObservableCollection<ScreenChip> ScreenTabs { get; private set; }
        public ICommand AddApp { get; private set; }

        public AzVm(Shell shell)
        {
            this.shell = shell;
            save = new Debounce(250, Save);
            Apps = new ObservableCollection<AzApp>();
            ScreenTabs = new ObservableCollection<ScreenChip>();
            AddApp = new Cmd(PickApp);
        }

        public string Editing { get { return editing; } }

        void Edit(string screen)
        {
            if (screen == editing) return;
            editing = screen;
            foreach (var a in Apps) a.RaiseAll();
            RaiseAll();
        }

        public void ScreensChanged() { RebuildTabs(); RaiseAll(); }

        void RebuildTabs()
        {
            var list = shell.ScreenList;
            var want = new List<ScreenChip>();
            Action<ScreenChip> pick = delegate(ScreenChip c) { Edit(c.Key); };
            want.Add(new ScreenChip("AzScreen", "", "All screens", "Unless set below", false, pick));
            foreach (var sc in list) want.Add(new ScreenChip("AzScreen", sc.Name, sc.Name, ScreenChip.Describe(sc), false, pick));
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var sc in list) seen.Add(sc.Name);
            foreach (var a in Apps)
                foreach (var name in a.OnScreen.Keys)
                    if (seen.Add(name)) want.Add(new ScreenChip("AzScreen", name, name, "Not connected", true, pick));
            bool same = want.Count == ScreenTabs.Count;
            for (int i = 0; same && i < want.Count; i++) same = want[i].Key == ScreenTabs[i].Key && want[i].Sub == ScreenTabs[i].Sub;
            if (!same) { ScreenTabs.Clear(); foreach (var c in want) ScreenTabs.Add(c); }
            bool found = false;
            foreach (var c in ScreenTabs) if (c.Key.Equals(editing, StringComparison.OrdinalIgnoreCase)) found = true;
            if (!found) Edit("");
            foreach (var c in ScreenTabs) c.Mark(c.Key.Equals(editing, StringComparison.OrdinalIgnoreCase));
        }

        public bool ShowScreenTabs { get { return ScreenTabs.Count > 2 || shell.ScreenList.Count > 1; } }

        public string AppsHint
        {
            get
            {
                if (editing.Length > 0) return "Levels on " + editing + ". Apps you don't change here use their all-screens levels. Moving a window to another screen switches its zoom when you let go.";
                return "Zoom goes in steps. One step is one press of the app's zoom-in key (Ctrl and +); below 100% it's zoom-out (Ctrl and -).";
            }
        }

        Snap S { get { return shell.State; } }

        public void Load()
        {
            Apps.Clear();
            other.Clear();
            var byProc = new Dictionary<string, AzApp>(StringComparer.OrdinalIgnoreCase);
            var later = new List<string[]>(); // per-screen lines: proc, screen, max, normal
            string[] lines = new string[0];
            try { if (File.Exists(P.AzSettings)) lines = File.ReadAllLines(P.AzSettings); } catch { }
            foreach (var raw in lines)
            {
                string line = raw.Trim();
                int eq = line.IndexOf('=');
                if (eq > 4 && line.StartsWith("App.", StringComparison.OrdinalIgnoreCase))
                {
                    string proc = line.Substring(4, eq - 4).Trim(), screen = "";
                    int at = proc.IndexOf('@');
                    if (at >= 0) { screen = proc.Substring(at + 1).Trim(); proc = proc.Substring(0, at).Trim(); }
                    if (proc.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) proc = proc.Substring(0, proc.Length - 4);
                    string[] parts = line.Substring(eq + 1).Split(',');
                    int max, normal;
                    if (proc.Length > 0 && parts.Length >= 2
                        && int.TryParse(parts[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out max)
                        && int.TryParse(parts[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out normal))
                    {
                        if (screen.Length > 0) later.Add(new[] { proc, screen, parts[0].Trim(), parts[1].Trim() });
                        else if (!byProc.ContainsKey(proc))
                        {
                            var app = new AzApp(this, proc, FriendlyName(proc), max, normal);
                            byProc[proc] = app;
                            Apps.Add(app);
                        }
                        continue;
                    }
                }
                other.Add(raw);
            }
            foreach (var l in later)
            {
                AzApp app;
                if (!byProc.TryGetValue(l[0], out app)) { app = new AzApp(this, l[0], FriendlyName(l[0]), 0, 0); byProc[l[0]] = app; Apps.Add(app); }
                app.SetOnScreen(l[1], int.Parse(l[2], CultureInfo.InvariantCulture), int.Parse(l[3], CultureInfo.InvariantCulture));
            }
            RebuildTabs();
            RaiseAll();
        }

        // Sample list for the self-test screenshots.
        public void Sample()
        {
            Apps.Clear();
            Apps.Add(new AzApp(this, "claude", "Claude", 4, 1));
            Apps.Add(new AzApp(this, "ms-teams", "Microsoft Teams", 2, 0));
            Apps.Add(new AzApp(this, "notepad", "Notepad", 0, -1));
            Apps[0].SetOnScreen("LG TV SSCR2", 7, 3);
            RebuildTabs();
            RaiseAll();
        }

        public void Flush() { save.Flush(); }
        public void Changed() { save.Kick(); RebuildTabs(); RaiseAll(); }
        public void RemoveApp(AzApp app) { Apps.Remove(app); Changed(); }

        void Save()
        {
            if (Shell.Demo || !Az.Installed) return;
            var lines = new List<string>(other);
            int at = 0;
            while (at < lines.Count && (lines[at].Trim().StartsWith("#") || lines[at].Trim().StartsWith(";"))) at++;
            var apps = new List<string>();
            foreach (var a in Apps)
            {
                apps.Add("App." + a.ProcName + "=" + a.BaseMax.ToString(CultureInfo.InvariantCulture) + "," + a.BaseNormal.ToString(CultureInfo.InvariantCulture));
                foreach (var kv in a.OnScreen)
                    apps.Add("App." + a.ProcName + "@" + kv.Key + "=" + kv.Value[0].ToString(CultureInfo.InvariantCulture) + "," + kv.Value[1].ToString(CultureInfo.InvariantCulture));
            }
            lines.InsertRange(at, apps);
            for (int attempt = 0; attempt < 6; attempt++)
            {
                try
                {
                    Directory.CreateDirectory(P.AzDir);
                    File.WriteAllText(P.AzSettings, string.Join(Environment.NewLine, lines) + Environment.NewLine);
                    shell.NoteOwnWrite(P.AzSettings);
                    return;
                }
                catch (IOException) { Thread.Sleep(80); }
                catch (UnauthorizedAccessException) { Thread.Sleep(80); }
            }
            Log.Write("couldn't save " + P.AzSettings);
        }

        // A menu of the apps that have a window open right now.
        void PickApp()
        {
            var found = new SortedDictionary<string, string>(StringComparer.CurrentCultureIgnoreCase); // name -> process
            var have = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var a in Apps) have.Add(a.ProcName);
            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    if (p.MainWindowHandle == IntPtr.Zero || p.MainWindowTitle.Length == 0) continue;
                    string proc = p.ProcessName;
                    if (have.Contains(proc) || NotApps.Contains(proc)) continue;
                    string name = Describe(p, proc);
                    if (found.ContainsKey(name) && found[name] != proc) name = name + " (" + proc + ")";
                    found[name] = proc;
                    have.Add(proc);
                }
                catch { }
                finally { p.Dispose(); }
            }

            var menu = new ContextMenu();
            foreach (var kv in found)
            {
                var item = new MenuItem();
                item.Header = kv.Key;
                string proc = kv.Value, name = kv.Key;
                item.Click += delegate
                {
                    Apps.Add(new AzApp(this, proc, name, 2, 0));
                    Log.Write("AutoZoom: added " + proc);
                    Changed();
                };
                menu.Items.Add(item);
            }
            if (found.Count == 0)
            {
                var none = new MenuItem();
                none.Header = "Open the app first, then add it here";
                none.IsEnabled = false;
                menu.Items.Add(none);
            }
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
            menu.IsOpen = true;
        }

        static string FriendlyName(string proc)
        {
            foreach (var p in Process.GetProcessesByName(proc))
            {
                try { return Describe(p, proc); }
                finally { p.Dispose(); }
            }
            return Describe(null, proc);
        }

        static string Describe(Process p, string proc)
        {
            if (p != null)
            {
                try
                {
                    string d = p.MainModule.FileVersionInfo.FileDescription;
                    if (!string.IsNullOrWhiteSpace(d)) return d.Trim();
                }
                catch { }
            }
            return proc.Length > 0 ? char.ToUpperInvariant(proc[0]) + proc.Substring(1) : proc;
        }

        public bool HasApps { get { return Apps.Count > 0; } }
        public bool NoApps { get { return Apps.Count == 0; } }

        public string Problem
        {
            get
            {
                if (!S.AzInstalled) return "AutoZoom isn't installed on this PC. Get it from the toolkit website (Updates and about).";
                if (Shell.Paused) return "The toolkit is paused. AutoZoom comes back when you resume.";
                if (!S.AzEnabled) return "AutoZoom is turned off. Turn it on from All tools.";
                return "";
            }
        }
        public bool HasProblem { get { return Problem.Length > 0; } }
    }

    // ------------------------------------------------------------------ Appearance page

    class LookVm : Obs
    {
        readonly Shell shell;
        public ObservableCollection<Choice> Swatches { get; private set; }

        public LookVm(Shell shell)
        {
            this.shell = shell;
            Swatches = new ObservableCollection<Choice>();
            foreach (var s in Theme.Swatches)
            {
                Brush b = s[1] == "Windows" ? Theme.Brush(Theme.WindowsAccent()) : Theme.Brush(s[1]);
                var c = new Choice(s[1], s[0], b, delegate(Choice picked) { Theme.AccentKey = picked.Key; Changed(); });
                c.Mark(string.Equals(s[1], Theme.AccentKey, StringComparison.OrdinalIgnoreCase));
                Swatches.Add(c);
            }
        }

        void Changed()
        {
            Theme.Save();
            Theme.Apply();
            shell.UpdateIcon();
            RaiseAll();
        }

        public bool ThemeSystem { get { return Theme.Mode == "System"; } set { if (value) { Theme.Mode = "System"; Changed(); } } }
        public bool ThemeLight { get { return Theme.Mode == "Light"; } set { if (value) { Theme.Mode = "Light"; Changed(); } } }
        public bool ThemeDark { get { return Theme.Mode == "Dark"; } set { if (value) { Theme.Mode = "Dark"; Changed(); } } }
        public bool IconLock { get { return Theme.Icon == "Lock"; } set { if (value) { Theme.Icon = "Lock"; Changed(); } } }
        public bool IconDot { get { return Theme.Icon == "Dot"; } set { if (value) { Theme.Icon = "Dot"; Changed(); } } }
        public bool IconMode { get { return Theme.Icon == "Mode"; } set { if (value) { Theme.Icon = "Mode"; Changed(); } } }
        public bool Motion { get { return Theme.Motion; } set { Theme.Motion = value; Changed(); } }
    }

    // ------------------------------------------------------------------ Updates and about page

    class AboutVm : Obs
    {
        const string RunName = "WindowsUnlockedToolkit";
        public string VersionTitle { get { return "Windows Unlocked Toolkit " + Program.Version; } }
        public string VersionHint { get { return "Updates aren't automatic yet. The website always has the newest installer, and it keeps your settings."; } }
        public ICommand OpenWebsite { get { return new Cmd(delegate { Procs.Open(Program.Site); }); } }
        public ICommand OpenGitHub { get { return new Cmd(delegate { Procs.Open(Program.Repo); }); } }
        public ICommand OpenIssues { get { return new Cmd(delegate { Procs.Open(Program.Repo + "/issues"); }); } }
        public ICommand OpenLogs { get { return new Cmd(delegate { Directory.CreateDirectory(P.Data); Procs.Open(P.Data); }); } }

        public bool StartWithWindows
        {
            get { return Shell.Demo || Startup.GetRunValue(RunName) != null; }
            set
            {
                Startup.SetRunValue(RunName, value ? "\"" + Process.GetCurrentProcess().MainModule.FileName + "\"" : null);
                Raise();
            }
        }
    }

    // ------------------------------------------------------------------ settings window

    class MainVm : Obs
    {
        string page = "home";
        public LeanVm Lean { get; private set; }
        public MtjVm Mtj { get; private set; }
        public AzVm Az { get; private set; }
        public LookVm Look { get; private set; }
        public AboutVm About { get; private set; }
        public ObservableCollection<ToolCard> Tools { get; private set; }

        public MainVm(Shell shell)
        {
            Lean = new LeanVm(shell);
            Mtj = new MtjVm(shell);
            Az = new AzVm(shell);
            Look = new LookVm(shell);
            About = new AboutVm();
            Tools = new ObservableCollection<ToolCard>();

            var lean = new ToolCard();
            lean.Id = "leanback"; lean.NameText = "Leanback Mode"; lean.MonoText = "L";
            lean.TileBrush = Theme.Gradient(Theme.Parse("#3B6CF6", Colors.Blue), Theme.Parse("#7C5CFF", Colors.Blue));
            lean.BlurbText = "Turn on a controller and your PC gets big and couch-friendly. Turn it off and everything goes back.";
            lean.GetInstalled = delegate { return shell.State.LeanInstalled; };
            lean.GetOn = delegate { return shell.State.LeanEnabled; };
            lean.GetRunning = delegate { return shell.State.LeanRunning; };
            lean.SetOn = shell.SetLeanback;
            lean.Open = new Cmd(delegate { Page = "lean"; });
            Tools.Add(lean);

            var mtj = new ToolCard();
            mtj.Id = "mediatabjump"; mtj.NameText = "Media Tab Jump"; mtj.MonoText = "M";
            mtj.TileBrush = Theme.Gradient(Theme.Parse("#FF8A5B", Colors.Orange), Theme.Parse("#E5487A", Colors.Red));
            mtj.BlurbText = "Click what\u2019s playing in the volume flyout to jump straight to its browser tab.";
            mtj.GetInstalled = delegate { return shell.State.MtjInstalled; };
            mtj.GetOn = delegate { return shell.State.MtjEnabled; };
            mtj.GetRunning = delegate { return shell.State.MtjRunning; };
            mtj.SetOn = shell.SetMtj;
            mtj.Open = new Cmd(delegate { Page = "mtj"; });
            Tools.Add(mtj);

            var az = new ToolCard();
            az.Id = "autozoom"; az.NameText = "AutoZoom"; az.MonoText = "Z";
            az.TileBrush = Theme.Gradient(Theme.Parse("#D98E04", Colors.Orange), Theme.Parse("#B5452A", Colors.Red));
            az.BlurbText = "Pick how big each app is when its window is maximized, and when it isn\u2019t.";
            az.GetInstalled = delegate { return shell.State.AzInstalled; };
            az.GetOn = delegate { return shell.State.AzEnabled; };
            az.GetRunning = delegate { return shell.State.AzRunning; };
            az.SetOn = shell.SetAz;
            az.Open = new Cmd(delegate { Page = "az"; });
            Tools.Add(az);

            string[][] soon =
            {
                new[] { "Window Tab Organizer", "W", "#16A37B", "#0B5F6E" },
                new[] { "Startup Minimizer", "S", "#5B6472", "#2A2F3A" }
            };
            foreach (var s in soon)
            {
                var c = new ToolCard();
                c.NameText = s[0]; c.MonoText = s[1]; c.BadgeText = "Coming soon";
                c.TileBrush = Theme.Gradient(Theme.Parse(s[2], Colors.Gray), Theme.Parse(s[3], Colors.Black));
                c.BlurbText = "Being added to the toolkit.";
                Tools.Add(c);
            }
        }

        public string Page
        {
            get { return page; }
            set { page = value; RaiseAll(); }
        }

        public bool IsHome { get { return page == "home"; } set { if (value) Page = "home"; } }
        public bool IsLean { get { return page == "lean"; } set { if (value) Page = "lean"; } }
        public bool IsMtj { get { return page == "mtj"; } set { if (value) Page = "mtj"; } }
        public bool IsAz { get { return page == "az"; } set { if (value) Page = "az"; } }
        public bool IsLook { get { return page == "look"; } set { if (value) Page = "look"; } }
        public bool IsAbout { get { return page == "about"; } set { if (value) Page = "about"; } }
        public string VersionText { get { return "Version " + Program.Version; } }
        public string FooterText { get { return Shell.Paused ? "Paused until " + Shell.PausedUntil.ToString("t", CultureInfo.CurrentCulture) : "Free and open source"; } }

        public void Refresh()
        {
            foreach (var t in Tools) t.RaiseAll();
            Lean.StatusChanged();
            Mtj.RaiseAll();
            Az.RaiseAll();
            RaiseAll();
        }
    }

    // ------------------------------------------------------------------ tray menu

    class Tile : Obs
    {
        public string Label { get; set; }
        public Func<string> GetSub;
        public Func<bool> GetOn, GetEnabled;
        public Action<bool> SetOn;
        public string Sub { get { return GetSub(); } }
        public bool Enabled { get { return GetEnabled(); } }
        public bool IsOn
        {
            get { return GetOn(); }
            set { SetOn(value); RaiseAll(); }
        }
    }

    class FlyoutVm : Obs
    {
        readonly Shell shell;
        public ObservableCollection<Tile> Tiles { get; private set; }
        public ICommand ToggleTv { get; private set; }
        public ICommand Pause { get; private set; }
        public ICommand OpenSettings { get; private set; }
        public ICommand Quit { get; private set; }

        public FlyoutVm(Shell shell)
        {
            this.shell = shell;
            ToggleTv = new Cmd(shell.ToggleTv);
            Pause = new Cmd(delegate { if (Shell.Paused) shell.Resume(); else shell.PauseAll(); });
            OpenSettings = new Cmd(delegate { shell.ShowSettings(null); });
            Quit = new Cmd(shell.Quit);
            Tiles = new ObservableCollection<Tile>();

            Tiles.Add(MakeTile("Leanback Mode",
                delegate { return S.LeanInstalled; },
                delegate { return S.LeanEnabled; },
                delegate { return S.LeanRunning ? "Follows controller" : "Starting\u2026"; },
                shell.SetLeanback));
            Tiles.Add(MakeTile("Controller mouse",
                delegate { return S.AmxInstalled; },
                delegate { return S.AmxEnabled; },
                delegate { return S.Mouse.StartsWith("paused") ? "Paused for a game" : S.AmxRunning ? "Left stick moves" : "Starting\u2026"; },
                shell.SetMouse));
            Tiles.Add(MakeTile("Media Tab Jump",
                delegate { return S.MtjInstalled; },
                delegate { return S.MtjEnabled; },
                delegate { return S.MtjRunning ? "Edge, Chrome, Brave" : "Starting\u2026"; },
                shell.SetMtj));
            Tiles.Add(MakeTile("Hover glow",
                delegate { return S.MtjInstalled; },
                delegate { return S.GlowOn && S.MtjEnabled; },
                delegate { return GlowName(); },
                shell.SetGlow));
            Tiles.Add(MakeTile("AutoZoom",
                delegate { return S.AzInstalled; },
                delegate { return S.AzEnabled; },
                delegate { return S.AzRunning ? AzSummary() : "Starting\u2026"; },
                shell.SetAz));
        }

        string AzSummary()
        {
            int n = shell.Vm.Az.Apps.Count;
            return n == 0 ? "No apps yet" : n == 1 ? "1 app" : n + " apps";
        }

        Snap S { get { return shell.State; } }

        string GlowName()
        {
            string style = Ini.Get(Ini.Read(P.MtjSettings), "GlowStyle", "Shift");
            switch (style)
            {
                case "Accent": return "Accent colour";
                case "Sunset": return "Sunset";
                case "Ocean": return "Ocean";
                case "White": return "Soft white";
                default: return "Colour shift";
            }
        }

        static Tile MakeTile(string label, Func<bool> installed, Func<bool> on, Func<string> sub, Action<bool> set)
        {
            var t = new Tile();
            t.Label = label;
            t.GetEnabled = delegate { return installed() && !Shell.Paused; };
            t.GetOn = delegate { return installed() && on() && !Shell.Paused; };
            t.GetSub = delegate
            {
                if (!installed()) return "Not installed";
                if (Shell.Paused) return "Paused";
                return on() ? sub() : "Off";
            };
            t.SetOn = set;
            return t;
        }

        public bool LeanInstalled { get { return S.LeanInstalled; } }

        public Brush HeroBrush
        {
            get
            {
                if (Shell.Paused || !S.LeanRunning) return Theme.Gradient(Theme.Parse("#5B6472", Colors.Gray), Theme.Parse("#2A2F3A", Colors.Black));
                return S.Tv ? Theme.Gradient(Theme.Accent, Theme.Parse("#22183F", Colors.Black))
                            : Theme.Gradient(Theme.Parse("#3A4256", Colors.Gray), Theme.Parse("#262B38", Colors.Black));
            }
        }

        public string HeroTitle
        {
            get
            {
                if (Shell.Paused) return "Toolkit paused";
                if (!S.LeanRunning) return "Leanback Mode is off";
                return S.Tv ? "Leanback mode" : "Desktop mode";
            }
        }

        public string HeroSub
        {
            get
            {
                if (Shell.Paused) return "Back on at " + Shell.PausedUntil.ToString("t", CultureInfo.CurrentCulture);
                if (!S.LeanRunning) return "Start it to follow your controller";
                string controller = Ini.Get(S.Status, "Controller", "");
                if (S.Tv && Ini.Get(S.Status, "Why", "") == "controller asleep") return "Controller asleep. Staying big.";
                if (S.Tv && controller.Length > 0) return Shell.ShortName(controller) + " is on";
                return S.Tv ? "Switched by you" : "Turn on a controller to lean back";
            }
        }

        public string HeroCta
        {
            get
            {
                if (Shell.Paused) return "Resume";
                if (!S.LeanRunning) return "Start Leanback Mode";
                return S.Tv ? "Back to desktop" : "Switch to Leanback mode";
            }
        }

        public string PauseText { get { return Shell.Paused ? "Resume everything" : "Pause everything for 1 hour"; } }

        public void Refresh()
        {
            foreach (var t in Tiles) t.RaiseAll();
            RaiseAll();
        }
    }

    // ------------------------------------------------------------------ the app

    class Shell
    {
        public static bool Demo;          // self-test: sample data, never touches files or other apps
        public static bool Paused;
        public static DateTime PausedUntil;

        public Snap State = new Snap();
        public List<ScreenInfo> ScreenList = new List<ScreenInfo>();
        string screensKey;
        readonly MainVm vm;
        readonly FlyoutVm fvm;
        WF.NotifyIcon tray;
        WF.ContextMenuStrip trayMenu;
        Window main, flyout;
        DispatcherTimer poll;
        string lastKey;
        DateTime statusStamp, leanStamp, mtjStamp, azStamp, flyoutHidden;
        readonly List<string> pausedWhat = new List<string>();
        SD.Icon currentIcon;
        bool lastTv;

        public Shell(bool demo)
        {
            Demo = demo;
            vm = new MainVm(this);
            fvm = new FlyoutVm(this);
        }

        public MainVm Vm { get { return vm; } }
        public FlyoutVm Fvm { get { return fvm; } }

        public void Start(bool showSettings)
        {
            Log.Write("started (version " + Program.Version + ")");
            RefreshScreens();
            Refresh();
            vm.Lean.LoadSettings();
            vm.Lean.LoadLayout(null);
            vm.Mtj.Load();
            vm.Az.Load();
            azStamp = Stamp(P.AzSettings);
            Leanback.ShowOwnIcon(false); // one tray icon for everything

            trayMenu = new WF.ContextMenuStrip();
            trayMenu.Items.Add("Open settings", null, delegate { ShowSettings(null); });
            trayMenu.Items.Add("Switch Leanback / desktop mode", null, delegate { ToggleTv(); });
            var pauseItem = new WF.ToolStripMenuItem("Pause everything for 1 hour", null, delegate { if (Paused) Resume(); else PauseAll(); });
            trayMenu.Items.Add(pauseItem);
            trayMenu.Items.Add(new WF.ToolStripSeparator());
            trayMenu.Items.Add("Quit toolkit", null, delegate { Quit(); });
            trayMenu.Opening += delegate { pauseItem.Text = Paused ? "Resume everything" : "Pause everything for 1 hour"; };

            tray = new WF.NotifyIcon();
            tray.Text = "Windows Unlocked Toolkit";
            tray.MouseUp += delegate(object s, WF.MouseEventArgs e)
            {
                if (e.Button == WF.MouseButtons.Left) ToggleFlyout();
                else if (e.Button == WF.MouseButtons.Right) { HideFlyout(); trayMenu.Show(WF.Cursor.Position); }
            };
            UpdateIcon();
            tray.Visible = true;

            Theme.Changed += delegate { UpdateIcon(); vm.Refresh(); fvm.Refresh(); };
            SystemEvents.UserPreferenceChanged += delegate(object s, UserPreferenceChangedEventArgs e)
            {
                if (e.Category == UserPreferenceCategory.General || e.Category == UserPreferenceCategory.Color || e.Category == UserPreferenceCategory.VisualStyle)
                    Application.Current.Dispatcher.BeginInvoke(new Action(Theme.Apply));
            };

            poll = new DispatcherTimer();
            poll.Interval = TimeSpan.FromMilliseconds(1000);
            poll.Tick += delegate { Refresh(); };
            poll.Start();

            Listen(Program.ShowEventName, delegate { ShowSettings(null); });
            Listen(Program.ExitEventName, Quit);

            if (showSettings) ShowSettings(null);
        }

        void Listen(string name, Action action)
        {
            var d = Application.Current.Dispatcher;
            var handle = new EventWaitHandle(false, EventResetMode.AutoReset, name);
            var t = new Thread(delegate()
            {
                while (true)
                {
                    handle.WaitOne();
                    d.BeginInvoke(action);
                }
            });
            t.IsBackground = true;
            t.Start();
        }

        // Turns "8BitDo (2DC8:301A)" into "8BitDo"; "Xbox-style controller (XInput slot 1)" into "Xbox-style controller".
        public static string ShortName(string controller)
        {
            int i = controller.IndexOf(" (", StringComparison.Ordinal);
            return i > 0 ? controller.Substring(0, i) : controller;
        }

        // ---- reading state

        static DateTime Stamp(string path)
        {
            try { return File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue; } catch { return DateTime.MinValue; }
        }

        public void NoteOwnWrite(string path) { if (path == P.LeanSettings) leanStamp = Stamp(path); if (path == P.AzSettings) azStamp = Stamp(path); }

        // The connected screens. Checked every second (cheap), so plugging in a screen or switching a TV on shows up.
        public void RefreshScreens()
        {
            List<ScreenInfo> list;
            if (Demo)
            {
                list = new List<ScreenInfo>();
                var a = new ScreenInfo(); a.Name = "DELL U2723QE"; a.Primary = true; a.Right = 3840; a.Bottom = 2160; list.Add(a);
                var b = new ScreenInfo(); b.Name = "LG TV SSCR2"; b.Left = 3840; b.Right = 7680; b.Bottom = 2160; list.Add(b);
            }
            else list = Screens.List();
            var sb = new StringBuilder();
            foreach (var x in list) sb.Append(x.Name).Append(x.Primary).Append(x.Width).Append('x').Append(x.Height).Append('|');
            string key = sb.ToString();
            if (key == screensKey) return;
            screensKey = key;
            ScreenList = list;
            vm.Lean.ScreensChanged();
            vm.Mtj.RaiseAll();
            vm.Az.ScreensChanged();
        }

        public void Refresh()
        {
            if (Demo) return;
            RefreshScreens();
            var s = new Snap();
            s.LeanInstalled = Leanback.Installed;
            s.LeanRunning = s.LeanInstalled && Procs.Running("LeanbackMode");
            s.LeanEnabled = Leanback.Enabled;
            s.AmxInstalled = Amx.Installed;
            s.AmxRunning = s.AmxInstalled && Procs.Running("antimicrox");
            s.AmxEnabled = Amx.Enabled;
            s.MtjInstalled = Mtj.Installed;
            s.MtjRunning = s.MtjInstalled && Procs.Running("MediaTabJump");
            s.MtjEnabled = Mtj.Enabled;
            s.GlowOn = !File.Exists(P.MtjGlowOff);
            s.AzInstalled = Az.Installed;
            s.AzRunning = s.AzInstalled && Procs.Running("AutoZoom");
            s.AzEnabled = Az.Enabled;
            s.Status = s.LeanRunning ? Ini.Read(P.LeanStatus) : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            State = s;

            // Leanback Mode rewrites its settings file when it starts (or when you edit it by hand): reload.
            var ls = Stamp(P.LeanSettings);
            if (ls != leanStamp)
            {
                leanStamp = ls;
                vm.Lean.LoadSettings();
                if (s.LeanRunning && Ini.Bool(Ini.Read(P.LeanSettings), "ShowTrayIcon", true)) Leanback.ShowOwnIcon(false);
            }
            var ms = Stamp(P.MtjSettings);
            if (ms != mtjStamp) { mtjStamp = ms; vm.Mtj.Load(); }
            var zs = Stamp(P.AzSettings);
            if (zs != azStamp) { azStamp = zs; vm.Az.Flush(); vm.Az.Load(); }

            if (Paused && DateTime.Now >= PausedUntil) Resume();

            string key = s.Key() + Paused;
            if (key == lastKey) return;
            lastKey = key;
            vm.Refresh();
            fvm.Refresh();
            if (s.Tv != lastTv) { lastTv = s.Tv; UpdateIcon(); }
            if (tray != null)
                tray.Text = Paused ? "Windows Unlocked Toolkit (paused)" : s.LeanRunning ? "Windows Unlocked Toolkit: " + (s.Tv ? "Leanback mode" : "desktop mode") : "Windows Unlocked Toolkit";
        }

        // ---- actions

        public void ToggleTv()
        {
            if (Paused) { Resume(); return; }
            if (!State.LeanInstalled) { ShowSettings("lean"); return; }
            if (!State.LeanRunning) { SetLeanback(true); return; }
            if (!Leanback.Toggle()) Log.Write("couldn't reach Leanback Mode to switch modes");
        }

        public void SetLeanback(bool on)
        {
            if (Paused) Resume();
            Startup.SetShortcut(Leanback.Shortcut, on);
            Log.Write("Leanback Mode turned " + (on ? "on" : "off"));
            if (on) { Leanback.Start(); Thread.Sleep(150); Refresh(); }
            else Procs.Background(Leanback.Exit, Refresh);
            Refresh();
        }

        public void SetMouse(bool on)
        {
            if (Paused) Resume();
            Startup.SetShortcut(Amx.Shortcut, on);
            Log.Write("controller mouse turned " + (on ? "on" : "off"));
            Procs.Background(delegate { if (on) Amx.Start(); else Amx.Stop(); }, Refresh);
        }

        public void SetMtj(bool on)
        {
            if (Paused) Resume();
            Startup.SetRun(Mtj.RunName, on);
            Log.Write("Media Tab Jump turned " + (on ? "on" : "off"));
            Procs.Background(delegate { if (on) Mtj.Start(); else Mtj.Stop(); }, Refresh);
        }

        public void SetAz(bool on)
        {
            if (Paused) Resume();
            Startup.SetRun(Az.RunName, on);
            Log.Write("AutoZoom turned " + (on ? "on" : "off"));
            Procs.Background(delegate { if (on) Az.Start(); else Az.Stop(); }, Refresh);
        }

        public void SetGlow(bool on)
        {
            if (!Demo)
            {
                try
                {
                    if (on) { if (File.Exists(P.MtjGlowOff)) File.Delete(P.MtjGlowOff); }
                    else { Directory.CreateDirectory(P.MtjDir); File.WriteAllText(P.MtjGlowOff, "Hover glow turned off from Windows Unlocked Toolkit. Delete this file to turn it back on." + Environment.NewLine); }
                }
                catch (Exception ex) { Log.Write("couldn't change the hover glow: " + ex.Message); }
            }
            if (on && State.MtjInstalled && !State.MtjEnabled) SetMtj(true); // the glow needs Media Tab Jump running
            Refresh();
        }

        // Stops what's running for an hour (startup entries stay as they are). Comes back on its own.
        public void PauseAll()
        {
            if (Paused) return;
            Paused = true;
            PausedUntil = DateTime.Now.AddHours(1);
            pausedWhat.Clear();
            var s = State;
            if (s.LeanRunning) pausedWhat.Add("lean");
            if (s.AmxRunning) pausedWhat.Add("amx");
            if (s.MtjRunning) pausedWhat.Add("mtj");
            if (s.AzRunning) pausedWhat.Add("az");
            Log.Write("paused everything until " + PausedUntil.ToString("HH:mm", CultureInfo.InvariantCulture));
            var what = new List<string>(pausedWhat);
            Procs.Background(delegate
            {
                if (what.Contains("lean")) Leanback.Exit(); // back to desktop mode first
                if (what.Contains("amx")) Amx.Stop();
                if (what.Contains("mtj")) Mtj.Stop();
                if (what.Contains("az")) Az.Stop();
            }, Refresh);
            lastKey = null;
            UpdateIcon();
            Refresh();
        }

        public void Resume()
        {
            if (!Paused) return;
            Paused = false;
            Log.Write("resumed");
            var what = new List<string>(pausedWhat);
            pausedWhat.Clear();
            Procs.Background(delegate
            {
                if (what.Contains("amx")) Amx.Start();
                if (what.Contains("lean")) Leanback.Start();
                if (what.Contains("mtj")) Mtj.Start();
                if (what.Contains("az")) Az.Start();
            }, Refresh);
            lastKey = null;
            UpdateIcon();
            Refresh();
        }

        public void Quit()
        {
            Log.Write("quit");
            try { vm.Lean.Flush(); vm.Mtj.Flush(); vm.Az.Flush(); } catch { }
            if (Paused) Resume(); // never leave tools stopped behind
            Leanback.ShowOwnIcon(true); // Leanback Mode shows its own icon again while the toolkit is closed
            if (tray != null) { tray.Visible = false; tray.Dispose(); }
            Application.Current.Shutdown();
        }

        // ---- windows

        public Window BuildMain()
        {
            var w = (Window)Ui.LoadXaml("MainWindow.xaml");
            w.DataContext = vm;
            w.Icon = IconImage();
            w.PreviewKeyDown += delegate(object s, KeyEventArgs e)
            {
                Key k = e.Key == Key.System ? e.SystemKey : e.Key;
                if (vm.Lean.OfferKey(k, Keyboard.Modifiers)) e.Handled = true;
            };
            w.Closing += delegate(object s, CancelEventArgs e)
            {
                vm.Lean.Capturing = false;
                vm.Lean.Flush();
                vm.Mtj.Flush();
                vm.Az.Flush();
                if (!Demo) { e.Cancel = true; w.Hide(); }
            };
            return w;
        }

        public void ShowSettings(string page)
        {
            HideFlyout();
            if (main == null) main = BuildMain();
            if (page != null) vm.Page = page;
            Refresh();
            main.Show();
            if (main.WindowState == WindowState.Minimized) main.WindowState = WindowState.Normal;
            main.Activate();
        }

        public Window BuildFlyout()
        {
            var w = (Window)Ui.LoadXaml("Flyout.xaml");
            w.DataContext = fvm;
            w.Deactivated += delegate { HideFlyout(); };
            w.PreviewKeyDown += delegate(object s, KeyEventArgs e) { if (e.Key == Key.Escape) HideFlyout(); };
            return w;
        }

        void ToggleFlyout()
        {
            if (flyout != null && flyout.IsVisible) { HideFlyout(); return; }
            if ((DateTime.Now - flyoutHidden).TotalMilliseconds < 300) return; // the click that just closed it
            if (flyout == null) flyout = BuildFlyout();
            Refresh();
            fvm.Refresh();

            // Place it above the tray, next to the pointer (taskbar on any edge).
            var cursor = WF.Cursor.Position;
            var area = WF.Screen.FromPoint(cursor).WorkingArea;
            var bounds = WF.Screen.FromPoint(cursor).Bounds;
            double scale;
            using (var g = SD.Graphics.FromHwnd(IntPtr.Zero)) scale = g.DpiX / 96.0;
            flyout.Opacity = 0;
            flyout.Left = -30000;
            flyout.Show();
            flyout.UpdateLayout();
            double w = flyout.ActualWidth, h = flyout.ActualHeight, inset = 6;
            double left = cursor.X / scale - w / 2;
            double top = area.Bottom / scale - h + inset;
            if (area.Top > bounds.Top) top = area.Top / scale - inset;                        // taskbar at the top
            if (area.Left > bounds.Left) left = area.Left / scale - inset;                    // taskbar on the left
            if (area.Right < bounds.Right) left = area.Right / scale - w + inset;             // taskbar on the right
            if (area.Left > bounds.Left || area.Right < bounds.Right) top = cursor.Y / scale - h / 2;
            left = Math.Max(area.Left / scale - inset, Math.Min(left, area.Right / scale - w + inset));
            top = Math.Max(area.Top / scale - inset, Math.Min(top, area.Bottom / scale - h + inset));
            flyout.Left = left;
            flyout.Top = top;
            flyout.Activate();

            var panel = flyout.FindName("Panel") as FrameworkElement;
            var slide = panel != null ? panel.RenderTransform as TranslateTransform : null;
            if (Theme.Motion && slide != null)
            {
                var ease = new CubicEase();
                ease.EasingMode = EasingMode.EaseOut;
                var move = new DoubleAnimation(14, 0, TimeSpan.FromMilliseconds(180));
                move.EasingFunction = ease;
                slide.BeginAnimation(TranslateTransform.YProperty, move);
                flyout.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140)));
            }
            else
            {
                flyout.BeginAnimation(UIElement.OpacityProperty, null);
                flyout.Opacity = 1;
            }
        }

        void HideFlyout()
        {
            if (flyout == null || !flyout.IsVisible) return;
            flyout.Hide();
            flyoutHidden = DateTime.Now;
        }

        // ---- tray icon

        public void UpdateIcon()
        {
            if (tray == null) return;
            int size = WF.SystemInformation.SmallIconSize.Width;
            var bmp = DrawIcon(size, Theme.Icon, Paused ? Color.FromRgb(0x8A, 0x8A, 0x96) : Theme.Accent, State.Tv, Theme.TaskbarDark());
            IntPtr h = bmp.GetHicon();
            var icon = SD.Icon.FromHandle(h);
            tray.Icon = icon;
            if (currentIcon != null) { DestroyIcon(currentIcon.Handle); currentIcon.Dispose(); }
            currentIcon = icon;
            bmp.Dispose();
        }

        [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr handle);

        ImageSource IconImage()
        {
            using (var bmp = DrawIcon(64, "Lock", Theme.Accent, false, false))
            {
                var ms = new MemoryStream();
                bmp.Save(ms, SD.Imaging.ImageFormat.Png);
                ms.Position = 0;
                var img = new BitmapImage();
                img.BeginInit();
                img.StreamSource = ms;
                img.CacheOption = BitmapCacheOption.OnLoad;
                img.EndInit();
                img.Freeze();
                return img;
            }
        }

        static SD.Color Gdi(Color c) { return SD.Color.FromArgb(c.A, c.R, c.G, c.B); }

        static SD.Drawing2D.GraphicsPath Rounded(float x, float y, float w, float h, float r)
        {
            var p = new SD.Drawing2D.GraphicsPath();
            float d = r * 2;
            p.AddArc(x, y, d, d, 180, 90);
            p.AddArc(x + w - d, y, d, d, 270, 90);
            p.AddArc(x + w - d, y + h - d, d, d, 0, 90);
            p.AddArc(x, y + h - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        // Lock: accent tile with an open padlock.  Dot: accent circle.
        // Mode: a TV (filled, accent) in Leanback mode, a monitor outline in desktop mode.
        public static SD.Bitmap DrawIcon(int size, string style, Color accent, bool tv, bool darkTaskbar)
        {
            var bmp = new SD.Bitmap(size, size, SD.Imaging.PixelFormat.Format32bppArgb);
            using (var g = SD.Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SD.Drawing2D.SmoothingMode.AntiAlias;
                g.Clear(SD.Color.Transparent);
                float s = size / 16f;
                var fill = Gdi(accent);
                var ink = Theme.Luma(accent) > 0.62 ? SD.Color.FromArgb(27, 27, 33) : SD.Color.White;
                var line = darkTaskbar ? SD.Color.White : SD.Color.FromArgb(32, 32, 38);
                if (style == "Dot")
                {
                    using (var b = new SD.SolidBrush(fill)) g.FillEllipse(b, 2.5f * s, 2.5f * s, 11 * s, 11 * s);
                }
                else if (style == "Mode")
                {
                    using (var path = Rounded(1 * s, 2.5f * s, 14 * s, 9.5f * s, 2 * s))
                    using (var pen = new SD.Pen(tv ? fill : line, 1.4f * s))
                    using (var b = new SD.SolidBrush(fill))
                    {
                        if (tv) g.FillPath(b, path); else g.DrawPath(pen, path);
                        pen.StartCap = pen.EndCap = SD.Drawing2D.LineCap.Round;
                        if (tv) { g.DrawLine(pen, 4.5f * s, 14.2f * s, 11.5f * s, 14.2f * s); }
                        else { g.DrawLine(pen, 8 * s, 12 * s, 8 * s, 14 * s); g.DrawLine(pen, 5 * s, 14.3f * s, 11 * s, 14.3f * s); }
                    }
                }
                else
                {
                    using (var path = Rounded(0.5f * s, 0.5f * s, 15 * s, 15 * s, 3.6f * s))
                    using (var b = new SD.SolidBrush(fill)) g.FillPath(b, path);
                    using (var pen = new SD.Pen(ink, 1.5f * s))
                    using (var body = Rounded(4.2f * s, 7.4f * s, 7.6f * s, 5.6f * s, 1.1f * s))
                    {
                        pen.StartCap = pen.EndCap = SD.Drawing2D.LineCap.Round;
                        using (var b = new SD.SolidBrush(ink)) g.FillPath(b, body);
                        g.DrawArc(pen, 5.6f * s, 2.8f * s, 4.8f * s, 5.4f * s, 180, 160); // open shackle
                        g.DrawLine(pen, 5.6f * s, 5.5f * s, 5.6f * s, 7.6f * s);
                    }
                }
            }
            return bmp;
        }
    }

    // ------------------------------------------------------------------ self-test (CI)

    // Renders every page with sample data to PNG files and fails on any binding or loading error.
    static class SelfTest
    {
        class Collector : TraceListener
        {
            public readonly StringBuilder Text = new StringBuilder();
            public int Count;
            public override void Write(string message) { Text.Append(message); }
            public override void WriteLine(string message) { Text.AppendLine(message); Count++; }
        }

        public static int Run(string outDir, string layoutPath)
        {
            var report = new StringBuilder();
            var errors = new Collector();
            int rc = 0;
            try
            {
                Directory.CreateDirectory(outDir);
                PresentationTraceSources.Refresh();
                PresentationTraceSources.DataBindingSource.Listeners.Add(errors);
                PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Warning;

                var app = new Application();
                app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                Ui.LoadTheme(app);
                Theme.Mode = "Light"; Theme.AccentKey = "#3B6CF6"; Theme.Apply();

                var shell = new Shell(true);
                var s = shell.State;
                s.LeanInstalled = s.LeanRunning = s.LeanEnabled = true;
                s.AmxInstalled = s.AmxRunning = s.AmxEnabled = true;
                s.MtjInstalled = s.MtjRunning = s.MtjEnabled = s.GlowOn = true;
                s.AzInstalled = s.AzRunning = s.AzEnabled = true;
                shell.RefreshScreens();
                shell.Vm.Az.Sample();
                s.Status = Ini.Pairs("Mode", "TV", "Why", "controller on", "Controller", "8BitDo (2DC8:301A)", "LastController", "8BitDo (2DC8:301A)", "Mouse", "on", "Hotkey", "Ctrl+Alt+T");
                shell.Vm.Lean.LoadSettings();
                shell.Vm.Lean.LoadLayout(layoutPath);
                shell.Vm.Lean.StatusChanged();
                shell.Vm.Refresh();
                report.AppendLine("controller layout " + (layoutPath != null && File.Exists(layoutPath) ? "loaded: " + shell.Vm.Lean.ActionLabel("A") + " on A" : "not given"));

                var main = Detach(shell.BuildMain());
                foreach (var page in new[] { "home", "lean", "mtj", "az", "look", "about" })
                {
                    shell.Vm.Page = page;
                    Save(main, 1100, Path.Combine(outDir, "settings-" + page + ".png"), report);
                }
                shell.Vm.Page = "az";
                foreach (var tab in shell.Vm.Az.ScreenTabs) if (tab.Key == "LG TV SSCR2") tab.IsSelected = true;
                Save(main, 1100, Path.Combine(outDir, "settings-az-screen.png"), report);
                Theme.Mode = "Dark"; Theme.AccentKey = "#7C5CFF"; Theme.Apply();
                shell.Vm.Page = "lean";
                Save(main, 1100, Path.Combine(outDir, "settings-lean-dark.png"), report);
                Theme.Mode = "Light"; Theme.AccentKey = "#3B6CF6"; Theme.Apply();

                var fly = Detach(shell.BuildFlyout());
                shell.Fvm.Refresh();
                Save(fly, 360, Path.Combine(outDir, "tray-menu.png"), report);

                foreach (var style in new[] { "Lock", "Dot", "Mode" })
                    using (var bmp = Shell.DrawIcon(64, style, Theme.Accent, true, true)) bmp.Save(Path.Combine(outDir, "icon-" + style.ToLowerInvariant() + ".png"));
            }
            catch (Exception ex)
            {
                report.AppendLine("FAILED: " + ex);
                rc = 1;
            }
            if (errors.Count > 0)
            {
                report.AppendLine("Binding problems:");
                report.Append(errors.Text);
                rc = 1;
            }
            report.AppendLine(rc == 0 ? "OK" : "FAILED");
            try { File.WriteAllText(Path.Combine(outDir, "selftest.txt"), report.ToString()); } catch { }
            return rc;
        }

        // Takes a window's content out so it can be laid out at its full height (windows are limited to the screen).
        static FrameworkElement Detach(Window w)
        {
            var root = (FrameworkElement)w.Content;
            object data = w.DataContext;
            w.Content = null;
            root.DataContext = data;
            root.SetResourceReference(TextElement.FontFamilyProperty, "UiFont");
            root.SetResourceReference(TextElement.ForegroundProperty, "Ink");
            TextElement.SetFontSize(root, 14);
            return root;
        }

        static void Save(FrameworkElement root, double width0, string file, StringBuilder report)
        {
            for (int i = 0; i < 3; i++)
            {
                Ui.Settle();
                root.Measure(new Size(width0, double.PositiveInfinity));
                root.Arrange(new Rect(new Point(0, 0), root.DesiredSize));
                root.UpdateLayout();
            }
            int width = (int)Math.Ceiling(root.ActualWidth), height = (int)Math.Ceiling(root.ActualHeight);
            var rtb = new RenderTargetBitmap(Math.Max(1, width), Math.Max(1, height), 96, 96, PixelFormats.Pbgra32);
            rtb.Render(root);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(rtb));
            using (var f = File.Create(file)) enc.Save(f);
            report.AppendLine("rendered " + System.IO.Path.GetFileName(file) + " " + width + "x" + height);
        }
    }
}
