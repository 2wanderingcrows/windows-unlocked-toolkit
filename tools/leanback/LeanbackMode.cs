// Leanback Mode for Windows  (part of Windows Unlocked Toolkit)
// Watches for a game controller: Xbox-style (XInput) pads, plus PlayStation, Switch Pro, 8BitDo and
// generic Bluetooth / 2.4 GHz pads that Windows lists as "HID-compliant game controller".
// Controller on  -> "Leanback mode": bigger Windows scaling on one screen + bigger mouse pointer.
//                    Which screen is up to you (Screen= in settings.ini): your main display, or say the TV.
//                    Other screens are left alone, and the pointer jumps to the Leanback screen.
// Controller off -> back to your normal scaling and pointer size ("desktop mode").
// If the controller only fell asleep (it went off by itself after sitting idle), Leanback mode stays on;
// switching the controller off yourself goes back to desktop mode. See StayInLeanbackWhenAsleep.
// (Internally and in settings.ini, Leanback mode is still called "TV", e.g. TvScale and Mode=TV.)
//
// The switch is hidden behind a frosted overlay:
//   1. blur in (vsync-paced fade)            2. switch scaling + pointer behind it
//   3. wait until the screen stops changing  4. dissolve to a blurred snapshot of the NEW layout
//   5. fade that away into the sharp new layout (a soft "focus pull", no layout jumping)
// The overlay hides the pointer, stays above the taskbar, and is excluded from its own screen captures.
//
// Settings: %LOCALAPPDATA%\LeanbackMode\settings.ini (edit from the tray menu; changes apply live).
//
// Build (no SDK needed, uses the C# compiler that ships with Windows):
//   C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /nologo /target:winexe /optimize+
//     /r:System.Windows.Forms.dll /r:System.Drawing.dll /out:..\LeanbackMode.exe LeanbackMode.cs
// Management options (used by uninstall.ps1):
//   --exit     ask the running copy to close (it switches back to desktop mode first), wait for it, then exit
//   --reset    set display scaling and pointer size back to NormalScale / NormalPointerSize right away
// Test options:
//   --render-backdrop <file.png> [tv|desktop]   save what the overlay looks like, then exit
//   --preview [tv|desktop]                      play the transition without changing any settings, then exit
//   --capture-test <file.txt>                   check that screen captures skip the overlay, then exit
//   --layer-test <layout.amgp> <out.txt> [combo...]  write which button the layout holds for the controller layer

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace LeanbackMode
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            try { Native.SetProcessDpiAwarenessContext(new IntPtr(-4)); } catch { }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            if (args.Length >= 1 && args[0] == "--exit")
            {
                EventWaitHandle ev;
                if (EventWaitHandle.TryOpenExisting(TrayApp.ExitEventName, out ev))
                {
                    using (ev) ev.Set();
                    int me = Process.GetCurrentProcess().Id;
                    var sw = Stopwatch.StartNew();
                    while (sw.Elapsed.TotalSeconds < 20)
                    {
                        bool running = false;
                        foreach (var p in Process.GetProcessesByName("LeanbackMode")) { if (p.Id != me) running = true; p.Dispose(); }
                        if (!running) break;
                        Thread.Sleep(200);
                    }
                }
                return;
            }
            if (args.Length >= 1 && args[0] == "--reset")
            {
                var s = File.Exists(Settings.FilePath) ? Settings.Load() : new Settings();
                DisplayScale.ScreenName = s.ScreenName;
                DisplayScale.Set(s.NormalScale);
                PointerSize.Set(s.NormalPointerSize);
                return;
            }
            if (args.Length >= 3 && args[0] == "--layer-test")
            {
                // Which button the layout holds for the controller layer, and how LayerTap combos are read.
                var sb = new StringBuilder();
                sb.AppendLine("layer=" + (Layer.FindButton(args[1]) ?? "none"));
                for (int i = 3; i < args.Length; i++)
                {
                    var names = new List<string>();
                    foreach (var t in Inject.Parse(args[i])) { Keys k; names.Add(Enum.TryParse(t, true, out k) ? k.ToString() : t); }
                    sb.AppendLine("tap " + args[i] + "=" + string.Join(",", names));
                }
                File.WriteAllText(args[2], sb.ToString());
                return;
            }
            if (args.Length >= 2 && args[0] == "--render-backdrop")
            {
                bool tv = !(args.Length >= 3 && args[2] == "desktop");
                Rectangle b = DisplayScale.TargetBounds(), ind;
                using (var small = Visuals.CaptureSmall(b))
                using (var bg = Visuals.MakeBackdrop(small, b.Size, tv, "preview", out ind))
                {
                    using (var g = Graphics.FromImage(bg)) Visuals.DrawIndicator(g, ind, 0.35, false);
                    bg.Save(args[1], ImageFormat.Png);
                }
                return;
            }
            if (args.Length >= 2 && args[0] == "--capture-test")
            {
                Rectangle b = Screen.PrimaryScreen.Bounds;
                using (var before = Visuals.CaptureSmall(b))
                using (var red = new Bitmap(b.Width, b.Height, PixelFormat.Format32bppPArgb))
                {
                    using (var g = Graphics.FromImage(red)) g.Clear(Color.Red);
                    var v = new Veil(red, b, Rectangle.Empty);
                    v.Show(); v.Refresh(); v.SetAlpha(110);
                    for (int i = 0; i < 3; i++) Native.DwmFlush();
                    Thread.Sleep(30);
                    double d;
                    using (var after = Visuals.CaptureSmall(b)) d = Visuals.Diff(before, after);
                    v.Close();
                    File.WriteAllText(args[1], "diff=" + d.ToString("0.00", CultureInfo.InvariantCulture));
                }
                return;
            }
            if (args.Length >= 3 && args[0] == "--check-game")
            {
                // --check-game <out.txt> <exe path> [<exe path> ...]
                var det = new GameDetector();
                det.Configure(Settings.Load());
                var sb = new StringBuilder();
                for (int i = 2; i < args.Length; i++)
                    sb.AppendLine((det.IsGamePath(args[i]) ? "GAME      " : "not game  ") + args[i]);
                File.WriteAllText(args[1], sb.ToString());
                return;
            }
            if (args.Length >= 1 && args[0] == "--preview")
            {
                bool tv = !(args.Length >= 2 && args[1] == "desktop");
                var t = new Transition(tv, "preview", delegate { Thread.Sleep(1200); return null; }, Settings.Load());
                t.Finished += delegate { Application.ExitThread(); };
                t.Start();
                Application.Run();
                return;
            }

            bool created;
            using (var mutex = new Mutex(true, "Local\\LeanbackMode_SingleInstance", out created))
            {
                if (!created) return; // already running
                Application.Run(new TrayApp());
            }
        }
    }

    // ---------------------------------------------------------------- native helpers

    static class Native
    {
        [StructLayout(LayoutKind.Sequential)] public struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }
        [DllImport("user32.dll")] public static extern bool GetLastInputInfo(ref LASTINPUTINFO info);

        // How long since any keyboard, mouse or controller-as-mouse input (AntiMicroX types and clicks for the controller).
        public static TimeSpan SinceLastInput()
        {
            var li = new LASTINPUTINFO();
            li.cbSize = (uint)Marshal.SizeOf(typeof(LASTINPUTINFO));
            if (!GetLastInputInfo(ref li)) return TimeSpan.Zero;
            return TimeSpan.FromMilliseconds(unchecked((uint)Environment.TickCount - li.dwTime));
        }

        [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr value);
        [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);
        [DllImport("gdi32.dll")] public static extern bool StretchBlt(IntPtr dst, int xd, int yd, int wd, int hd, IntPtr src, int xs, int ys, int ws, int hs, uint rop);
        [DllImport("gdi32.dll")] public static extern int SetStretchBltMode(IntPtr hdc, int mode);
        [DllImport("gdi32.dll")] public static extern bool SetBrushOrgEx(IntPtr hdc, int x, int y, IntPtr prev);
        [DllImport("user32.dll")] public static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint key, byte alpha, uint flags);
        [DllImport("user32.dll")] public static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);
        [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] public static extern IntPtr SetCursor(IntPtr cursor);
        [DllImport("user32.dll")] public static extern bool SendNotifyMessage(IntPtr hwnd, uint msg, UIntPtr w, IntPtr l);
        [DllImport("dwmapi.dll")] public static extern int DwmFlush();
        [DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        [DllImport("winmm.dll")] public static extern uint timeBeginPeriod(uint p);
        [DllImport("winmm.dll")] public static extern uint timeEndPeriod(uint p);
        [DllImport("user32.dll")] static extern uint SendInput(uint n, INPUT[] inputs, int size);

        [StructLayout(LayoutKind.Sequential)] struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr extra; }
        [StructLayout(LayoutKind.Sequential)] struct INPUT { public uint type; public MOUSEINPUT mi; }

        public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        public const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOACTIVATE = 0x10, SWP_NOOWNERZORDER = 0x200;

        // A zero-distance mouse move: makes Windows re-evaluate which cursor to show, without moving it.
        public static void NudgeCursor()
        {
            try
            {
                var inp = new INPUT[1];
                inp[0].type = 0; inp[0].mi.dwFlags = 0x0001; // MOUSEEVENTF_MOVE, dx = dy = 0
                SendInput(1, inp, Marshal.SizeOf(typeof(INPUT)));
            }
            catch { }
        }

        // Wait for the next frame the compositor presents (falls back to a short sleep).
        public static void WaitFrame()
        {
            var sw = Stopwatch.StartNew();
            try { DwmFlush(); } catch { }
            if (sw.Elapsed.TotalMilliseconds < 2) Thread.Sleep(4);
        }
    }

    // ---------------------------------------------------------------- settings

    class Settings
    {
        public int TvScale = 150;
        public int NormalScale = 100;
        public int TvPointerSize = 48;
        public int NormalPointerSize = 32;
        public double SwitchOnDelaySeconds = 0.5;
        public double SwitchOffDelaySeconds = 1;
        public bool SmoothTransition = true;
        public int FadeInMs = 250;
        public int SettleMinMs = 250;
        public int SettleMaxMs = 3000;
        public int CrossfadeMs = 220;
        public int FadeOutMs = 420;
        public string Mode = "Auto"; // Auto | TV | Normal
        public string ScreenName = Screens.Main; // which screen gets bigger: Main, or a screen's name ("LG TV SSCR2")
        public bool MovePointerToScreen = true;  // bring the pointer to that screen when Leanback mode starts
        public string Hotkey = "Ctrl+Alt+T";
        public bool PauseMouseInGames = true;
        public double ResumeAfterGameSeconds = 2;
        public string GameFolders = @"C:\Games";
        public string NotGames = "";
        public string AntiMicroXPath = @"C:\Program Files\AntiMicroX\bin\antimicrox.exe";
        public bool ShowTrayIcon = true;        // the Windows Unlocked Toolkit app turns this off and shows its own icon
        public bool StayInLeanbackWhenAsleep = true; // controller went off by itself after sitting idle -> keep Leanback mode
        public double AsleepAfterIdleMinutes = 5;     // idle this long before going off = it fell asleep
        public string LayerTap = "Space";       // what a quick tap of the layer button does (keys like Ctrl+Shift+T)
        public int VolumeSpeed = 50;            // controller layer: volume percent per second at full tilt
        public string DetectControllers = "All"; // All | XInput
        public string IgnoreControllers = "";    // VID:PID;VID:PID  (hex, as shown in the log)
        public string AntiMicroXProfile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"antimicrox\profiles\controller-mouse.amgp");

        public static string Dir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LeanbackMode"); }
        }
        public static string FilePath { get { return Path.Combine(Dir, "settings.ini"); } }

        public static Settings Load()
        {
            var s = new Settings();
            if (!File.Exists(FilePath))
            {
                // First run: remember what "normal" looks like on this PC.
                DisplayScale.ScreenName = s.ScreenName;
                int cur = DisplayScale.GetCurrent();
                if (cur > 0) s.NormalScale = cur;
                if (s.TvScale <= s.NormalScale) s.TvScale = s.NormalScale + 50; // TV mode should always be bigger
                s.NormalPointerSize = PointerSize.Get();
                s.Save();
                s.ApplyToController();
                return s;
            }
            var seen = new HashSet<string>();
            foreach (var raw in File.ReadAllLines(FilePath))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";")) continue;
                int eq = line.IndexOf('=');
                if (eq < 0) continue;
                string k = line.Substring(0, eq).Trim().ToLowerInvariant(), v = line.Substring(eq + 1).Trim();
                seen.Add(k);
                int i; double d;
                switch (k)
                {
                    case "tvscale": if (int.TryParse(v, out i)) s.TvScale = i; break;
                    case "normalscale": if (int.TryParse(v, out i)) s.NormalScale = i; break;
                    case "tvpointersize": if (int.TryParse(v, out i)) s.TvPointerSize = i; break;
                    case "normalpointersize": if (int.TryParse(v, out i)) s.NormalPointerSize = i; break;
                    case "switchondelayseconds": if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) s.SwitchOnDelaySeconds = d; break;
                    case "switchoffdelayseconds": if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) s.SwitchOffDelaySeconds = d; break;
                    case "smoothtransition": s.SmoothTransition = !(v == "0" || v.Equals("false", StringComparison.OrdinalIgnoreCase) || v.Equals("off", StringComparison.OrdinalIgnoreCase)); break;
                    case "fadeinms": if (int.TryParse(v, out i)) s.FadeInMs = Math.Max(0, i); break;
                    case "settleminms": if (int.TryParse(v, out i)) s.SettleMinMs = Math.Max(0, i); break;
                    case "settlemaxms": if (int.TryParse(v, out i)) s.SettleMaxMs = Math.Max(0, i); break;
                    case "crossfadems": if (int.TryParse(v, out i)) s.CrossfadeMs = Math.Max(0, i); break;
                    case "fadeoutms": if (int.TryParse(v, out i)) s.FadeOutMs = Math.Max(0, i); break;
                    case "mode": s.Mode = v; break;
                    case "screen": s.ScreenName = v.Length > 0 ? v : Screens.Main; break;
                    case "movepointertoscreen": s.MovePointerToScreen = !(v == "0" || v.Equals("false", StringComparison.OrdinalIgnoreCase) || v.Equals("off", StringComparison.OrdinalIgnoreCase)); break;
                    case "hotkey": s.Hotkey = v; break;
                    case "pausemouseingames": s.PauseMouseInGames = !(v == "0" || v.Equals("false", StringComparison.OrdinalIgnoreCase) || v.Equals("off", StringComparison.OrdinalIgnoreCase)); break;
                    case "resumeaftergameseconds": if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) s.ResumeAfterGameSeconds = Math.Max(0, d); break;
                    case "gamefolders": s.GameFolders = v; break;
                    case "notgames": s.NotGames = v; break;
                    case "showtrayicon": s.ShowTrayIcon = !(v == "0" || v.Equals("false", StringComparison.OrdinalIgnoreCase) || v.Equals("off", StringComparison.OrdinalIgnoreCase)); break;
                    case "stayinleanbackwhenasleep": s.StayInLeanbackWhenAsleep = !(v == "0" || v.Equals("false", StringComparison.OrdinalIgnoreCase) || v.Equals("off", StringComparison.OrdinalIgnoreCase)); break;
                    case "asleepafteridleminutes": if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) s.AsleepAfterIdleMinutes = Math.Max(0.5, d); break;
                    case "layertap": s.LayerTap = v; break;
                    case "volumespeed": if (int.TryParse(v, out i)) s.VolumeSpeed = Math.Max(10, Math.Min(200, i)); break;
                    case "detectcontrollers": s.DetectControllers = v.Equals("xinput", StringComparison.OrdinalIgnoreCase) ? "XInput" : "All"; break;
                    case "ignorecontrollers": s.IgnoreControllers = v; break;
                    case "antimicroxpath": if (v.Length > 0) s.AntiMicroXPath = v; break;
                    case "antimicroxprofile": if (v.Length > 0) s.AntiMicroXProfile = v; break;
                }
            }
            if (!seen.Contains("crossfadems") || !seen.Contains("pausemouseingames") || !seen.Contains("hotkey") || !seen.Contains("detectcontrollers") || !seen.Contains("stayinleanbackwhenasleep") || !seen.Contains("screen") || !seen.Contains("layertap")) { try { s.Save(); } catch { } } // add new options to older files
            s.ApplyToController();
            return s;
        }

        public void ApplyToController()
        {
            DisplayScale.ScreenName = ScreenName;
            Controller.AllTypes = DetectControllers != "XInput";
            var ignored = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var part in IgnoreControllers.Split(new[] { ';', ',', ' ' }, StringSplitOptions.RemoveEmptyEntries))
                ignored.Add(part.Trim());
            Controller.Ignored = ignored;
        }

        public void Save()
        {
            Directory.CreateDirectory(Dir);
            var sb = new StringBuilder();
            sb.AppendLine("# Leanback Mode settings. Save this file and changes apply right away.");
            sb.AppendLine("#");
            sb.AppendLine("# Which screen gets bigger in Leanback mode. Main = your main display, or a screen's name as Windows");
            sb.AppendLine("# Settings > Display shows it (log.txt lists the connected ones), e.g. Screen=LG TV SSCR2. Other screens");
            sb.AppendLine("# are never changed. If the named screen isn't connected (TV off), scaling is left alone until it is.");
            sb.AppendLine("Screen=" + ScreenName);
            sb.AppendLine("# Move the pointer onto that screen when Leanback mode starts (only if it's on another screen).");
            sb.AppendLine("MovePointerToScreen=" + (MovePointerToScreen ? "true" : "false"));
            sb.AppendLine("#");
            sb.AppendLine("# Display scaling of that screen in percent. Windows allows 100, 125, 150, 175, 200, 225 on most screens.");
            sb.AppendLine("TvScale=" + TvScale);
            sb.AppendLine("NormalScale=" + NormalScale);
            sb.AppendLine("#");
            sb.AppendLine("# Mouse pointer size: 32 is the Windows default, up to 256. (Windows also scales it with the display.)");
            sb.AppendLine("TvPointerSize=" + TvPointerSize);
            sb.AppendLine("NormalPointerSize=" + NormalPointerSize);
            sb.AppendLine("#");
            sb.AppendLine("# Which controllers count. All = Xbox-style (XInput) plus PlayStation, Switch Pro and other Bluetooth / 2.4 GHz");
            sb.AppendLine("# game controllers. XInput = only Xbox-style ones. If something that isn't a controller keeps Leanback mode on, the log");
            sb.AppendLine("# (log.txt in this folder) names it with an ID like 054C:0CE6: add that to IgnoreControllers (separate with ;).");
            sb.AppendLine("DetectControllers=" + DetectControllers);
            sb.AppendLine("IgnoreControllers=" + IgnoreControllers);
            sb.AppendLine("#");
            sb.AppendLine("# How long the controller must stay on / off before switching (avoids flicker on brief signal drops).");
            sb.AppendLine("SwitchOnDelaySeconds=" + SwitchOnDelaySeconds.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("SwitchOffDelaySeconds=" + SwitchOffDelaySeconds.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("#");
            sb.AppendLine("# Controllers switch themselves off after sitting idle for a while. Windows can't tell that apart from you");
            sb.AppendLine("# switching it off, so Leanback Mode looks at what happened just before: if nothing on the controller was");
            sb.AppendLine("# touched for AsleepAfterIdleMinutes, it fell asleep and Leanback mode stays on (until the controller next");
            sb.AppendLine("# turns on and off, or you use the shortcut). Holding the Home / Xbox button to switch it off always counts as");
            sb.AppendLine("# you switching it off (Xbox-style controllers only). StayInLeanbackWhenAsleep=false: always go back to desktop.");
            sb.AppendLine("StayInLeanbackWhenAsleep=" + (StayInLeanbackWhenAsleep ? "true" : "false"));
            sb.AppendLine("AsleepAfterIdleMinutes=" + AsleepAfterIdleMinutes.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("#");
            sb.AppendLine("# Controller layer (Xbox-style controllers). Hold LT (or the button your controller layout holds for its");
            sb.AppendLine("# second set) and tilt the right stick: up / down changes the volume, the further the faster (VolumeSpeed =");
            sb.AppendLine("# percent per second at full tilt); left / right flips through open windows, and letting go picks one.");
            sb.AppendLine("# A quick tap of the button presses LayerTap instead: a key or combo such as Space, Ctrl+Shift+T, MediaPlayPause,");
            sb.AppendLine("# BrowserBack or LeftClick (Windows Forms key names), or Run:C:\\path\\to\\app.exe to open an app.");
            sb.AppendLine("LayerTap=" + LayerTap);
            sb.AppendLine("VolumeSpeed=" + VolumeSpeed);
            sb.AppendLine("#");
            sb.AppendLine("# Transition (milliseconds). SmoothTransition=false switches instantly with no overlay.");
            sb.AppendLine("#   FadeInMs     blur in");
            sb.AppendLine("#   SettleMinMs  after switching, reveal once the screen has stopped changing (at least this long)...");
            sb.AppendLine("#   SettleMaxMs  ...but never wait longer than this");
            sb.AppendLine("#   CrossfadeMs  dissolve from the old blurred layout to the new blurred layout");
            sb.AppendLine("#   FadeOutMs    un-blur into the new layout");
            sb.AppendLine("SmoothTransition=" + (SmoothTransition ? "true" : "false"));
            sb.AppendLine("FadeInMs=" + FadeInMs);
            sb.AppendLine("SettleMinMs=" + SettleMinMs);
            sb.AppendLine("SettleMaxMs=" + SettleMaxMs);
            sb.AppendLine("CrossfadeMs=" + CrossfadeMs);
            sb.AppendLine("FadeOutMs=" + FadeOutMs);
            sb.AppendLine("#");
            sb.AppendLine("# Auto = follow the controller, TV = always Leanback mode, Normal = always desktop mode (also set from the tray menu).");
            sb.AppendLine("Mode=" + Mode);
            sb.AppendLine("#");
            sb.AppendLine("# Keyboard shortcut that switches between Leanback and desktop mode from anywhere (Ctrl/Alt/Shift/Win + a key).");
            sb.AppendLine("# In automatic mode the switch lasts until the controller next turns on (-> Leanback) or off (-> desktop).");
            sb.AppendLine("Hotkey=" + Hotkey);
            sb.AppendLine("#");
            sb.AppendLine("# Pause the controller mouse (AntiMicroX) while a game is the window in front, so the game gets the controller.");
            sb.AppendLine("# A game = anything run from Steam/Epic/Xbox/GOG/EA/Ubisoft/Riot/Rockstar game folders, a folder in GameFolders,");
            sb.AppendLine("# or a game Windows Game Bar has recognised. NotGames lists exe names to never treat as games.");
            sb.AppendLine("# Separate multiple entries with ;   e.g.  GameFolders=C:\\Games;D:\\Emulators   NotGames=someapp.exe");
            sb.AppendLine("PauseMouseInGames=" + (PauseMouseInGames ? "true" : "false"));
            sb.AppendLine("ResumeAfterGameSeconds=" + ResumeAfterGameSeconds.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("GameFolders=" + GameFolders);
            sb.AppendLine("NotGames=" + NotGames);
            sb.AppendLine("#");
            sb.AppendLine("# Show Leanback Mode's own tray icon. The Windows Unlocked Toolkit app sets this to false while it runs.");
            sb.AppendLine("ShowTrayIcon=" + (ShowTrayIcon ? "true" : "false"));
            sb.AppendLine("AntiMicroXPath=" + AntiMicroXPath);
            sb.AppendLine("AntiMicroXProfile=" + AntiMicroXProfile);
            File.WriteAllText(FilePath, sb.ToString());
        }
    }

    static class Log
    {
        static readonly object gate = new object();
        public static void Write(string msg)
        {
            try
            {
                lock (gate)
                {
                    Directory.CreateDirectory(Settings.Dir);
                    string p = Path.Combine(Settings.Dir, "log.txt");
                    if (File.Exists(p) && new FileInfo(p).Length > 200 * 1024) File.Delete(p);
                    File.AppendAllText(p, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + "  " + msg + Environment.NewLine);
                }
            }
            catch { }
        }
    }

    // ---------------------------------------------------------------- controller detection

    static class Controller
    {
        // 1) XInput: Xbox controllers and pads in "X-input" mode (most 8BitDo pads and dongles, many generic ones).
        [StructLayout(LayoutKind.Sequential)]
        public struct XINPUT_STATE { public uint Packet; public ushort Buttons; public byte LT, RT; public short LX, LY, RX, RY; }
        [DllImport("xinput1_4.dll")] static extern uint XInputGetState(uint index, out XINPUT_STATE state);
        // Undocumented export #100 also reports the Home / Xbox (guide) button. Used only to notice it being held
        // to switch the controller off; falls back to the normal call if it's missing.
        [DllImport("xinput1_4.dll", EntryPoint = "#100")] static extern uint XInputGetStateEx(uint index, out XINPUT_STATE state);
        static volatile bool exMissing;
        public const ushort GUIDE = 0x0400;

        // When a controller was last used (button, trigger or stick), and when its Home button was last pressed.
        // Lets the app tell "fell asleep after sitting idle" apart from "switched off by hand".
        public static DateTime LastActivity = DateTime.Now, LastGuide = DateTime.MinValue;
        public static bool SawXInput;   // activity above came from an XInput pad (HID pads fall back to system input time)
        static readonly XINPUT_STATE[] prev = new XINPUT_STATE[4];
        static readonly bool[] hadPrev = new bool[4];

        public static uint ReadXInput(uint i, out XINPUT_STATE s)
        {
            if (!exMissing)
            {
                try { return XInputGetStateEx(i, out s); }
                catch (EntryPointNotFoundException) { exMissing = true; }
            }
            return XInputGetState(i, out s);
        }

        static bool Moved(short a, short b) { return Math.Abs(a - b) > 2500 && (Math.Abs((int)a) > 7000 || Math.Abs((int)b) > 7000); }

        static void Track(uint i, XINPUT_STATE s)
        {
            SawXInput = true;
            if (hadPrev[i])
            {
                var p = prev[i];
                bool used = s.Buttons != p.Buttons || Math.Abs(s.LT - p.LT) > 40 || Math.Abs(s.RT - p.RT) > 40 ||
                            Moved(s.LX, p.LX) || Moved(s.LY, p.LY) || Moved(s.RX, p.RX) || Moved(s.RY, p.RY);
                if (used) LastActivity = DateTime.Now;
            }
            if ((s.Buttons & GUIDE) != 0) LastGuide = DateTime.Now;
            prev[i] = s;
            hadPrev[i] = true;
        }

        // 2) Raw Input HID devices whose usage is joystick / gamepad / multi-axis controller: PlayStation,
        //    Switch Pro, and generic Bluetooth / 2.4 GHz pads. Listing them is cheap and needs no driver.
        [StructLayout(LayoutKind.Sequential)] struct RAWINPUTDEVICELIST { public IntPtr hDevice; public uint dwType; }
        [StructLayout(LayoutKind.Explicit, Size = 32)]
        struct RID_DEVICE_INFO
        {
            [FieldOffset(0)] public uint cbSize;
            [FieldOffset(4)] public uint dwType;
            [FieldOffset(8)] public uint VendorId;
            [FieldOffset(12)] public uint ProductId;
            [FieldOffset(16)] public uint VersionNumber;
            [FieldOffset(20)] public ushort UsagePage;
            [FieldOffset(22)] public ushort Usage;
        }
        [DllImport("user32.dll", SetLastError = true)]
        static extern uint GetRawInputDeviceList([In, Out] RAWINPUTDEVICELIST[] list, ref uint count, uint size);
        [DllImport("user32.dll", SetLastError = true)]
        static extern uint GetRawInputDeviceInfo(IntPtr device, uint command, ref RID_DEVICE_INFO data, ref uint size);
        const uint RIM_TYPEHID = 2, RIDI_DEVICEINFO = 0x2000000b, FAILED = 0xFFFFFFFF;

        // Set from settings.ini: DetectControllers=All|XInput and IgnoreControllers=VID:PID;VID:PID
        public static bool AllTypes = true;
        public static HashSet<string> Ignored = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public static bool AnyConnected() { return Find() != null; }

        // A short description of the first controller that's on (for the log), or null if none is.
        public static string Find()
        {
            for (uint i = 0; i < 4; i++)
            {
                XINPUT_STATE s;
                try
                {
                    if (ReadXInput(i, out s) == 0) { Track(i, s); return "Xbox-style controller (XInput slot " + (i + 1) + ")"; }
                    hadPrev[i] = false;
                }
                catch { break; } // XInput missing: fall through to HID
            }
            return AllTypes ? FindHid() : null;
        }

        static string FindHid()
        {
            try
            {
                uint size = (uint)Marshal.SizeOf(typeof(RAWINPUTDEVICELIST));
                for (int attempt = 0; attempt < 2; attempt++)
                {
                    uint count = 0;
                    if (GetRawInputDeviceList(null, ref count, size) != 0 || count == 0) return null;
                    var list = new RAWINPUTDEVICELIST[count + 4];
                    count = (uint)list.Length;
                    uint got = GetRawInputDeviceList(list, ref count, size);
                    if (got == FAILED) continue; // a device arrived between the two calls; try again
                    for (int i = 0; i < got; i++)
                    {
                        if (list[i].dwType != RIM_TYPEHID) continue;
                        var info = new RID_DEVICE_INFO();
                        info.cbSize = 32;
                        uint infoSize = 32;
                        if (GetRawInputDeviceInfo(list[i].hDevice, RIDI_DEVICEINFO, ref info, ref infoSize) == FAILED) continue;
                        if (info.UsagePage != 0x01) continue;                                          // Generic Desktop
                        if (info.Usage != 0x04 && info.Usage != 0x05 && info.Usage != 0x08) continue;  // joystick, gamepad, multi-axis
                        string id = (info.VendorId & 0xFFFF).ToString("X4") + ":" + (info.ProductId & 0xFFFF).ToString("X4");
                        if (Ignored.Contains(id)) continue;
                        return Brand(info.VendorId & 0xFFFF) + " (" + id + ")";
                    }
                    return null;
                }
            }
            catch { }
            return null;
        }

        static string Brand(uint vendor)
        {
            switch (vendor)
            {
                case 0x054C: return "PlayStation controller";
                case 0x057E: return "Nintendo controller";
                case 0x045E: return "Xbox controller";
                case 0x2DC8: return "8BitDo controller";
                case 0x046D: return "Logitech controller";
                case 0x0F0D: return "HORI controller";
                case 0x0E6F: return "PDP controller";
                case 0x24C6: return "PowerA controller";
                case 0x28DE: return "Steam controller";
                default: return "Game controller";
            }
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

    // ---------------------------------------------------------------- display scaling

    // Uses the same (undocumented) DisplayConfig calls the Windows Settings app uses for per-display scaling.
    static class DisplayScale
    {
        [StructLayout(LayoutKind.Sequential)] struct LUID { public uint Low; public int High; }
        [StructLayout(LayoutKind.Sequential)] struct HEADER { public int type; public int size; public LUID adapterId; public uint id; }
        [StructLayout(LayoutKind.Sequential)] struct DPI_GET { public HEADER h; public int minRel; public int curRel; public int maxRel; }
        [StructLayout(LayoutKind.Sequential)] struct DPI_SET { public HEADER h; public int scaleRel; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct SOURCE_NAME { public HEADER h; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string gdiName; }

        [DllImport("user32.dll")] static extern int GetDisplayConfigBufferSizes(uint flags, out uint numPaths, out uint numModes);
        [DllImport("user32.dll")] static extern int QueryDisplayConfig(uint flags, ref uint numPaths, IntPtr paths, ref uint numModes, IntPtr modes, IntPtr topology);
        [DllImport("user32.dll")] static extern int DisplayConfigGetDeviceInfo(ref DPI_GET p);
        [DllImport("user32.dll")] static extern int DisplayConfigGetDeviceInfo(ref SOURCE_NAME p);
        [DllImport("user32.dll")] static extern int DisplayConfigSetDeviceInfo(ref DPI_SET p);

        const uint QDC_ONLY_ACTIVE_PATHS = 2;
        const int GET_SOURCE_NAME = 1, GET_DPI = -3, SET_DPI = -4;
        static readonly int[] Steps = { 100, 125, 150, 175, 200, 225, 250, 300, 350, 400, 450, 500 };

        // The screen Leanback mode changes (settings: Screen=). Set from the settings whenever they load.
        public static string ScreenName = Screens.Main;

        // Windows' device name (\\.\DISPLAYn) of that screen, or null if it isn't connected.
        public static string TargetDevice()
        {
            var t = Screens.Find(Screens.List(), ScreenName);
            if (t != null) return t.Device;
            return Screens.IsMain(ScreenName) ? Screen.PrimaryScreen.DeviceName : null;
        }

        // Where to show the transition overlay: the Leanback screen, or the main display if it isn't connected.
        public static Rectangle TargetBounds()
        {
            var list = Screens.List();
            var t = Screens.Find(list, ScreenName) ?? Screens.Find(list, Screens.Main);
            return t != null ? Rectangle.FromLTRB(t.Left, t.Top, t.Right, t.Bottom) : Screen.PrimaryScreen.Bounds;
        }

        static bool FindDevice(string device, out LUID adapter, out uint sourceId)
        {
            adapter = new LUID(); sourceId = 0;
            if (device == null) return false;
            bool anyMatch = Screens.IsMain(ScreenName); // a named screen must match exactly; never change another one
            string primary = device;
            uint np, nm;
            if (GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, out np, out nm) != 0 || np == 0) return false;
            IntPtr paths = Marshal.AllocHGlobal((int)np * 72), modes = Marshal.AllocHGlobal((int)Math.Max(nm, 1) * 64);
            try
            {
                if (QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, ref np, paths, ref nm, modes, IntPtr.Zero) != 0) return false;
                bool found = false;
                for (int i = 0; i < np; i++)
                {
                    IntPtr p = new IntPtr(paths.ToInt64() + i * 72);
                    var luid = new LUID { Low = (uint)Marshal.ReadInt32(p, 0), High = Marshal.ReadInt32(p, 4) };
                    uint sid = (uint)Marshal.ReadInt32(p, 8);
                    var sn = new SOURCE_NAME();
                    sn.h.type = GET_SOURCE_NAME; sn.h.size = Marshal.SizeOf(typeof(SOURCE_NAME)); sn.h.adapterId = luid; sn.h.id = sid;
                    DisplayConfigGetDeviceInfo(ref sn);
                    bool match = string.Equals(sn.gdiName, primary, StringComparison.OrdinalIgnoreCase);
                    if (match || (!found && anyMatch))
                    {
                        adapter = luid; sourceId = sid; found = true;
                        if (match) return true;
                    }
                }
                return found; // fall back to the first display if no name matches
            }
            finally { Marshal.FreeHGlobal(paths); Marshal.FreeHGlobal(modes); }
        }

        static bool Query(out LUID adapter, out uint sourceId, out DPI_GET info) { return Query(TargetDevice(), out adapter, out sourceId, out info); }

        static bool Query(string device, out LUID adapter, out uint sourceId, out DPI_GET info)
        {
            info = new DPI_GET();
            if (!FindDevice(device, out adapter, out sourceId)) return false;
            info.h.type = GET_DPI; info.h.size = Marshal.SizeOf(typeof(DPI_GET)); info.h.adapterId = adapter; info.h.id = sourceId;
            return DisplayConfigGetDeviceInfo(ref info) == 0;
        }

        static int TargetRel(DPI_GET g, int percent)
        {
            int recIdx = Math.Abs(g.minRel), target = 0, bestDiff = int.MaxValue;
            for (int i = 0; i < Steps.Length; i++)
            {
                int diff = Math.Abs(Steps[i] - percent);
                if (diff < bestDiff) { bestDiff = diff; target = i; }
            }
            return Math.Max(g.minRel, Math.Min(g.maxRel, target - recIdx));
        }

        public static int GetCurrent() { return GetCurrent(TargetDevice()); }

        public static int GetCurrent(string device)
        {
            LUID a; uint id; DPI_GET g;
            if (device == null || !Query(device, out a, out id, out g)) return -1;
            int idx = Math.Abs(g.minRel) + g.curRel;
            return (idx >= 0 && idx < Steps.Length) ? Steps[idx] : -1;
        }

        public static bool WouldChange(int percent)
        {
            LUID a; uint id; DPI_GET g;
            if (!Query(out a, out id, out g)) return false;
            return TargetRel(g, percent) != g.curRel;
        }

        public static string Set(int percent) { return Set(percent, TargetDevice()); }

        // device = \\.\DISPLAYn of the screen to change.
        public static string Set(int percent, string device)
        {
            LUID a; uint id; DPI_GET g;
            if (device == null) return "the Leanback screen (" + ScreenName + ") isn't connected, so scaling was left alone";
            if (!Query(device, out a, out id, out g)) return "could not read display scaling";
            int rel = TargetRel(g, percent);
            if (rel == g.curRel) return null; // already there
            var s = new DPI_SET();
            s.h.type = SET_DPI; s.h.size = Marshal.SizeOf(typeof(DPI_SET)); s.h.adapterId = a; s.h.id = id; s.scaleRel = rel;
            int err = DisplayConfigSetDeviceInfo(ref s);
            return err == 0 ? null : "setting display scaling failed (error " + err + ")";
        }
    }

    // ---------------------------------------------------------------- pointer size

    static class PointerSize
    {
        [DllImport("user32.dll", SetLastError = true)]
        static extern bool SystemParametersInfo(uint action, uint uiParam, IntPtr pvParam, uint winIni);
        const uint SPI_SETCURSORBASESIZE = 0x2029; // same call Settings > Accessibility > Mouse pointer uses
        const uint SPIF_UPDATEINIFILE = 0x01;
        const uint WM_SETTINGCHANGE = 0x001A;

        public static int Clamp(int size) { return Math.Max(32, Math.Min(256, size)); }

        public static int Get()
        {
            using (var k = Registry.CurrentUser.OpenSubKey(@"Control Panel\Cursors"))
            {
                object v = k == null ? null : k.GetValue("CursorBaseSize");
                return v is int ? (int)v : 32;
            }
        }

        public static string Set(int size)
        {
            size = Clamp(size);
            if (Get() == size) return null;
            // Apply + save without the blocking "tell every app and wait" broadcast...
            bool ok = SystemParametersInfo(SPI_SETCURSORBASESIZE, 0, new IntPtr(size), SPIF_UPDATEINIFILE);
            int lastErr = Marshal.GetLastWin32Error();
            // ...then tell apps without waiting for them.
            try { Native.SendNotifyMessage(new IntPtr(0xFFFF), WM_SETTINGCHANGE, new UIntPtr(SPI_SETCURSORBASESIZE), IntPtr.Zero); } catch { }
            try
            {
                // Keep the slider in Settings > Accessibility > Mouse pointer in sync (1..15, steps of 16px).
                using (var k = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Accessibility"))
                    k.SetValue("CursorSize", Math.Max(1, Math.Min(15, (size - 32) / 16 + 1)), RegistryValueKind.DWord);
            }
            catch { }
            return ok ? null : "setting pointer size failed (error " + lastErr + ")";
        }
    }

    // ---------------------------------------------------------------- transition visuals

    static class Visuals
    {
        const int CaptureDivisor = 20;

        // Fast, already-softened screen capture at 1/20 size (the overlay windows are excluded from it).
        public static Bitmap CaptureSmall(Rectangle b)
        {
            int sw = Math.Max(1, b.Width / CaptureDivisor), sh = Math.Max(1, b.Height / CaptureDivisor);
            var bmp = new Bitmap(sw, sh, PixelFormat.Format32bppRgb);
            using (var g = Graphics.FromImage(bmp))
            {
                IntPtr dst = g.GetHdc();
                IntPtr src = Native.GetDC(IntPtr.Zero);
                try
                {
                    Native.SetStretchBltMode(dst, 4 /*HALFTONE*/);
                    Native.SetBrushOrgEx(dst, 0, 0, IntPtr.Zero);
                    Native.StretchBlt(dst, 0, 0, sw, sh, src, b.X, b.Y, b.Width, b.Height, 0x00CC0020 /*SRCCOPY*/ | 0x40000000 /*CAPTUREBLT*/);
                }
                finally { Native.ReleaseDC(IntPtr.Zero, src); g.ReleaseHdc(dst); }
            }
            return bmp;
        }

        // Fraction (0..1) of the screen that visibly changed between two same-size captures.
        // A rescale changes nearly everything; a playing video or blinking cursor only a small patch.
        public static double ChangedFraction(Bitmap a, Bitmap b)
        {
            if (a.Width != b.Width || a.Height != b.Height) return 1;
            var r = new Rectangle(0, 0, a.Width, a.Height);
            BitmapData da = a.LockBits(r, ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb);
            BitmapData db = b.LockBits(r, ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb);
            try
            {
                int n = da.Stride * a.Height;
                byte[] x = new byte[n], y = new byte[n];
                Marshal.Copy(da.Scan0, x, 0, n);
                Marshal.Copy(db.Scan0, y, 0, n);
                int changed = 0;
                for (int row = 0; row < a.Height; row++)
                {
                    int o = row * da.Stride;
                    for (int col = 0; col < a.Width; col++, o += 4)
                        if (Math.Abs(x[o] - y[o]) + Math.Abs(x[o + 1] - y[o + 1]) + Math.Abs(x[o + 2] - y[o + 2]) > 24) changed++;
                }
                return (double)changed / (a.Width * a.Height);
            }
            finally { a.UnlockBits(da); b.UnlockBits(db); }
        }

        // Mean absolute difference per colour channel (0..255) between two same-size captures.
        public static double Diff(Bitmap a, Bitmap b)
        {
            if (a.Width != b.Width || a.Height != b.Height) return 255;
            var r = new Rectangle(0, 0, a.Width, a.Height);
            BitmapData da = a.LockBits(r, ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb);
            BitmapData db = b.LockBits(r, ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb);
            try
            {
                int n = da.Stride * a.Height;
                byte[] x = new byte[n], y = new byte[n];
                Marshal.Copy(da.Scan0, x, 0, n);
                Marshal.Copy(db.Scan0, y, 0, n);
                long sum = 0; int count = 0;
                for (int row = 0; row < a.Height; row++)
                {
                    int o = row * da.Stride;
                    for (int col = 0; col < a.Width; col++, o += 4)
                    {
                        sum += Math.Abs(x[o] - y[o]) + Math.Abs(x[o + 1] - y[o + 1]) + Math.Abs(x[o + 2] - y[o + 2]);
                        count += 3;
                    }
                }
                return count == 0 ? 0 : (double)sum / count;
            }
            finally { a.UnlockBits(da); b.UnlockBits(db); }
        }

        static string IconFont()
        {
            foreach (var f in FontFamily.Families)
                if (f.Name == "Segoe Fluent Icons") return f.Name;
            return "Segoe MDL2 Assets";
        }

        // Frosted, dimmed full-size version of a small capture, with a centered card. Returns where the progress bar goes.
        public static Bitmap MakeBackdrop(Bitmap small, Size full, bool tv, string reason, out Rectangle indicator)
        {
            int w = full.Width, h = full.Height;
            var bg = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
            using (var tiny = new Bitmap(Math.Max(1, small.Width / 2), Math.Max(1, small.Height / 2), PixelFormat.Format32bppRgb))
            {
                using (var g = Graphics.FromImage(tiny))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.DrawImage(small, new Rectangle(0, 0, tiny.Width, tiny.Height));
                }
                using (var g = Graphics.FromImage(bg))
                using (var ia = new ImageAttributes())
                {
                    ia.SetWrapMode(WrapMode.TileFlipXY); // no dark edges when blurring
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.DrawImage(tiny, new Rectangle(0, 0, w, h), 0, 0, tiny.Width, tiny.Height, GraphicsUnit.Pixel, ia);
                }
            }

            using (var g = Graphics.FromImage(bg))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;

                // Dim, slightly darker toward the bottom, plus a soft vignette.
                using (var dim = new LinearGradientBrush(new Rectangle(0, 0, w, h), Color.FromArgb(145, 8, 10, 16), Color.FromArgb(185, 6, 8, 12), LinearGradientMode.Vertical))
                    g.FillRectangle(dim, 0, 0, w, h);
                using (var path = new GraphicsPath())
                {
                    path.AddEllipse(-w * 0.25f, -h * 0.6f, w * 1.5f, h * 2.2f);
                    using (var vig = new PathGradientBrush(path))
                    {
                        vig.CenterColor = Color.FromArgb(0, 0, 0, 0);
                        vig.SurroundColors = new[] { Color.FromArgb(110, 0, 0, 0) };
                        vig.FocusScales = new PointF(0.55f, 0.55f);
                        g.FillRectangle(vig, 0, 0, w, h);
                    }
                }

                float s = h / 1440f, cx = w / 2f, cy = h / 2f;
                string glyph = tv ? "\uE7FC" : "\uE7F4"; // game controller / monitor
                string title = tv ? "Leanback mode" : "Desktop mode";
                string sub = reason == "manual" ? "Switched from the tray menu"
                           : reason == "preview" ? "Preview"
                           : reason == "controller" ? (tv ? "Controller connected" : "Controller turned off")
                           : reason; // any other text is shown as-is (e.g. "Switched with Ctrl+Alt+T")

                using (var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                using (var iconFont = new Font(IconFont(), 88 * s, FontStyle.Regular, GraphicsUnit.Pixel))
                using (var titleFont = new Font("Segoe UI Semibold", 54 * s, FontStyle.Regular, GraphicsUnit.Pixel))
                using (var subFont = new Font("Segoe UI", 26 * s, FontStyle.Regular, GraphicsUnit.Pixel))
                using (var shadow = new SolidBrush(Color.FromArgb(90, 0, 0, 0)))
                using (var white = new SolidBrush(Color.FromArgb(245, 255, 255, 255)))
                using (var soft = new SolidBrush(Color.FromArgb(185, 255, 255, 255)))
                {
                    float off = 2 * s;
                    var iconRect = new RectangleF(cx - 150 * s, cy - 150 * s, 300 * s, 116 * s);
                    var titleRect = new RectangleF(cx - 400 * s, cy - 26 * s, 800 * s, 74 * s);
                    var subRect = new RectangleF(cx - 400 * s, cy + 46 * s, 800 * s, 40 * s);
                    foreach (var item in new[] { new { r = iconRect, f = iconFont, t = glyph, b = (Brush)white },
                                                 new { r = titleRect, f = titleFont, t = title, b = (Brush)white },
                                                 new { r = subRect, f = subFont, t = sub, b = (Brush)soft } })
                    {
                        var rs = item.r; rs.Offset(0, off);
                        g.DrawString(item.t, item.f, shadow, rs, fmt);
                        g.DrawString(item.t, item.f, item.b, item.r, fmt);
                    }
                }
                indicator = Rectangle.Round(new RectangleF(cx - 120 * s, cy + 118 * s, 240 * s, Math.Max(4, 5 * s)));
            }
            return bg;
        }

        // Slim progress bar: an indeterminate sweep while switching, full once done. phase 0..1 loops.
        public static void DrawIndicator(Graphics g, Rectangle r, double phase, bool complete)
        {
            if (r.Width <= 0) return;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.CompositingMode = CompositingMode.SourceOver;
            using (var track = Pill(r))
            {
                if (complete)
                {
                    using (var full = new SolidBrush(Color.FromArgb(230, 255, 255, 255))) g.FillPath(full, track);
                    return;
                }
                using (var trackBrush = new SolidBrush(Color.FromArgb(55, 255, 255, 255))) g.FillPath(trackBrush, track);
                double e = phase < 0.5 ? 4 * phase * phase * phase : 1 - Math.Pow(-2 * phase + 2, 3) / 2;
                float segW = r.Width * 0.32f;
                float x = (float)(r.X - segW + (r.Width + segW) * e);
                var old = g.Clip;
                using (var clip = new Region(track))
                {
                    g.SetClip(clip, CombineMode.Intersect);
                    using (var segPath = Pill(Rectangle.Round(new RectangleF(x, r.Y, segW, r.Height))))
                    using (var segBrush = new SolidBrush(Color.FromArgb(230, 255, 255, 255)))
                        g.FillPath(segBrush, segPath);
                    g.Clip = old;
                }
            }
        }

        static GraphicsPath Pill(Rectangle r)
        {
            var p = new GraphicsPath();
            int d = Math.Max(1, r.Height);
            p.AddArc(r.X, r.Y, d, d, 90, 180);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 180);
            p.CloseFigure();
            return p;
        }
    }

    // A full-screen overlay: topmost, never activates, hides the pointer, never re-sizes on DPI changes,
    // left out of screen captures, and faded with per-window alpha (composited by DWM, no repainting).
    class Veil : Form
    {
        const int WM_DPICHANGED = 0x02E0, WM_SETCURSOR = 0x0020, WM_MOUSEACTIVATE = 0x0021, MA_NOACTIVATE = 3;
        const uint LWA_ALPHA = 0x2, WDA_EXCLUDEFROMCAPTURE = 0x11;

        readonly Bitmap bg;
        readonly Rectangle indicator;
        double phase;
        bool complete;

        public Veil(Bitmap background, Rectangle bounds, Rectangle indicatorRect)
        {
            bg = background; indicator = indicatorRect;
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            BackColor = Color.Black;
            Bounds = bounds;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.Opaque | ControlStyles.OptimizedDoubleBuffer, true);
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x80 /*TOOLWINDOW*/ | 0x08000000 /*NOACTIVATE*/ | 0x08 /*TOPMOST*/ | 0x80000 /*LAYERED*/;
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Native.SetLayeredWindowAttributes(Handle, 0, 0, LWA_ALPHA); // start invisible
            int on = 1;
            try { Native.DwmSetWindowAttribute(Handle, 3 /*DWMWA_TRANSITIONS_FORCEDISABLED*/, ref on, 4); } catch { }
            try { Native.SetWindowDisplayAffinity(Handle, WDA_EXCLUDEFROMCAPTURE); } catch { }
        }

        protected override void WndProc(ref Message m)
        {
            switch (m.Msg)
            {
                case WM_DPICHANGED: m.Result = IntPtr.Zero; return;                     // stay pixel-for-pixel full screen
                case WM_SETCURSOR: Native.SetCursor(IntPtr.Zero); m.Result = new IntPtr(1); return; // hide the pointer
                case WM_MOUSEACTIVATE: m.Result = new IntPtr(MA_NOACTIVATE); return;
            }
            base.WndProc(ref m);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.CompositingMode = CompositingMode.SourceCopy;
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.DrawImage(bg, e.ClipRectangle, e.ClipRectangle, GraphicsUnit.Pixel);
            if (e.ClipRectangle.IntersectsWith(indicator)) Visuals.DrawIndicator(g, indicator, phase, complete);
        }

        public void SetAlpha(byte a) { if (IsHandleCreated) Native.SetLayeredWindowAttributes(Handle, 0, a, LWA_ALPHA); }

        public void KeepOnTop()
        {
            if (IsHandleCreated)
                Native.SetWindowPos(Handle, Native.HWND_TOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE | Native.SWP_NOOWNERZORDER);
        }

        public void PlaceBelow(Veil other)
        {
            Native.SetWindowPos(Handle, other.Handle, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE | Native.SWP_NOOWNERZORDER);
        }

        public void TickIndicator(double p)
        {
            phase = p;
            var r = indicator; r.Inflate(4, 4);
            Invalidate(r);
        }

        public void MarkComplete()
        {
            complete = true;
            var r = indicator; r.Inflate(4, 4);
            Invalidate(r);
            Update();
        }
    }

    // Blur in -> switch on a background thread -> wait until the screen is still ->
    // dissolve to a blurred snapshot of the new layout -> un-blur into it.
    class Transition
    {
        public delegate string Work();
        public event EventHandler Finished;
        public string Error;

        readonly bool tv; readonly string reason; readonly Work work; readonly Settings cfg;
        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        readonly Stopwatch loopClock = new Stopwatch(), total = new Stopwatch();
        Rectangle bounds, indicator;
        Veil veil1, veil2;
        Bitmap bg1, bg2;
        int topTick;

        public Transition(bool toTv, string why, Work w, Settings settings)
        {
            tv = toTv; reason = why; work = w; cfg = settings;
            timer.Interval = 10;
            timer.Tick += delegate
            {
                veil1.TickIndicator((loopClock.Elapsed.TotalMilliseconds % 1500) / 1500.0);
                if (++topTick % 8 == 0) veil1.KeepOnTop(); // Windows may re-raise the taskbar while rescaling
            };
        }

        static double OutCubic(double p) { return 1 - Math.Pow(1 - p, 3); }
        static double InOutSine(double p) { return -(Math.Cos(Math.PI * p) - 1) / 2; }
        static double InOutCubic(double p) { return p < 0.5 ? 4 * p * p * p : 1 - Math.Pow(-2 * p + 2, 3) / 2; }

        // Vsync-paced alpha animation (blocks the UI thread for its duration; DWM keeps showing the window).
        static void Fade(Veil v, int from, int to, int ms, Func<double, double> ease)
        {
            var sw = Stopwatch.StartNew();
            while (true)
            {
                double p = ms <= 0 ? 1 : Math.Min(1, sw.Elapsed.TotalMilliseconds / ms);
                v.SetAlpha((byte)Math.Round(from + (to - from) * ease(p)));
                if (p >= 1) break;
                Native.WaitFrame();
            }
        }

        public void Start()
        {
            total.Start();
            Native.timeBeginPeriod(1);
            bounds = DisplayScale.TargetBounds();
            var sw = Stopwatch.StartNew();
            using (var small = Visuals.CaptureSmall(bounds))
                bg1 = Visuals.MakeBackdrop(small, bounds.Size, tv, reason, out indicator);
            long prepMs = sw.ElapsedMilliseconds;

            veil1 = new Veil(bg1, bounds, indicator);
            veil1.Show();
            veil1.Refresh();
            veil1.KeepOnTop();
            Native.NudgeCursor();
            Fade(veil1, 0, 255, cfg.FadeInMs, OutCubic);
            Log.Write("transition: prep " + prepMs + " ms, fade-in done at " + total.ElapsedMilliseconds + " ms");

            loopClock.Start();
            timer.Start();
            var th = new Thread(WorkerMain);
            th.IsBackground = true;
            th.Start();
        }

        void WorkerMain()
        {
            var sw = Stopwatch.StartNew();
            string err = null;
            try { err = work(); } catch (Exception ex) { err = ex.Message; }
            long workMs = sw.ElapsedMilliseconds;

            // Wait until the screen (minus our overlay) stops changing, so the reveal shows a finished layout.
            sw.Restart();
            Bitmap prev = null;
            int stableRuns = 0;
            try
            {
                while (true)
                {
                    Thread.Sleep(90);
                    Bitmap cur = Visuals.CaptureSmall(bounds);
                    if (prev != null)
                    {
                        stableRuns = Visuals.ChangedFraction(prev, cur) < 0.15 ? stableRuns + 1 : 0;
                        prev.Dispose();
                    }
                    prev = cur;
                    long el = sw.ElapsedMilliseconds;
                    if ((stableRuns >= 2 && el >= cfg.SettleMinMs) || el >= cfg.SettleMaxMs) break;
                }
            }
            catch (Exception ex) { Log.Write("settle check failed: " + ex.Message); }
            long settleMs = sw.ElapsedMilliseconds;

            Bitmap newBg = null;
            try
            {
                if (prev != null) { Rectangle ignore; newBg = Visuals.MakeBackdrop(prev, bounds.Size, tv, reason, out ignore); }
            }
            catch (Exception ex) { Log.Write("new backdrop failed: " + ex.Message); }
            if (prev != null) prev.Dispose();

            string timing = "switch " + workMs + " ms, settle " + settleMs + " ms (" + (stableRuns >= 2 ? "screen still" : "hit max") + ")";
            try { veil1.BeginInvoke((MethodInvoker)delegate { Reveal(newBg, err, timing); }); } catch { }
        }

        void Reveal(Bitmap newBg, string err, string timing)
        {
            Error = err;
            timer.Stop();
            veil1.MarkComplete();
            try
            {
                if (newBg != null)
                {
                    bg2 = newBg;
                    veil2 = new Veil(bg2, bounds, indicator);
                    veil2.MarkComplete();
                    veil2.Show();             // invisible (alpha 0) until placed under veil1
                    veil2.Refresh();
                    veil2.PlaceBelow(veil1);
                    veil2.SetAlpha(255);
                    Native.WaitFrame();
                    Fade(veil1, 255, 0, cfg.CrossfadeMs, InOutSine); // old blurred layout -> new blurred layout
                    CloseVeil(ref veil1, ref bg1);
                    Fade(veil2, 255, 0, cfg.FadeOutMs, InOutCubic);  // un-blur into the new layout
                    CloseVeil(ref veil2, ref bg2);
                }
                else
                {
                    Fade(veil1, 255, 0, cfg.FadeOutMs, InOutCubic);
                    CloseVeil(ref veil1, ref bg1);
                }
            }
            finally
            {
                CloseVeil(ref veil1, ref bg1);
                CloseVeil(ref veil2, ref bg2);
                Native.NudgeCursor(); // bring the pointer back
                Native.timeEndPeriod(1);
                Log.Write("transition: " + timing + ", total " + total.ElapsedMilliseconds + " ms");
                if (Finished != null) Finished(this, EventArgs.Empty);
            }
        }

        static void CloseVeil(ref Veil v, ref Bitmap b)
        {
            if (v != null) { try { v.Close(); v.Dispose(); } catch { } v = null; }
            if (b != null) { b.Dispose(); b = null; }
        }
    }

    // ---------------------------------------------------------------- game detection

    // Decides whether the window in front belongs to a game.
    class GameDetector
    {
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
        [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool QueryFullProcessImageName(IntPtr h, int flags, StringBuilder name, ref int size);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
        const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

        // Standard places launchers install games (matched anywhere in the path, any drive).
        static readonly string[] LauncherFolders = {
            @"\steamapps\common\", @"\xboxgames\", @"\epic games\", @"\gog galaxy\games\", @"\gog games\",
            @"\ea games\", @"\ubisoft game launcher\games\", @"\riot games\", @"\rockstar games\" };
        // Launchers and tools that live inside those folders but aren't games.
        static readonly string[] NotGameFolders = {
            @"\epic games\launcher\", @"\epic games\epic online services\", @"\riot games\riot client\",
            @"\rockstar games\launcher\", @"\rockstar games\social club\", @"\steamapps\common\steamworks shared\",
            @"\steamapps\common\steam controller configs\", @"\steamapps\common\wallpaper_engine\", @"\steamapps\common\steamvr\" };

        List<string> extraFolders = new List<string>();
        HashSet<string> knownGameExes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);     // full paths
        HashSet<string> knownGameNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);    // file names (fallback)
        HashSet<string> notGames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        DateTime lastRefresh = DateTime.MinValue;
        uint cachedPid; string cachedPath; bool cachedResult;

        public void Configure(Settings s)
        {
            extraFolders.Clear();
            foreach (var f in (s.GameFolders ?? "").Split(';'))
            {
                string t = f.Trim().TrimEnd('\\');
                if (t.Length > 0) extraFolders.Add(t.ToLowerInvariant() + "\\");
            }
            notGames.Clear();
            foreach (var n in (s.NotGames ?? "").Split(';'))
                if (n.Trim().Length > 0) notGames.Add(Path.GetFileName(n.Trim()));
            lastRefresh = DateTime.MinValue; cachedPid = 0;
        }

        // Games Windows Game Bar has recognised (refreshed every minute).
        void RefreshKnownGames()
        {
            if ((DateTime.Now - lastRefresh).TotalSeconds < 60) return;
            lastRefresh = DateTime.Now;
            knownGameExes.Clear(); knownGameNames.Clear();
            try
            {
                using (var root = Registry.CurrentUser.OpenSubKey(@"System\GameConfigStore\Children"))
                {
                    if (root == null) return;
                    foreach (var name in root.GetSubKeyNames())
                        using (var k = root.OpenSubKey(name))
                        {
                            var p = k == null ? null : k.GetValue("MatchedExeFullPath") as string;
                            if (string.IsNullOrEmpty(p)) continue;
                            knownGameExes.Add(p);
                            knownGameNames.Add(Path.GetFileName(p));
                        }
                }
            }
            catch { }
        }

        public bool IsGamePath(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            if (notGames.Contains(Path.GetFileName(path))) return false;
            string p = path.ToLowerInvariant();
            foreach (var n in NotGameFolders) if (p.Contains(n)) return false;
            foreach (var f in LauncherFolders) if (p.Contains(f)) return true;
            foreach (var f in extraFolders) if (p.StartsWith(f)) return true;
            RefreshKnownGames();
            return knownGameExes.Contains(path);
        }

        // Returns the game's exe name if the window in front is a game, otherwise null.
        public string GameInFront()
        {
            uint pid;
            GetWindowThreadProcessId(GetForegroundWindow(), out pid);
            if (pid == 0 || pid == (uint)Process.GetCurrentProcess().Id) return null;
            if (pid != cachedPid)
            {
                cachedPid = pid;
                cachedPath = ExePath(pid);
                if (cachedPath != null) cachedResult = IsGamePath(cachedPath);
                else
                {
                    // Can't read the path (e.g. protected by anti-cheat): fall back to the process name.
                    string name = null;
                    try { name = Process.GetProcessById((int)pid).ProcessName + ".exe"; } catch { }
                    RefreshKnownGames();
                    cachedPath = name;
                    cachedResult = name != null && knownGameNames.Contains(name) && !notGames.Contains(name);
                }
            }
            return cachedResult ? Path.GetFileName(cachedPath) : null;
        }

        static string ExePath(uint pid)
        {
            IntPtr h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (h == IntPtr.Zero) return null;
            try
            {
                var sb = new StringBuilder(1024); int size = sb.Capacity;
                return QueryFullProcessImageName(h, 0, sb, ref size) ? sb.ToString() : null;
            }
            finally { CloseHandle(h); }
        }
    }

    // Stops / restarts AntiMicroX (the controller-to-mouse mapper) around games.
    static class MouseMapper
    {
        public static bool IsRunning() { return Process.GetProcessesByName("antimicrox").Length > 0; }

        public static void Stop()
        {
            foreach (var p in Process.GetProcessesByName("antimicrox"))
            {
                try { p.Kill(); p.WaitForExit(2000); } catch { }
                finally { p.Dispose(); }
            }
        }

        public static string Start(Settings s)
        {
            try
            {
                if (IsRunning()) return null;
                var psi = new ProcessStartInfo(s.AntiMicroXPath, "--tray --profile \"" + s.AntiMicroXProfile + "\"");
                psi.WorkingDirectory = Path.GetDirectoryName(s.AntiMicroXPath);
                psi.UseShellExecute = false;
                Process.Start(psi).Dispose();
                return null;
            }
            catch (Exception ex) { return ex.Message; }
        }
    }

    // ---------------------------------------------------------------- pressing keys for the controller

    // Presses keys, key combos and mouse buttons as if they came from the keyboard and mouse.
    static class Inject
    {
        [StructLayout(LayoutKind.Sequential)] struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr extra; }
        [StructLayout(LayoutKind.Sequential)] struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr extra; }
        [StructLayout(LayoutKind.Explicit)] struct INPUTUNION { [FieldOffset(0)] public MOUSEINPUT mi; [FieldOffset(0)] public KEYBDINPUT ki; }
        [StructLayout(LayoutKind.Sequential)] struct INPUT { public uint type; public INPUTUNION u; }
        [DllImport("user32.dll")] static extern uint SendInput(uint n, INPUT[] inputs, int size);
        [DllImport("user32.dll")] static extern uint MapVirtualKey(uint code, uint mapType);

        const uint KEYUP = 0x2, EXTENDED = 0x1;

        static bool IsExtended(Keys k)
        {
            switch (k)
            {
                case Keys.Insert: case Keys.Delete: case Keys.Home: case Keys.End: case Keys.PageUp: case Keys.PageDown:
                case Keys.Left: case Keys.Right: case Keys.Up: case Keys.Down: case Keys.RMenu: case Keys.RControlKey:
                case Keys.LWin: case Keys.RWin: case Keys.Apps: case Keys.NumLock: case Keys.Divide: case Keys.PrintScreen:
                    return true;
            }
            return false;
        }

        public static void Key(Keys k, bool down)
        {
            var inp = new INPUT[1];
            inp[0].type = 1;
            inp[0].u.ki.wVk = (ushort)k;
            inp[0].u.ki.wScan = (ushort)MapVirtualKey((uint)k, 0);
            inp[0].u.ki.dwFlags = (down ? 0 : KEYUP) | (IsExtended(k) ? EXTENDED : 0);
            SendInput(1, inp, Marshal.SizeOf(typeof(INPUT)));
        }

        public static void Tap(Keys k) { Key(k, true); Key(k, false); }

        static void Mouse(uint downFlag)
        {
            var inp = new INPUT[2];
            inp[0].type = 0; inp[0].u.mi.dwFlags = downFlag;
            inp[1].type = 0; inp[1].u.mi.dwFlags = downFlag << 1; // the matching "up" flag
            SendInput(2, inp, Marshal.SizeOf(typeof(INPUT)));
        }

        // "Space", "Ctrl+Shift+T", "MediaPlayPause", "BrowserBack", "LeftClick"... Key names are Windows Forms Keys names.
        public static List<string> Parse(string combo)
        {
            var list = new List<string>();
            foreach (var raw in (combo ?? "").Split('+'))
            {
                string t = raw.Trim();
                if (t.Length == 0) continue;
                string l = t.ToLowerInvariant();
                if (l == "ctrl" || l == "control") t = "LControlKey";
                else if (l == "alt") t = "LMenu";
                else if (l == "shift") t = "LShiftKey";
                else if (l == "win" || l == "windows") t = "LWin";
                else if (l == "none" || l == "nothing") continue;
                list.Add(t);
            }
            return list;
        }

        public static void Combo(string combo)
        {
            string c = (combo ?? "").Trim();
            if (c.StartsWith("Run:", StringComparison.OrdinalIgnoreCase))
            {
                // Open an app (set from the toolkit app's "Open an app..." choice).
                try { var proc = Process.Start(new ProcessStartInfo(c.Substring(4).Trim()) { UseShellExecute = true }); if (proc != null) proc.Dispose(); }
                catch (Exception ex) { Log.Write("controller layer: couldn't open " + c.Substring(4).Trim() + ": " + ex.Message); }
                return;
            }
            var keys = new List<Keys>();
            foreach (var t in Parse(combo))
            {
                string l = t.ToLowerInvariant();
                if (l == "leftclick") { Mouse(0x0002); continue; }
                if (l == "rightclick") { Mouse(0x0008); continue; }
                if (l == "middleclick") { Mouse(0x0020); continue; }
                Keys k;
                if (Enum.TryParse(t, true, out k) && k != Keys.None) keys.Add(k & Keys.KeyCode);
                else Log.Write("controller layer: unknown key \"" + t + "\" in LayerTap");
            }
            foreach (var k in keys) Key(k, true);
            for (int i = keys.Count - 1; i >= 0; i--) Key(keys[i], false);
        }
    }

    // ---------------------------------------------------------------- the controller layer

    // Hold a button (LT unless the layout says otherwise) for a second layer of controls:
    //   right stick up / down     volume, smoothly: the further you tilt, the faster it moves
    //   right stick left / right  flip through open windows (Alt+Tab); let go of the button to pick one
    //   a quick tap                does the button's own job (LayerTap, Space for LT)
    // AntiMicroX switches its layout to set 2 while the button is held (its other buttons can do other things
    // there, set from the toolkit app); set 2 leaves the right stick alone so this can use it.
    // Only Xbox-style (XInput) controllers report the stick here, so the layer needs one of those.
    class Layer
    {
        public volatile bool Active;           // AntiMicroX running with a layout that has a layer button, no game in front
        public volatile string Button = "LT"; // LT | RT | LB | RB
        public volatile string Tap = "Space";
        public volatile int VolumeSpeed = 50;  // volume percent per second at full tilt

        const short STICK_START = 9000, STICK_REST = 7000, SWITCH_START = 16000, SWITCH_REST = 9000;
        bool held, used, altDown;
        DateTime heldSince;
        ushort buttonsAtPress;
        byte otherTriggerAtPress;
        int axis;                 // 0 = stick at rest, 1 = volume (up/down), 2 = windows (left/right)
        double steps;             // volume steps owed (each volume key press moves Windows' volume by 2)
        DateTime nextFlip;
        bool loggedError;

        public void Start()
        {
            var t = new Thread(Run);
            t.IsBackground = true;
            t.Name = "controller layer";
            t.Start();
        }

        void Run()
        {
            var clock = Stopwatch.StartNew();
            double last = clock.Elapsed.TotalSeconds;
            while (true)
            {
                double now = clock.Elapsed.TotalSeconds, dt = Math.Min(0.1, now - last);
                last = now;
                bool pad = false;
                try { pad = Step(dt); }
                catch (Exception ex)
                {
                    if (!loggedError) { Log.Write("controller layer: " + ex.Message); loggedError = true; }
                    Reset();
                }
                Thread.Sleep(Active && pad ? (held ? 10 : 25) : 250);
            }
        }

        bool IsHeld(Controller.XINPUT_STATE s)
        {
            switch (Button)
            {
                case "RT": return s.RT > (held ? 25 : 40);
                case "LB": return (s.Buttons & 0x0100) != 0;
                case "RB": return (s.Buttons & 0x0200) != 0;
                default: return s.LT > (held ? 25 : 40);
            }
        }

        ushort OwnBit { get { return (ushort)(Button == "LB" ? 0x0100 : Button == "RB" ? 0x0200 : 0); } }
        byte OtherTrigger(Controller.XINPUT_STATE s) { return Button == "LT" ? s.RT : Button == "RT" ? s.LT : Math.Max(s.LT, s.RT); }

        // Returns whether an Xbox-style controller is connected.
        bool Step(double dt)
        {
            if (!Active) { Reset(); return false; }
            Controller.XINPUT_STATE cur = new Controller.XINPUT_STATE(), s;
            bool any = false, down = false;
            for (uint i = 0; i < 4; i++)
            {
                if (Controller.ReadXInput(i, out s) != 0) continue;
                if (!any) { cur = s; any = true; }
                if (IsHeld(s)) { cur = s; down = true; break; }
            }
            if (!any) { Reset(); return false; }

            if (down && !held)
            {
                held = true; used = false; heldSince = DateTime.Now;
                buttonsAtPress = (ushort)(cur.Buttons & ~Controller.GUIDE);
                otherTriggerAtPress = OtherTrigger(cur);
                axis = 0;
            }
            else if (!down && held)
            {
                held = false;
                if (altDown) { Inject.Key(Keys.LMenu, false); altDown = false; }
                else if (!used && (DateTime.Now - heldSince).TotalMilliseconds < 400 && Tap.Length > 0) Inject.Combo(Tap);
                axis = 0;
                return true;
            }
            if (!held) return true;

            // Anything else pressed during the hold belongs to the layer, so letting go isn't a tap.
            ushort others = (ushort)(cur.Buttons & ~Controller.GUIDE & ~OwnBit);
            if ((others & ~buttonsAtPress) != 0 || OtherTrigger(cur) > otherTriggerAtPress + 60) used = true;

            int rx = cur.RX, ry = cur.RY;
            int ax = Math.Abs(rx), ay = Math.Abs(ry);
            if (axis == 0)
            {
                if (ay >= STICK_START && ay >= ax) { axis = 1; steps = 1; used = true; }      // first step right away
                else if (ax >= SWITCH_START && ax > ay) { axis = 2; Flip(rx > 0 ? 1 : -1); nextFlip = DateTime.Now.AddMilliseconds(450); used = true; }
            }
            else if (axis == 1)
            {
                if (ay < STICK_REST) axis = 0;
                else
                {
                    double t = Math.Min(1.0, (ay - STICK_START) / (32767.0 - STICK_START));
                    if (t < 0) t = 0;
                    double pctPerSec = 6 + (Math.Max(10, VolumeSpeed) - 6) * t * t;
                    steps += pctPerSec / 2.0 * dt;
                    int dir = ry > 0 ? 1 : -1;
                    int n = 0;
                    while (steps >= 1 && n < 4) { Inject.Tap(dir > 0 ? Keys.VolumeUp : Keys.VolumeDown); steps -= 1; n++; }
                }
            }
            else if (axis == 2)
            {
                if (ax < SWITCH_REST) axis = 0;
                else if (DateTime.Now >= nextFlip) { Flip(rx > 0 ? 1 : -1); nextFlip = DateTime.Now.AddMilliseconds(170); }
            }
            return true;
        }

        // Alt stays down for as long as the layer button is held, so Windows' window switcher stays open.
        void Flip(int dir)
        {
            if (!altDown) { Inject.Key(Keys.LMenu, true); altDown = true; }
            if (dir < 0) Inject.Key(Keys.LShiftKey, true);
            Inject.Tap(Keys.Tab);
            if (dir < 0) Inject.Key(Keys.LShiftKey, false);
        }

        // Leanback Mode is closing: never leave Alt held down.
        public void Shutdown()
        {
            Active = false;
            if (altDown) { try { Inject.Key(Keys.LMenu, false); } catch { } altDown = false; }
        }

        void Reset()
        {
            if (altDown) { try { Inject.Key(Keys.LMenu, false); } catch { } altDown = false; }
            held = false; axis = 0;
        }

        // Which button the AntiMicroX layout holds for set 2 (null = none). Plain text search: no XML library needed.
        public static string FindButton(string profile)
        {
            try
            {
                if (string.IsNullOrEmpty(profile) || !File.Exists(profile)) return null;
                string xml = File.ReadAllText(profile);
                var set = System.Text.RegularExpressions.Regex.Match(xml, "<set index=\"1\">(.*?)</set>", System.Text.RegularExpressions.RegexOptions.Singleline);
                if (!set.Success) return null;
                string[][] candidates =
                {
                    new[] { "LT", "<trigger index=\"5\">", "</trigger>" }, new[] { "RT", "<trigger index=\"6\">", "</trigger>" },
                    new[] { "LB", "<button index=\"10\">", "</button>" }, new[] { "RB", "<button index=\"11\">", "</button>" }
                };
                foreach (var c in candidates)
                {
                    int a = set.Groups[1].Value.IndexOf(c[1], StringComparison.Ordinal);
                    if (a < 0) continue;
                    int b = set.Groups[1].Value.IndexOf(c[2], a, StringComparison.Ordinal);
                    if (b < 0) continue;
                    if (set.Groups[1].Value.Substring(a, b - a).Contains("<setselectcondition>while-held</setselectcondition>")) return c[0];
                }
            }
            catch { }
            return null;
        }
    }

    // ---------------------------------------------------------------- global keyboard shortcut

    // Listens for a system-wide shortcut such as "Ctrl+Alt+T" (works whichever app is in front).
    class HotkeyListener : NativeWindow, IDisposable
    {
        [DllImport("user32.dll", SetLastError = true)] static extern bool RegisterHotKey(IntPtr hwnd, int id, uint mods, uint vk);
        [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hwnd, int id);
        const int WM_HOTKEY = 0x0312, Id = 1;
        const uint MOD_ALT = 1, MOD_CONTROL = 2, MOD_SHIFT = 4, MOD_WIN = 8, MOD_NOREPEAT = 0x4000;

        public event EventHandler Pressed;
        public string Current;  // the registered shortcut, or null
        public string Error;

        public HotkeyListener() { CreateHandle(new CreateParams()); }

        public bool Register(string spec)
        {
            Unregister();
            uint mods; Keys key;
            if (!Parse(spec, out mods, out key)) { Error = "couldn't read the shortcut \"" + spec + "\""; return false; }
            if (!RegisterHotKey(Handle, Id, mods | MOD_NOREPEAT, (uint)key))
            {
                Error = spec + " is already taken by another app (error " + Marshal.GetLastWin32Error() + ")";
                return false;
            }
            Current = spec; Error = null;
            return true;
        }

        public void Unregister()
        {
            if (Current != null) { UnregisterHotKey(Handle, Id); Current = null; }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY && m.WParam.ToInt32() == Id && Pressed != null) Pressed(this, EventArgs.Empty);
            base.WndProc(ref m);
        }

        static bool Parse(string spec, out uint mods, out Keys key)
        {
            mods = 0; key = Keys.None;
            if (string.IsNullOrEmpty(spec)) return false;
            foreach (var raw in spec.Split('+'))
            {
                string t = raw.Trim();
                switch (t.ToLowerInvariant())
                {
                    case "ctrl": case "control": mods |= MOD_CONTROL; break;
                    case "alt": mods |= MOD_ALT; break;
                    case "shift": mods |= MOD_SHIFT; break;
                    case "win": case "windows": mods |= MOD_WIN; break;
                    default:
                        if (t.Length == 1 && char.IsDigit(t[0])) t = "D" + t; // "1" -> Keys.D1
                        Keys k;
                        if (!Enum.TryParse(t, true, out k)) return false;
                        key = k;
                        break;
                }
            }
            return key != Keys.None && mods != 0;
        }

        public void Dispose()
        {
            Unregister();
            DestroyHandle();
        }
    }

    // ---------------------------------------------------------------- tray app

    class TrayApp : ApplicationContext
    {
        public const string ExitEventName = "Local\\LeanbackMode_Exit"; // set by "LeanbackMode.exe --exit"
        public const string ToggleEventName = "Local\\LeanbackMode_Toggle"; // set by the toolkit app's "Switch" button
        EventWaitHandle exitEvent, toggleEvent;
        Settings settings;
        readonly NotifyIcon tray = new NotifyIcon();
        readonly System.Windows.Forms.Timer poll = new System.Windows.Forms.Timer();
        readonly System.Windows.Forms.Timer reloadDebounce = new System.Windows.Forms.Timer();
        readonly System.Windows.Forms.Timer displayDebounce = new System.Windows.Forms.Timer();
        string activeDevice;      // the screen (\\.\DISPLAYn) Leanback mode last applied to; null = not connected
        ToolStripMenuItem screenMenu;
        readonly Control invoker = new Control();
        FileSystemWatcher watcher;
        readonly Icon iconTv, iconNormal;
        ToolStripMenuItem statusItem, autoItem, tvItem, normalItem;

        bool rawConnected, stableConnected;
        DateTime rawSince = DateTime.Now;
        bool? appliedTv; // null = nothing applied yet
        bool busy, exitRequested;
        string pendingReason;     // why the next switch happens (shown on the overlay)
        bool? overrideTv;         // set by the shortcut; cleared the next time the controller turns on/off
        bool asleepHold;          // overrideTv was set because the controller fell asleep (not by the shortcut)
        TimeSpan idleWhenLost;    // how long the controller had been idle when it disappeared
        HotkeyListener hotkey;
        ToolStripMenuItem toggleItem;

        readonly GameDetector games = new GameDetector();
        ToolStripMenuItem mouseStatusItem, pauseInGamesItem;
        bool gameRaw, mousePausedByUs;
        DateTime gameRawSince = DateTime.Now;
        string pausedFor;

        readonly Layer layer = new Layer();
        DateTime layerChecked = DateTime.MinValue, profileTime = DateTime.MinValue;
        string profileLayer;

        public TrayApp()
        {
            invoker.CreateControl();
            IntPtr ensureHandle = invoker.Handle; // a handle for BeginInvoke from other threads
            GC.KeepAlive(ensureHandle);
            settings = Settings.Load();
            games.Configure(settings);
            iconTv = MakeIcon(true);
            iconNormal = MakeIcon(false);

            var menu = new ContextMenuStrip();
            statusItem = new ToolStripMenuItem("Leanback Mode") { Enabled = false };
            toggleItem = new ToolStripMenuItem("Switch Leanback / desktop mode", null, delegate { ToggleMode("Switched from the tray menu"); });
            autoItem = new ToolStripMenuItem("Follow controller (automatic)", null, delegate { SetMode("Auto"); });
            tvItem = new ToolStripMenuItem("Always Leanback mode", null, delegate { SetMode("TV"); });
            normalItem = new ToolStripMenuItem("Always desktop mode", null, delegate { SetMode("Normal"); });
            mouseStatusItem = new ToolStripMenuItem("Controller mouse") { Enabled = false };
            pauseInGamesItem = new ToolStripMenuItem("Pause controller mouse in games", null, delegate
            {
                settings.PauseMouseInGames = !settings.PauseMouseInGames;
                try { settings.Save(); } catch (Exception ex) { Log.Write("save failed: " + ex.Message); }
                UpdateUi();
            });
            menu.Items.Add(statusItem);
            menu.Items.Add(toggleItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(autoItem);
            menu.Items.Add(tvItem);
            menu.Items.Add(normalItem);
            screenMenu = new ToolStripMenuItem("Leanback screen");
            screenMenu.DropDownItems.Add("(none)"); // filled in when the menu opens
            menu.Items.Add(screenMenu);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(mouseStatusItem);
            menu.Items.Add(pauseInGamesItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Opening += delegate { UpdateUi(); FillScreenMenu(); };
            menu.Items.Add(new ToolStripMenuItem("Preview transition", null, delegate { Preview(); }));
            menu.Items.Add(new ToolStripMenuItem("Edit settings...", null, delegate { Process.Start("notepad.exe", "\"" + Settings.FilePath + "\""); }));
            menu.Items.Add(new ToolStripMenuItem("Exit (back to desktop mode)", null, delegate { ExitApp(); }));
            tray.ContextMenuStrip = menu;

            hotkey = new HotkeyListener();
            hotkey.Pressed += delegate { ToggleMode("Switched with " + settings.Hotkey); };
            RegisterHotkey();
            tray.MouseClick += delegate(object s, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left)
                    typeof(NotifyIcon).GetMethod("ShowContextMenu", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(tray, null);
            };

            // Start from the real current state, so sign-in applies the right mode right away.
            lastFound = Controller.Find();
            rawConnected = stableConnected = lastFound != null;
            Log.Write("started; controller " + (rawConnected ? "on: " + lastFound : "off") + ", mode " + settings.Mode);
            LogScreens();
            LoadOverride();
            tray.Icon = iconNormal;
            tray.Visible = settings.ShowTrayIcon;
            Apply(true);

            poll.Interval = 250;
            poll.Tick += delegate { Tick(); };
            poll.Start();
            UpdateLayer();
            layer.Start();

            // A screen was plugged in, unplugged, switched on or off (a TV usually disappears when it's off):
            // if that changes which screen Leanback mode is for, apply the current mode to it.
            displayDebounce.Interval = 1500;
            displayDebounce.Tick += delegate
            {
                displayDebounce.Stop();
                if (busy) { displayDebounce.Start(); return; }
                string dev = DisplayScale.TargetDevice();
                if (string.Equals(dev, activeDevice, StringComparison.OrdinalIgnoreCase)) return;
                LogScreens();
                pendingReason = "Screen connected";
                Apply(true);
            };
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplayChanged;

            reloadDebounce.Interval = 400;
            reloadDebounce.Tick += delegate { reloadDebounce.Stop(); ReloadSettings(); };
            try
            {
                watcher = new FileSystemWatcher(Settings.Dir, "settings.ini");
                watcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName;
                FileSystemEventHandler fh = delegate { try { invoker.BeginInvoke((MethodInvoker)delegate { reloadDebounce.Stop(); reloadDebounce.Start(); }); } catch { } };
                watcher.Changed += fh; watcher.Created += fh;
                watcher.Renamed += delegate { fh(null, null); }; // editors that save by replacing the file
                watcher.EnableRaisingEvents = true;
            }
            catch (Exception ex) { Log.Write("settings watcher failed: " + ex.Message); }

            // Let "LeanbackMode.exe --exit" (used by uninstall.ps1) close us cleanly.
            try
            {
                exitEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ExitEventName);
                var waiter = new Thread(delegate()
                {
                    try
                    {
                        exitEvent.WaitOne();
                        invoker.BeginInvoke((MethodInvoker)delegate { Log.Write("exit requested from the command line"); ExitApp(); });
                    }
                    catch { }
                });
                waiter.IsBackground = true;
                waiter.Start();
            }
            catch (Exception ex) { Log.Write("exit listener failed: " + ex.Message); }

            // Let the toolkit app switch modes, like the shortcut does.
            try
            {
                toggleEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ToggleEventName);
                var toggler = new Thread(delegate()
                {
                    while (true)
                    {
                        try
                        {
                            toggleEvent.WaitOne();
                            invoker.BeginInvoke((MethodInvoker)delegate { ToggleMode("Switched from the toolkit"); });
                        }
                        catch { return; }
                    }
                });
                toggler.IsBackground = true;
                toggler.Start();
            }
            catch (Exception ex) { Log.Write("toggle listener failed: " + ex.Message); }
        }

        bool WantTv
        {
            get
            {
                if (string.Equals(settings.Mode, "TV", StringComparison.OrdinalIgnoreCase)) return true;
                if (string.Equals(settings.Mode, "Normal", StringComparison.OrdinalIgnoreCase)) return false;
                if (overrideTv.HasValue) return overrideTv.Value;
                return stableConnected;
            }
        }

        // Shortcut / menu toggle. In automatic mode this lasts until the controller next turns on or off
        // (turning it on always ends in TV mode, turning it off in desktop mode).
        // In "Always TV" / "Always desktop" mode it flips that setting instead.
        void ToggleMode(string reason)
        {
            bool target = !WantTv;
            if (string.Equals(settings.Mode, "TV", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(settings.Mode, "Normal", StringComparison.OrdinalIgnoreCase))
            {
                settings.Mode = target ? "TV" : "Normal";
                try { settings.Save(); } catch (Exception ex) { Log.Write("save failed: " + ex.Message); }
            }
            else { overrideTv = target; asleepHold = false; SaveOverride(); }
            Log.Write((target ? "Leanback mode" : "desktop mode") + " requested (" + reason + ")");
            pendingReason = reason;
            Apply(false);
        }

        // A shortcut switch survives restarts (sign-in, reinstall) until the controller turns on or off.
        static string StatePath { get { return Path.Combine(Settings.Dir, "state.ini"); } }

        void SaveOverride()
        {
            try
            {
                if (!overrideTv.HasValue) { if (File.Exists(StatePath)) File.Delete(StatePath); return; }
                File.WriteAllText(StatePath, "OverrideTv=" + (overrideTv.Value ? "true" : "false") + Environment.NewLine +
                                             "ControllerWas=" + (stableConnected ? "on" : "off") + Environment.NewLine +
                                             "Asleep=" + (asleepHold ? "true" : "false") + Environment.NewLine);
            }
            catch (Exception ex) { Log.Write("couldn't save shortcut state: " + ex.Message); }
        }

        void LoadOverride()
        {
            try
            {
                if (!File.Exists(StatePath)) return;
                string tv = null, was = null, asleep = null;
                foreach (var line in File.ReadAllLines(StatePath))
                {
                    int eq = line.IndexOf('=');
                    if (eq < 0) continue;
                    string k = line.Substring(0, eq).Trim(), v = line.Substring(eq + 1).Trim();
                    if (k == "OverrideTv") tv = v; else if (k == "ControllerWas") was = v; else if (k == "Asleep") asleep = v;
                }
                if (tv != null && was == (stableConnected ? "on" : "off"))
                {
                    overrideTv = tv == "true";
                    asleepHold = asleep == "true" && overrideTv.Value;
                    Log.Write("kept the " + (asleepHold ? "controller-asleep hold" : "shortcut switch") + " to " + (overrideTv.Value ? "Leanback" : "desktop") + " mode from before the restart");
                }
                else { overrideTv = null; File.Delete(StatePath); } // controller changed while we were closed
            }
            catch (Exception ex) { Log.Write("couldn't read shortcut state: " + ex.Message); }
        }

        void RegisterHotkey()
        {
            if (hotkey.Register(settings.Hotkey)) Log.Write("shortcut " + settings.Hotkey + " ready");
            else Log.Write("shortcut not available: " + hotkey.Error);
            UpdateUi();
        }

        // Pause AntiMicroX while a game is in front; bring it back shortly after you leave the game.
        void GameTick()
        {
            if (!settings.PauseMouseInGames)
            {
                if (mousePausedByUs) ResumeMouse("pausing in games turned off");
                return;
            }
            string game = null;
            try { game = games.GameInFront(); } catch { }
            bool inGame = game != null;
            if (inGame != gameRaw) { gameRaw = inGame; gameRawSince = DateTime.Now; }
            double needed = inGame ? 0.5 : settings.ResumeAfterGameSeconds;
            if ((DateTime.Now - gameRawSince).TotalSeconds < needed) return;

            if (inGame && !mousePausedByUs)
            {
                if (!MouseMapper.IsRunning()) return; // you quit it yourself: leave it off
                MouseMapper.Stop();
                mousePausedByUs = true;
                UpdateLayer();
                pausedFor = game;
                Log.Write("controller mouse paused: " + game + " is in front");
                UpdateUi();
            }
            else if (!inGame && mousePausedByUs)
            {
                ResumeMouse("left " + pausedFor);
            }
        }

        void ResumeMouse(string why)
        {
            string err = MouseMapper.Start(settings);
            mousePausedByUs = false;
            UpdateLayer();
            Log.Write("controller mouse resumed (" + why + ")" + (err != null ? "; failed to start AntiMicroX: " + err : ""));
            UpdateUi();
        }

        string lastFound;

        void Tick()
        {
            GameTick();
            if ((DateTime.Now - layerChecked).TotalSeconds >= 1.5) UpdateLayer();
            string found = Controller.Find();
            bool now = found != null;
            if (now) lastFound = found;
            if (now != rawConnected)
            {
                rawConnected = now;
                rawSince = DateTime.Now;
                if (!now) idleWhenLost = IdleBeforeLoss();
            }
            double needed = rawConnected ? settings.SwitchOnDelaySeconds : settings.SwitchOffDelaySeconds;
            if (rawConnected != stableConnected && (DateTime.Now - rawSince).TotalSeconds >= needed)
            {
                stableConnected = rawConnected;
                overrideTv = null; // the controller takes over again after a shortcut switch
                asleepHold = false;
                if (!stableConnected && FellAsleep())
                {
                    // It went off by itself after sitting idle: stay in Leanback mode until it turns on/off again.
                    overrideTv = true;
                    asleepHold = true;
                    Log.Write("controller went off after " + Math.Round(idleWhenLost.TotalMinutes, 1) + " min idle, so it fell asleep; staying in Leanback mode");
                }
                else Log.Write(stableConnected ? "controller turned on: " + lastFound : "controller turned off");
                SaveOverride();
                pendingReason = asleepHold ? null : "controller";
                Apply(false);
            }
        }

        // The controller layer is on while AntiMicroX runs a layout with a layer button and no game is in front.
        void UpdateLayer()
        {
            layerChecked = DateTime.Now;
            try
            {
                DateTime t = File.Exists(settings.AntiMicroXProfile) ? File.GetLastWriteTimeUtc(settings.AntiMicroXProfile) : DateTime.MinValue;
                if (t != profileTime) { profileTime = t; profileLayer = Layer.FindButton(settings.AntiMicroXProfile); }
                layer.Button = profileLayer ?? "LT";
                layer.Tap = settings.LayerTap;
                layer.VolumeSpeed = settings.VolumeSpeed;
                bool active = profileLayer != null && !mousePausedByUs && MouseMapper.IsRunning();
                if (active != layer.Active)
                {
                    layer.Active = active;
                    Log.Write(active ? "controller layer on: hold " + profileLayer + " for volume and window switching" : "controller layer off");
                }
            }
            catch (Exception ex) { Log.Write("controller layer check failed: " + ex.Message); }
        }

        // Idle time just before the controller disappeared. XInput pads report their own buttons and sticks;
        // for other pads, the last keyboard/mouse input stands in (the controller mouse produces mouse input).
        TimeSpan IdleBeforeLoss()
        {
            DateTime now = DateTime.Now;
            if (Controller.SawXInput) return now - Controller.LastActivity;
            return Native.SinceLastInput();
        }

        // Only meaningful while Leanback mode is on and following the controller.
        bool FellAsleep()
        {
            if (!settings.StayInLeanbackWhenAsleep) return false;
            if (!string.Equals(settings.Mode, "Auto", StringComparison.OrdinalIgnoreCase)) return false;
            if (!(appliedTv.HasValue && appliedTv.Value)) return false;
            if ((rawSince - Controller.LastGuide).TotalSeconds < 20) return false; // Home button held to switch it off
            return idleWhenLost.TotalMinutes >= settings.AsleepAfterIdleMinutes;
        }

        void OnDisplayChanged(object sender, EventArgs e)
        {
            try { invoker.BeginInvoke((MethodInvoker)delegate { displayDebounce.Stop(); displayDebounce.Start(); }); } catch { }
        }

        void LogScreens()
        {
            var sb = new StringBuilder("screens: ");
            var list = Screens.List();
            for (int i = 0; i < list.Count; i++)
                sb.Append(i > 0 ? "; " : "").Append(list[i].Name).Append(" (").Append(list[i].Width).Append('x').Append(list[i].Height)
                  .Append(list[i].Primary ? ", main" : "").Append(')');
            string dev = DisplayScale.TargetDevice();
            sb.Append(". Leanback screen: ").Append(settings.ScreenName).Append(dev == null ? " (not connected)" : "");
            Log.Write(sb.ToString());
        }

        void FillScreenMenu()
        {
            screenMenu.DropDownItems.Clear();
            var list = Screens.List();
            var names = new List<string> { Screens.Main };
            foreach (var sc in list) names.Add(sc.Name);
            if (!Screens.IsMain(settings.ScreenName) && Screens.Find(list, settings.ScreenName) == null) names.Add(settings.ScreenName);
            foreach (var n in names)
            {
                string name = n;
                var sc = Screens.IsMain(name) ? null : Screens.Find(list, name);
                string label = Screens.IsMain(name) ? "Main display" : sc == null ? name + " (not connected)" : name + (sc.Primary ? " (main display)" : "");
                var item = new ToolStripMenuItem(label, null, delegate { ChooseScreen(name); });
                item.Checked = Screens.IsMain(name) ? Screens.IsMain(settings.ScreenName) : name.Equals(settings.ScreenName, StringComparison.OrdinalIgnoreCase);
                screenMenu.DropDownItems.Add(item);
            }
        }

        void ChooseScreen(string name)
        {
            if (string.Equals(name, settings.ScreenName, StringComparison.OrdinalIgnoreCase)) return;
            string oldDev = activeDevice;
            int oldNormal = settings.NormalScale;
            bool wasTv = appliedTv.HasValue && appliedTv.Value;
            settings.ScreenName = name;
            settings.ApplyToController();
            ScreenChosen(oldDev, oldNormal, wasTv);
            try { settings.Save(); } catch (Exception ex) { Log.Write("save failed: " + ex.Message); }
            pendingReason = "Leanback screen: " + (Screens.IsMain(name) ? "main display" : name);
            Apply(true);
        }

        // Leanback mode now belongs to another screen. Put the old one back the way it was, and take the new
        // screen's current scaling as its desktop-mode size (it hasn't been touched, so that's its normal).
        void ScreenChosen(string oldDev, int oldNormal, bool wasTv)
        {
            string dev = DisplayScale.TargetDevice();
            LogScreens();
            if (oldDev != null && wasTv && !string.Equals(oldDev, dev, StringComparison.OrdinalIgnoreCase))
            {
                string err = DisplayScale.Set(oldNormal, oldDev);
                Log.Write("previous Leanback screen back to " + oldNormal + "%" + (err != null ? "; " + err : ""));
            }
            if (dev != null && !string.Equals(oldDev, dev, StringComparison.OrdinalIgnoreCase))
            {
                int cur = DisplayScale.GetCurrent(dev);
                if (cur > 0 && cur != settings.NormalScale)
                {
                    settings.NormalScale = cur;
                    if (settings.TvScale <= cur) settings.TvScale = cur + 50; // Leanback mode should always be bigger
                    try { settings.Save(); } catch (Exception ex) { Log.Write("save failed: " + ex.Message); }
                    Log.Write("desktop size on the new Leanback screen is " + cur + "%");
                }
            }
            activeDevice = dev;
        }

        // Leanback mode just started: put the pointer on the Leanback screen if it's somewhere else.
        void BringPointer()
        {
            if (!settings.MovePointerToScreen) return;
            try
            {
                var t = Screens.Find(Screens.List(), settings.ScreenName);
                if (t == null) return;
                var p = Cursor.Position;
                if (p.X >= t.Left && p.X < t.Right && p.Y >= t.Top && p.Y < t.Bottom) return;
                Cursor.Position = new Point((t.Left + t.Right) / 2, (t.Top + t.Bottom) / 2);
                Log.Write("pointer moved to " + t.Name);
            }
            catch (Exception ex) { Log.Write("couldn't move the pointer: " + ex.Message); }
        }

        void Apply(bool force)
        {
            if (busy) return; // re-checked when the current transition finishes
            bool tv = WantTv;
            activeDevice = DisplayScale.TargetDevice();
            if (!force && appliedTv.HasValue && appliedTv.Value == tv) { UpdateUi(); return; }

            int scale = tv ? settings.TvScale : settings.NormalScale;
            int pointer = tv ? settings.TvPointerSize : settings.NormalPointerSize;
            bool needed = DisplayScale.WouldChange(scale) || PointerSize.Get() != PointerSize.Clamp(pointer);
            string reason = pendingReason ?? "controller";
            pendingReason = null;

            if (!needed) { bool entering = tv && !(appliedTv.HasValue && appliedTv.Value); appliedTv = tv; if (entering) BringPointer(); UpdateUi(); return; }

            Transition.Work work = MakeWork(scale, pointer);

            if (!settings.SmoothTransition)
            {
                Done(tv, work());
                return;
            }

            busy = true;
            try
            {
                var t = new Transition(tv, reason, work, settings);
                t.Finished += delegate { busy = false; Done(tv, t.Error); };
                t.Start();
            }
            catch (Exception ex)
            {
                // If the overlay can't be shown for some reason, still switch.
                busy = false;
                Log.Write("transition failed, switching directly: " + ex.Message);
                Done(tv, work());
            }
        }

        // Scaling and pointer size change at the same time; the slow part is Windows rescaling every app.
        static Transition.Work MakeWork(int scale, int pointer)
        {
            return delegate
            {
                string pointerErr = null;
                var pt = new Thread(delegate() { pointerErr = PointerSize.Set(pointer); });
                pt.IsBackground = true;
                pt.Start();
                var sw = Stopwatch.StartNew();
                string scaleErr = DisplayScale.Set(scale);
                long scaleMs = sw.ElapsedMilliseconds;
                pt.Join(5000);
                Log.Write("switch: scaling took " + scaleMs + " ms, scaling + pointer " + sw.ElapsedMilliseconds + " ms");
                if (scaleErr != null && pointerErr != null) return scaleErr + "; " + pointerErr;
                return scaleErr ?? pointerErr;
            };
        }

        void Done(bool tv, string err)
        {
            bool entering = tv && !(appliedTv.HasValue && appliedTv.Value);
            appliedTv = tv;
            if (entering) BringPointer();
            Log.Write((tv ? "Leanback mode" : "desktop mode") + " applied" + (err != null ? "; " + err : ""));
            UpdateUi();
            if (exitRequested) { ExitApp(); return; }
            if (WantTv != tv) Apply(false); // controller changed again mid-transition
        }

        void Preview()
        {
            if (busy) return;
            busy = true;
            bool tv = appliedTv.HasValue && appliedTv.Value;
            var t = new Transition(tv, "preview", delegate { Thread.Sleep(1200); return null; }, settings);
            t.Finished += delegate { busy = false; if (exitRequested) ExitApp(); else Apply(false); };
            t.Start();
        }

        void SetMode(string mode)
        {
            settings.Mode = mode;
            overrideTv = null;
            asleepHold = false;
            SaveOverride();
            pendingReason = "manual";
            try { settings.Save(); } catch (Exception ex) { Log.Write("save failed: " + ex.Message); }
            Apply(false);
        }

        void ReloadSettings()
        {
            try
            {
                string oldName = settings.ScreenName, oldDev = activeDevice;
                int oldNormal = settings.NormalScale;
                bool wasTv = appliedTv.HasValue && appliedTv.Value;
                settings = Settings.Load();
                if (!string.Equals(oldName, settings.ScreenName, StringComparison.OrdinalIgnoreCase)) ScreenChosen(oldDev, oldNormal, wasTv);
                games.Configure(settings);
                Log.Write("settings reloaded");
                if (!string.Equals(hotkey.Current, settings.Hotkey, StringComparison.OrdinalIgnoreCase)) RegisterHotkey();
                tray.Visible = settings.ShowTrayIcon;
                Apply(true); // re-apply current mode with the new numbers
            }
            catch (Exception ex) { Log.Write("reload failed: " + ex.Message); }
        }

        void UpdateUi()
        {
            bool tv = appliedTv.HasValue && appliedTv.Value;
            string why = !string.Equals(settings.Mode, "Auto", StringComparison.OrdinalIgnoreCase) ? "always"
                : overrideTv.HasValue ? (asleepHold ? "controller asleep" : "shortcut")
                : (stableConnected ? "controller on" : "controller off");
            string text = (tv ? "Leanback mode" : "Desktop mode") + " (" + why + ")";
            tray.Icon = tv ? iconTv : iconNormal;
            tray.Text = "Leanback Mode: " + text;
            statusItem.Text = text;
            if (toggleItem != null && hotkey != null)
                toggleItem.ShortcutKeyDisplayString = hotkey.Current ?? "(shortcut unavailable)";
            mouseStatusItem.Text = mousePausedByUs ? "Controller mouse paused (" + pausedFor + ")"
                : MouseMapper.IsRunning() ? "Controller mouse on" : "Controller mouse off (AntiMicroX not running)";
            pauseInGamesItem.Checked = settings.PauseMouseInGames;
            WriteStatus(tv, why);
            autoItem.Checked = string.Equals(settings.Mode, "Auto", StringComparison.OrdinalIgnoreCase);
            tvItem.Checked = string.Equals(settings.Mode, "TV", StringComparison.OrdinalIgnoreCase);
            normalItem.Checked = string.Equals(settings.Mode, "Normal", StringComparison.OrdinalIgnoreCase);
        }

        // status.ini: what the toolkit app shows. Only rewritten when something changed.
        string lastStatus;
        void WriteStatus(bool tv, string why)
        {
            string mouse = mousePausedByUs ? "paused" : MouseMapper.IsRunning() ? "on" : "off";
            string text = "Mode=" + (tv ? "TV" : "Desktop") + Environment.NewLine +
                          "Why=" + why + Environment.NewLine +
                          "Controller=" + (stableConnected && lastFound != null ? lastFound : "") + Environment.NewLine +
                          "LastController=" + (lastFound ?? "") + Environment.NewLine +
                          "Mouse=" + mouse + (mousePausedByUs && pausedFor != null ? " (" + pausedFor + ")" : "") + Environment.NewLine +
                          "Hotkey=" + (hotkey != null && hotkey.Current != null ? hotkey.Current : "") + Environment.NewLine +
                          "Screen=" + settings.ScreenName + Environment.NewLine +
                          "ScreenConnected=" + (activeDevice != null ? "true" : "false") + Environment.NewLine;
            if (text == lastStatus) return;
            lastStatus = text;
            try { File.WriteAllText(Path.Combine(Settings.Dir, "status.ini"), text); } catch { }
        }

        void ExitApp()
        {
            if (busy) { exitRequested = true; return; } // finish the current transition first
            poll.Stop();
            if (appliedTv.HasValue && appliedTv.Value)
            {
                exitRequested = true;
                busy = true; // block other switches while we restore
                var t = new Transition(false, "manual", MakeWork(settings.NormalScale, settings.NormalPointerSize), settings);
                t.Finished += delegate { Log.Write("exit: restored normal mode"); Quit(); };
                try { t.Start(); }
                catch { DisplayScale.Set(settings.NormalScale); PointerSize.Set(settings.NormalPointerSize); Quit(); }
                return;
            }
            Log.Write("exit");
            Quit();
        }

        void Quit()
        {
            layer.Shutdown();
            if (mousePausedByUs) ResumeMouse("Leanback Mode closing"); // never leave the controller mouse off
            try { File.Delete(Path.Combine(Settings.Dir, "status.ini")); } catch { }
            if (hotkey != null) hotkey.Dispose();
            try { Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnDisplayChanged; } catch { }
            tray.Visible = false;
            tray.Dispose();
            ExitThread();
        }

        // A small TV glyph: filled blue screen when TV mode is on, grey outline when off.
        static Icon MakeIcon(bool on)
        {
            var bmp = new Bitmap(32, 32);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                var screen = new Rectangle(2, 5, 28, 19);
                using (var path = RoundRect(screen, 4))
                {
                    if (on)
                    {
                        using (var fill = new SolidBrush(Color.FromArgb(255, 59, 130, 246))) g.FillPath(fill, path);
                        using (var pen = new Pen(Color.FromArgb(255, 30, 64, 175), 2f)) g.DrawPath(pen, path);
                    }
                    else
                    {
                        using (var pen = new Pen(Color.FromArgb(255, 150, 150, 150), 2.5f)) g.DrawPath(pen, path);
                    }
                }
                using (var stand = new Pen(on ? Color.FromArgb(255, 30, 64, 175) : Color.FromArgb(255, 150, 150, 150), 2.5f))
                {
                    g.DrawLine(stand, 16, 25, 16, 28);
                    g.DrawLine(stand, 10, 29, 22, 29);
                }
            }
            return Icon.FromHandle(bmp.GetHicon());
        }

        static GraphicsPath RoundRect(Rectangle r, int radius)
        {
            var p = new GraphicsPath();
            int d = radius * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }
}
