// AutoZoom
// ---------------------------------------------------------------------------
// Zooms apps in or out on their own when you maximize or restore their window.
// For each app you choose two zoom levels: one for when its window is maximized
// and one for when it isn't. AutoZoom watches the window in front and, when it
// changes between maximized and not, presses the app's own zoom keys for you
// (Ctrl+= to zoom in, Ctrl+- to zoom out, Ctrl+0 to reset).
//
// Settings: %LOCALAPPDATA%\AutoZoom\settings.ini  (the toolkit tray app edits it; so can you)
//   App.<process name>=<steps when maximized>,<steps when not maximized>
//       e.g.  App.claude=4,1   One step is one press of Ctrl+= (positive) or Ctrl+- (negative)
//             counted from the app's normal 100% size.
//   App.<process name>@<screen name>=<maximized>,<not maximized>
//       e.g.  App.claude@LG TV SSCR2=7,3   different levels while the window is on that screen.
//             Screen names are the ones Windows Settings > Display shows ("Built-in display" for a
//             laptop's own screen); the toolkit tray app lists them. Screens without a line use App.<name>.
//   PollMs=10     how often the window in front is checked, in ms
//   KeyGapMs=0    pause between key presses; raise to 10-20 if a switch lands on the wrong size
//   Log=true      write one line per zoom change to log.txt (process names only, never window titles)
// The file is re-read within a second of being saved, so changes apply right away.
//
// Every window is tracked on its own, so two windows of the same app (for example two
// accounts) never throw each other's zoom off. Moving a window to another screen re-applies
// that screen's levels once you let go of it. The first time AutoZoom sees a window it
// resets that window to 100% once (Ctrl+0), then sets the right level.
//
// Nothing here goes online. Built on your PC by Install.ps1 with the C# compiler built into Windows.
// ---------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace AutoZoomApp
{
    static class Program
    {
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll")] static extern bool IsZoomed(IntPtr h);
        [DllImport("user32.dll")] static extern bool IsIconic(IntPtr h);
        [DllImport("user32.dll")] static extern bool IsWindow(IntPtr h);
        [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int idx);
        [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vk);
        [DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
        [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr h, uint flags);
        [DllImport("winmm.dll")] static extern uint timeBeginPeriod(uint ms);

        const int GWL_STYLE = -16;
        const int WS_MAXIMIZEBOX = 0x10000;

        class Rule
        {
            public int Max, Normal;
            // Levels for particular screens, by screen name.
            public Dictionary<string, int[]> OnScreen = new Dictionary<string, int[]>(StringComparer.OrdinalIgnoreCase);
        }

        class WinState
        {
            public int Last = -1;      // -1 unknown, 0 not maximized, 1 maximized
            public string LastScreen = "";
            public int Current;        // steps from 100% that this window is at now
            public bool Synced;        // has been reset to 100% once
        }

        static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AutoZoom");
        static readonly string SettingsPath = Path.Combine(Dir, "settings.ini");
        static readonly string LogPath = Path.Combine(Dir, "log.txt");

        static Dictionary<string, Rule> rules = new Dictionary<string, Rule>(StringComparer.OrdinalIgnoreCase);
        static int pollMs = 10, gapMs = 0;
        static bool logOn = true;
        static DateTime stamp;
        static List<ScreenInfo> screens = new List<ScreenInfo>();
        static DateTime screensAt = DateTime.MinValue;

        [STAThread]
        static int Main(string[] args)
        {
            Directory.CreateDirectory(Dir);
            bool created;
            var mutex = new Mutex(true, "Local\\AutoZoom", out created);
            if (!created) return 0; // already running

            try { Run(); }
            catch (Exception ex) { Log("stopped by an error: " + ex.Message); return 1; }
            finally { GC.KeepAlive(mutex); }
            return 0;
        }

        static void Run()
        {
            timeBeginPeriod(1); // lets Sleep(10) really be about 10 ms
            Load();
            Log("started, watching " + rules.Count + (rules.Count == 1 ? " app" : " apps"));

            var names = new Dictionary<uint, string>();
            var wins = new Dictionary<IntPtr, WinState>();
            DateTime nextCheck = DateTime.UtcNow, nextPrune = DateTime.UtcNow.AddMinutes(1);

            while (true)
            {
                Thread.Sleep(pollMs);

                if (DateTime.UtcNow >= nextCheck)
                {
                    nextCheck = DateTime.UtcNow.AddSeconds(1);
                    if (Stamp() != stamp)
                    {
                        Load();
                        foreach (var w in wins.Values) w.Last = -1; // apply the new levels the next time each window is in front
                        Log("settings changed, watching " + rules.Count + (rules.Count == 1 ? " app" : " apps"));
                    }
                }
                if (DateTime.UtcNow >= nextPrune)
                {
                    nextPrune = DateTime.UtcNow.AddMinutes(1);
                    names.Clear(); // process ids get reused
                    foreach (var k in new List<IntPtr>(wins.Keys)) if (!IsWindow(k)) wins.Remove(k);
                }

                IntPtr h = GetForegroundWindow();
                if (h == IntPtr.Zero || IsIconic(h)) continue;
                uint pid;
                GetWindowThreadProcessId(h, out pid);
                string name;
                if (!names.TryGetValue(pid, out name))
                {
                    try { using (var p = Process.GetProcessById((int)pid)) name = p.ProcessName; }
                    catch { name = ""; }
                    names[pid] = name;
                }
                Rule rule;
                if (name.Length == 0 || !rules.TryGetValue(name, out rule)) continue;
                if ((GetWindowLong(h, GWL_STYLE) & WS_MAXIMIZEBOX) == 0) continue; // main windows only, not dialogs or menus

                WinState s;
                if (!wins.TryGetValue(h, out s)) { s = new WinState(); wins[h] = s; }
                int want = IsZoomed(h) ? 1 : 0;
                int[] levels = null;
                string screen = rule.OnScreen.Count > 0 ? ScreenOf(h) : "";
                if (screen.Length > 0) rule.OnScreen.TryGetValue(screen, out levels);
                string where = levels != null ? screen : "";
                if (want == s.Last && where == s.LastScreen) continue;
                if (ModifierHeld()) continue; // e.g. Win still down after Win+Up; try again in a moment
                if (MouseHeld()) continue;    // still dragging the window (maybe across screens); wait until it's let go

                int target = levels != null ? (want == 1 ? levels[0] : levels[1]) : (want == 1 ? rule.Max : rule.Normal);
                string what = (want == 1 ? "maximized" : "not maximized") + (where.Length > 0 ? " on " + where : "");
                if (!s.Synced)
                {
                    // The window may have only just come forward and not be taking keys yet, and a lost
                    // Ctrl+0 would leave the zoom wrong for good. Wait, check again, and press slowly.
                    Thread.Sleep(300);
                    if (GetForegroundWindow() != h) continue;
                    Zoom(true, target, Math.Max(gapMs, 25));
                    Log(name + " " + what + ": first time, reset to 100% then " + Signed(target));
                }
                else
                {
                    int delta = target - s.Current;
                    Zoom(false, delta, gapMs);
                    if (delta != 0) Log(name + " " + what + ": " + Signed(delta));
                }
                s.Current = target;
                s.Synced = true;
                s.Last = want;
                s.LastScreen = where;
            }
        }

        // ---- settings

        static DateTime Stamp()
        {
            try { return File.Exists(SettingsPath) ? File.GetLastWriteTimeUtc(SettingsPath) : DateTime.MinValue; }
            catch { return DateTime.MinValue; }
        }

        static void Load()
        {
            if (!File.Exists(SettingsPath))
            {
                try
                {
                    File.WriteAllText(SettingsPath,
                        "# AutoZoom settings. The toolkit tray app edits this file; you can too." + Environment.NewLine +
                        "# App.<process name>=<steps when maximized>,<steps when not maximized>" + Environment.NewLine +
                        "# App.<process name>@<screen name>=<maximized>,<not maximized>   (levels for one screen)" + Environment.NewLine +
                        "PollMs=10" + Environment.NewLine + "KeyGapMs=0" + Environment.NewLine + "Log=true" + Environment.NewLine);
                }
                catch { }
            }
            var next = new Dictionary<string, Rule>(StringComparer.OrdinalIgnoreCase);
            int poll = 10, gap = 0;
            bool log = true;
            string[] lines;
            for (int attempt = 0; ; attempt++)
            {
                try { lines = File.ReadAllLines(SettingsPath); break; }
                catch (IOException) { if (attempt >= 5) { stamp = Stamp(); return; } Thread.Sleep(80); }
                catch (Exception) { stamp = Stamp(); return; }
            }
            foreach (var raw in lines)
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == '#' || line[0] == ';' || line[0] == '[') continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string key = line.Substring(0, eq).Trim(), value = line.Substring(eq + 1).Trim();
                if (key.StartsWith("App.", StringComparison.OrdinalIgnoreCase))
                {
                    string app = key.Substring(4).Trim(), screen = "";
                    int at = app.IndexOf('@');
                    if (at >= 0) { screen = app.Substring(at + 1).Trim(); app = app.Substring(0, at).Trim(); }
                    if (app.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) app = app.Substring(0, app.Length - 4);
                    string[] parts = value.Split(',');
                    int max, normal;
                    if (app.Length > 0 && parts.Length >= 2 && TryInt(parts[0], out max) && TryInt(parts[1], out normal))
                    {
                        Rule r;
                        if (!next.TryGetValue(app, out r)) { r = new Rule(); next[app] = r; }
                        if (screen.Length == 0) { r.Max = Clamp(max); r.Normal = Clamp(normal); }
                        else r.OnScreen[screen] = new[] { Clamp(max), Clamp(normal) };
                    }
                }
                else if (key.Equals("PollMs", StringComparison.OrdinalIgnoreCase)) { int v; if (TryInt(value, out v)) poll = Math.Max(5, Math.Min(1000, v)); }
                else if (key.Equals("KeyGapMs", StringComparison.OrdinalIgnoreCase)) { int v; if (TryInt(value, out v)) gap = Math.Max(0, Math.Min(200, v)); }
                else if (key.Equals("Log", StringComparison.OrdinalIgnoreCase)) { string v = value.ToLowerInvariant(); log = !(v == "0" || v == "false" || v == "off" || v == "no"); }
            }
            rules = next;
            pollMs = poll;
            gapMs = gap;
            logOn = log;
            stamp = Stamp();
        }

        static bool TryInt(string s, out int v) { return int.TryParse(s.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out v); }
        // Name of the screen the window is on (mostly), as Windows Settings shows it.
        static string ScreenOf(IntPtr h)
        {
            IntPtr mon = MonitorFromWindow(h, 2); // MONITOR_DEFAULTTONEAREST
            ScreenInfo s = Screens.FromHandle(screens, mon);
            if (s == null || DateTime.UtcNow - screensAt > TimeSpan.FromSeconds(10))
            {
                screens = Screens.List(); // screens were plugged in, unplugged or rearranged
                screensAt = DateTime.UtcNow;
                s = Screens.FromHandle(screens, mon);
            }
            return s != null ? s.Name : "";
        }

        static bool MouseHeld() { return (GetAsyncKeyState(0x01) & 0x8000) != 0 || (GetAsyncKeyState(0x02) & 0x8000) != 0; }

        static int Clamp(int steps) { return Math.Max(-15, Math.Min(15, steps)); }
        static string Signed(int steps) { return steps.ToString("+0;-0;0", CultureInfo.InvariantCulture) + (Math.Abs(steps) == 1 ? " step" : " steps"); }

        // ---- keys

        static bool ModifierHeld()
        {
            int[] keys = { 0x10, 0x11, 0x12, 0x5B, 0x5C }; // Shift, Ctrl, Alt, left and right Windows key
            foreach (int k in keys) if ((GetAsyncKeyState(k) & 0x8000) != 0) return true;
            return false;
        }

        static void Tap(byte vk, int gap)
        {
            keybd_event(vk, 0, 0, UIntPtr.Zero);
            keybd_event(vk, 0, 2, UIntPtr.Zero);
            if (gap > 0) Thread.Sleep(gap);
        }

        // Holds Ctrl once and sends every key press back to back.
        static void Zoom(bool reset, int steps, int gap)
        {
            if (!reset && steps == 0) return;
            keybd_event(0x11, 0, 0, UIntPtr.Zero);              // Ctrl down
            if (reset) Tap(0x30, gap);                          // Ctrl+0
            byte key = steps > 0 ? (byte)0xBB : (byte)0xBD;     // Ctrl+= or Ctrl+-
            for (int i = 0; i < Math.Abs(steps); i++) Tap(key, gap);
            keybd_event(0x11, 0, 2, UIntPtr.Zero);              // Ctrl up
        }

        // ---- log

        static void Log(string msg)
        {
            if (!logOn) return;
            try
            {
                if (File.Exists(LogPath) && new FileInfo(LogPath).Length > 200000) File.Delete(LogPath);
                File.AppendAllText(LogPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture) + "  " + msg + Environment.NewLine);
            }
            catch { }
        }
    }

    // ---------------------------------------------------------------- screens
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
}
