// Media Tab Jump  (version 7.3)
// ---------------------------------------------------------------------------
// Click the "now playing" card in the Windows volume / Quick Settings flyout
// and this jumps you to the browser tab that is playing it.
//
// How it works (no browser extension, nothing sent over the network):
//   1. It watches for left-clicks. It only looks at them; it never blocks or
//      changes a click, and it does not watch the keyboard at all.
//   2. When a click lands on the Windows flyout, it reads the media card using
//      UI Automation (the same Windows feature screen readers use) to get the
//      app name ("Microsoft Edge") and the title of what's playing.
//   3. It lists that browser's tabs the same way, picks the tab whose title
//      matches, brings the window forward and selects the tab.
//
// Several screens: by default the browser window comes forward wherever it is. With
// BringToMyScreen=true in settings.ini it moves to the screen you clicked the card on.
//
// A small log is kept at %LOCALAPPDATA%\MediaTabJump\log.txt for troubleshooting.
//
// Written for the C# compiler that ships with Windows (.NET Framework 4.x,
// C# 5), so it can be built on any Windows 10/11 PC without installing anything.
// It talks to UI Automation through its COM interface (UIAutomationCore.dll),
// which can see the newer WinUI-based parts of Windows 11 such as Quick Settings.
// ---------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace MediaTabJump
{
    // ======================================================================
    // UI Automation COM interfaces (from UIAutomationClient.idl).
    // Only the start of each interface is declared, in exact vtable order;
    // "Unused" entries hold the places of methods this program never calls.
    // ======================================================================

    [StructLayout(LayoutKind.Sequential)]
    internal struct UiaPoint
    {
        public int X;
        public int Y;
    }

    [ComImport, Guid("30cbe57d-d9d0-452a-ab13-7ac5ac4825ee"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IUIAutomation
    {
        [return: MarshalAs(UnmanagedType.Bool)]
        bool CompareElements(IUIAutomationElement el1, IUIAutomationElement el2);
        void CompareRuntimeIds_Unused();
        IUIAutomationElement GetRootElement();
        IUIAutomationElement ElementFromHandle(IntPtr hwnd);
        IUIAutomationElement ElementFromPoint(UiaPoint pt);
        IUIAutomationElement GetFocusedElement();
        void GetRootElementBuildCache_Unused();
        void ElementFromHandleBuildCache_Unused();
        void ElementFromPointBuildCache_Unused();
        void GetFocusedElementBuildCache_Unused();
        void CreateTreeWalker_Unused();
        IUIAutomationTreeWalker get_ControlViewWalker();
        IUIAutomationTreeWalker get_ContentViewWalker();
        IUIAutomationTreeWalker get_RawViewWalker();
        IUIAutomationCondition get_RawViewCondition();
        IUIAutomationCondition get_ControlViewCondition();
        IUIAutomationCondition get_ContentViewCondition();
        IUIAutomationCacheRequest CreateCacheRequest();
        IUIAutomationCondition CreateTrueCondition();
    }

    [ComImport, Guid("d22108aa-8ac5-49a5-837b-37bbb3d7591e"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IUIAutomationElement
    {
        void SetFocus();
        void GetRuntimeId_Unused();
        void FindFirst_Unused();
        void FindAll_Unused();
        void FindFirstBuildCache_Unused();
        void FindAllBuildCache_Unused();
        IUIAutomationElement BuildUpdatedCache(IUIAutomationCacheRequest cacheRequest);
        [return: MarshalAs(UnmanagedType.Struct)]
        object GetCurrentPropertyValue(int propertyId);
        void GetCurrentPropertyValueEx_Unused();
        [return: MarshalAs(UnmanagedType.Struct)]
        object GetCachedPropertyValue(int propertyId);
        void GetCachedPropertyValueEx_Unused();
        void GetCurrentPatternAs_Unused();
        void GetCachedPatternAs_Unused();
        [return: MarshalAs(UnmanagedType.IUnknown)]
        object GetCurrentPattern(int patternId);
        void GetCachedPattern_Unused();
        IUIAutomationElement GetCachedParent();
        IUIAutomationElementArray GetCachedChildren();
    }

    [ComImport, Guid("14314595-b4bc-4055-95f2-58f2e42c9855"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IUIAutomationElementArray
    {
        int get_Length();
        IUIAutomationElement GetElement(int index);
    }

    [ComImport, Guid("b32a92b5-bc25-4078-9c08-d7ee95c48e03"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IUIAutomationCacheRequest
    {
        void AddProperty(int propertyId);
        void AddPattern(int patternId);
        void Clone_Unused();
        int get_TreeScope();
        void put_TreeScope(int scope);
        IUIAutomationCondition get_TreeFilter();
        void put_TreeFilter(IUIAutomationCondition filter);
        int get_AutomationElementMode();
        void put_AutomationElementMode(int mode);
    }

    [ComImport, Guid("4042c624-389c-4afc-a630-9df854a541fc"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IUIAutomationTreeWalker
    {
        IUIAutomationElement GetParentElement(IUIAutomationElement element);
        IUIAutomationElement GetFirstChildElement(IUIAutomationElement element);
        IUIAutomationElement GetLastChildElement(IUIAutomationElement element);
        IUIAutomationElement GetNextSiblingElement(IUIAutomationElement element);
        IUIAutomationElement GetPreviousSiblingElement(IUIAutomationElement element);
        IUIAutomationElement NormalizeElement(IUIAutomationElement element);
        IUIAutomationElement GetParentElementBuildCache(IUIAutomationElement element, IUIAutomationCacheRequest cacheRequest);
        IUIAutomationElement GetFirstChildElementBuildCache(IUIAutomationElement element, IUIAutomationCacheRequest cacheRequest);
        IUIAutomationElement GetLastChildElementBuildCache(IUIAutomationElement element, IUIAutomationCacheRequest cacheRequest);
        IUIAutomationElement GetNextSiblingElementBuildCache(IUIAutomationElement element, IUIAutomationCacheRequest cacheRequest);
    }

    [ComImport, Guid("352ffba8-0973-437c-a61f-f64cafd81df9"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IUIAutomationCondition
    {
    }

    [ComImport, Guid("a8efa66a-0fda-421a-9194-38021f3578ea"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IUIAutomationSelectionItemPattern
    {
        void Select();
        void AddToSelection();
        void RemoveFromSelection();
        [return: MarshalAs(UnmanagedType.Bool)]
        bool get_CurrentIsSelected();
    }

    [ComImport, Guid("fb377fbe-8ea6-46d5-9c73-6499642d3059"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IUIAutomationInvokePattern
    {
        void Invoke();
    }

    [ComImport, Guid("828055ad-355b-4435-86d5-3b51c14a9b1b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IUIAutomationLegacyIAccessiblePattern
    {
        void Select(int flagsSelect);
        void DoDefaultAction();
    }

    // UI Automation ID numbers (from UIAutomationClient.idl).
    internal static class Uia
    {
        public const int TreeScope_Element = 1;
        public const int TreeScope_Children = 2;
        public const int TreeScope_Subtree = 7;
        public const int ModeNone = 0;

        public const int BoundingRectangleProperty = 30001;
        public const int ControlTypeProperty = 30003;
        public const int NameProperty = 30005;
        public const int AutomationIdProperty = 30011;
        public const int ClassNameProperty = 30012;

        public const int InvokePattern = 10000;
        public const int SelectionItemPattern = 10010;
        public const int LegacyIAccessiblePattern = 10018;

        public const int Button = 50000;
        public const int Calendar = 50001;
        public const int CheckBox = 50002;
        public const int ComboBox = 50003;
        public const int Edit = 50004;
        public const int Hyperlink = 50005;
        public const int Image = 50006;
        public const int ListItem = 50007;
        public const int List = 50008;
        public const int Menu = 50009;
        public const int MenuBar = 50010;
        public const int MenuItem = 50011;
        public const int ProgressBar = 50012;
        public const int RadioButton = 50013;
        public const int ScrollBar = 50014;
        public const int Slider = 50015;
        public const int Spinner = 50016;
        public const int StatusBar = 50017;
        public const int Tab = 50018;
        public const int TabItem = 50019;
        public const int Text = 50020;
        public const int ToolBar = 50021;
        public const int ToolTip = 50022;
        public const int Tree = 50023;
        public const int TreeItem = 50024;
        public const int Custom = 50025;
        public const int Group = 50026;
        public const int Thumb = 50027;
        public const int DataGrid = 50028;
        public const int DataItem = 50029;
        public const int Document = 50030;
        public const int SplitButton = 50031;
        public const int Window = 50032;
        public const int Pane = 50033;
        public const int Header = 50034;
        public const int HeaderItem = 50035;
        public const int Table = 50036;
        public const int TitleBar = 50037;
        public const int Separator = 50038;

        private static readonly string[] TypeNames =
        {
            "Button", "Calendar", "CheckBox", "ComboBox", "Edit", "Hyperlink", "Image", "ListItem", "List", "Menu",
            "MenuBar", "MenuItem", "ProgressBar", "RadioButton", "ScrollBar", "Slider", "Spinner", "StatusBar", "Tab",
            "TabItem", "Text", "ToolBar", "ToolTip", "Tree", "TreeItem", "Custom", "Group", "Thumb", "DataGrid",
            "DataItem", "Document", "SplitButton", "Window", "Pane", "Header", "HeaderItem", "Table", "TitleBar", "Separator"
        };

        public static string TypeName(int id)
        {
            int index = id - Button;
            if (index >= 0 && index < TypeNames.Length) return TypeNames[index];
            return id == 0 ? "?" : id.ToString();
        }
    }

    // ======================================================================
    // Program data
    // ======================================================================

    // A screen rectangle in physical pixels.
    internal struct Box
    {
        public double X, Y, W, H;
        public bool HasValue;

        public Box(double x, double y, double w, double h)
        {
            X = x; Y = y; W = w; H = h;
            HasValue = w > 0 && h > 0;
        }

        public bool IsEmpty { get { return !HasValue; } }
        public double Area { get { return HasValue ? W * H : 0; } }

        public bool Contains(double px, double py)
        {
            return HasValue && px >= X && px <= X + W && py >= Y && py <= Y + H;
        }

        public override string ToString()
        {
            return HasValue ? "[" + (int)X + "," + (int)Y + " " + (int)W + "x" + (int)H + "]" : "[no size]";
        }
    }

    // A lightweight copy of one element of an accessibility tree.
    internal sealed class Node
    {
        public string Name = "";
        public int Type;
        public Box Bounds;
        public string AutomationId = "";
        public string ClassName = "";
        public Node Parent;
        public List<Node> Children = new List<Node>();
    }

    internal sealed class MediaCard
    {
        public Node Root;
        public string AppName = "";
        public string BrowserProcess = "";
        public string Title = "";                          // the main title line (what's playing)
        public List<string> Texts = new List<string>();   // title first, then the other lines
    }

    internal sealed class TabInfo
    {
        public IUIAutomationElement Element;
        public string Name = "";
        public IntPtr Window;
        public int Score;
    }

    public static class Program
    {
        private const string AppName = "MediaTabJump";
        private const string Version = "7.4";

        // ---------------------------------------------------------------- Win32

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X; public int Y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MSLLHOOKSTRUCT
        {
            public POINT pt;
            public uint mouseData;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MSG
        {
            public IntPtr hwnd;
            public uint message;
            public IntPtr wParam;
            public IntPtr lParam;
            public uint time;
            public POINT pt;
        }

        private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);
        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);
        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);
        [DllImport("user32.dll")]
        private static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);
        [DllImport("user32.dll")]
        private static extern bool TranslateMessage(ref MSG lpMsg);
        [DllImport("user32.dll")]
        private static extern IntPtr DispatchMessage(ref MSG lpMsg);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);
        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        [DllImport("user32.dll")]
        private static extern IntPtr WindowFromPoint(POINT p);
        [DllImport("user32.dll")]
        private static extern IntPtr GetAncestor(IntPtr hwnd, uint gaFlags);
        [DllImport("user32.dll")]
        private static extern IntPtr GetWindow(IntPtr hwnd, uint uCmd);
        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr hwnd, StringBuilder buffer, int maxCount);
        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hwnd);
        [DllImport("user32.dll")]
        private static extern int GetWindowTextLength(IntPtr hwnd);
        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hwnd, int nCmdShow);
        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hwnd);
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")]
        private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);
        [DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
        [DllImport("user32.dll")]
        private static extern void mouse_event(uint dwFlags, int dx, int dy, uint dwData, UIntPtr dwExtraInfo);
        [DllImport("user32.dll")]
        private static extern bool SetCursorPos(int x, int y);
        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT p);
        [DllImport("user32.dll")]
        private static extern bool SetProcessDpiAwarenessContext(IntPtr value);
        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(IntPtr hwnd);
        [DllImport("user32.dll")]
        private static extern void SwitchToThisWindow(IntPtr hwnd, bool fUnknown);
        [DllImport("user32.dll")]
        private static extern bool IsWindow(IntPtr hwnd);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int maxCount);

        private const int WH_MOUSE_LL = 14;
        private const int WM_LBUTTONDOWN = 0x0201;
        private const int WM_LBUTTONUP = 0x0202;
        private const int WM_MOUSEMOVE = 0x0200;
        private const uint LLMHF_INJECTED = 0x01;
        private const uint GA_ROOT = 2;
        private const uint GW_OWNER = 4;
        private const int SW_RESTORE = 9;
        private const byte VK_MENU = 0x12;
        private const byte VK_ESCAPE = 0x1B;
        private const uint KEYEVENTF_KEYUP = 0x0002;
        private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        private const uint MOUSEEVENTF_LEFTUP = 0x0004;

        // Controls smaller than this share of the media card (play, pause, next, the seek bar...)
        // are treated as playback controls, and clicks on them are left alone.
        private const double SmallControlFraction = 0.15;

        // ---------------------------------------------------------------- state

        private static IntPtr hookHandle = IntPtr.Zero;
        private static LowLevelMouseProc hookDelegate;   // kept in a field so it is never garbage collected
        private static Mutex singleInstance;

        private static readonly object pendingLock = new object();
        private static bool hasPending;
        private static POINT pendingPoint;
        private static POINT lastDown;
        private static readonly AutoResetEvent wake = new AutoResetEvent(false);

        private static volatile IUIAutomation automation;   // shared by the click worker and the hover tracker (UI Automation is thread-safe)

        private static string logDir = "";
        private static string logPath = "";
        private static readonly object logLock = new object();
        private static DateTime lastTreeDump = DateTime.MinValue;
        private static DateTime lastUnreadableNote = DateTime.MinValue;
        private static DateTime lastOtherWindowNote = DateTime.MinValue;

        private static readonly Dictionary<uint, string> processNames = new Dictionary<uint, string>();

        // Where each browser window keeps its tabs (its "tab bar"), remembered so that later
        // clicks can read every tab in one quick request instead of searching the window again.
        private static readonly Dictionary<IntPtr, List<IUIAutomationElement>> tabBars = new Dictionary<IntPtr, List<IUIAutomationElement>>();

        // Browsers whose windows are prepared in the background.
        private static readonly string[] BrowserProcesses = { "msedge", "chrome", "brave", "firefox", "opera", "vivaldi" };

        // Processes that host the Windows flyouts.
        private static readonly string[] ShellHosts = { "ShellExperienceHost", "ShellHost" };

        // App names Windows shows at the top of the media card, mapped to the browser's process name.
        private static readonly Dictionary<string, string> ExactAppNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "Microsoft Edge", "msedge" }, { "Microsoft Edge Beta", "msedge" }, { "Microsoft Edge Dev", "msedge" }, { "Microsoft Edge Canary", "msedge" },
            { "Google Chrome", "chrome" }, { "Chrome", "chrome" }, { "Google Chrome Beta", "chrome" }, { "Google Chrome Dev", "chrome" },
            { "Google Chrome Canary", "chrome" }, { "Chromium", "chrome" },
            { "Brave", "brave" }, { "Brave Browser", "brave" },
            { "Firefox", "firefox" }, { "Mozilla Firefox", "firefox" }, { "Firefox Developer Edition", "firefox" }, { "Firefox Nightly", "firefox" },
            { "Opera", "opera" }, { "Opera GX", "opera" }, { "Opera Internet Browser", "opera" },
            { "Vivaldi", "vivaldi" }
        };

        // Looser fallback, used only if no exact app name is found.
        private static readonly string[,] LooseAppNames =
        {
            { "microsoft edge", "msedge" }, { "google chrome", "chrome" }, { "firefox", "firefox" },
            { "brave", "brave" }, { "vivaldi", "vivaldi" }, { "opera", "opera" }
        };

        // ---------------------------------------------------------------- startup

        public static void Main(string[] args)
        {
            bool createdNew;
            singleInstance = new Mutex(true, "Local\\" + AppName + "-SingleInstance", out createdNew);
            if (!createdNew) return;   // already running

            EnableDpiAwareness();

            logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppName);
            try { Directory.CreateDirectory(logDir); } catch { }
            logPath = Path.Combine(logDir, "log.txt");

            AppDomain.CurrentDomain.UnhandledException += delegate(object sender, UnhandledExceptionEventArgs e)
            {
                Log("Unexpected error: " + e.ExceptionObject);
            };

            Log("Started (version " + Version + "). Click the media card in the Windows volume flyout to jump to its tab.");

            Thread worker = new Thread(WorkerLoop);
            worker.IsBackground = true;
            worker.Name = "MediaTabJump worker";
            worker.SetApartmentState(ApartmentState.MTA);
            worker.Start();

            GlowLayer.Start();
            Thread hover = new Thread(HoverLoop);
            hover.IsBackground = true;
            hover.Name = "MediaTabJump hover";
            hover.SetApartmentState(ApartmentState.MTA);
            hover.Start();

            hookDelegate = HookCallback;
            hookHandle = SetWindowsHookEx(WH_MOUSE_LL, hookDelegate, GetModuleHandle(null), 0);
            if (hookHandle == IntPtr.Zero)
            {
                Log("Could not start watching clicks (error " + Marshal.GetLastWin32Error() + "). Exiting.");
                return;
            }

            // A message loop is required for the mouse hook to receive events.
            MSG msg;
            while (GetMessage(out msg, IntPtr.Zero, 0, 0) > 0)
            {
                TranslateMessage(ref msg);
                DispatchMessage(ref msg);
            }

            UnhookWindowsHookEx(hookHandle);
            Log("Stopped.");
        }

        private static void EnableDpiAwareness()
        {
            // Without this, click positions and on-screen element positions disagree
            // on displays scaled above 100%.
            try
            {
                if (SetProcessDpiAwarenessContext(new IntPtr(-4))) return;   // per-monitor aware v2
            }
            catch { }
            try { SetProcessDPIAware(); } catch { }
        }

        private static IUIAutomation CreateAutomation()
        {
            // CUIAutomation8 (Windows 8 and later), then the original CUIAutomation.
            string[] classIds = { "e22ad333-b25f-460c-83d0-0581107395c9", "ff48dba4-60ef-4201-aa87-54103eef594e" };
            foreach (string classId in classIds)
            {
                try
                {
                    Type type = Type.GetTypeFromCLSID(new Guid(classId));
                    IUIAutomation result = Activator.CreateInstance(type) as IUIAutomation;
                    if (result != null) return result;
                }
                catch (Exception ex)
                {
                    Log("Couldn't create UI Automation object " + classId + ": " + ex.Message);
                }
            }
            return null;
        }

        // ---------------------------------------------------------------- mouse hook

        private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            // Keep this fast: Windows drops hooks that take too long. Real work happens on the worker thread.
            try
            {
                if (nCode >= 0)
                {
                    int message = (int)wParam.ToInt64();
                    if (message == WM_MOUSEMOVE)
                    {
                        hoverWake.Set();   // let the hover glow check where the pointer is
                    }
                    else if (message == WM_LBUTTONDOWN || message == WM_LBUTTONUP)
                    {
                        MSLLHOOKSTRUCT info = (MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(MSLLHOOKSTRUCT));
                        if ((info.flags & LLMHF_INJECTED) == 0)   // ignore synthetic clicks (including our own)
                        {
                            if (message == WM_LBUTTONDOWN)
                            {
                                lastDown = info.pt;
                                hoverPress = true;
                                hoverWake.Set();
                            }
                            else if (Math.Abs(info.pt.X - lastDown.X) <= 12 && Math.Abs(info.pt.Y - lastDown.Y) <= 12)
                            {
                                lock (pendingLock)
                                {
                                    pendingPoint = info.pt;
                                    hasPending = true;
                                }
                                wake.Set();
                                hoverReleaseX = info.pt.X;
                                hoverReleaseY = info.pt.Y;
                                hoverRelease = true;
                                hoverWake.Set();
                            }
                        }
                    }
                }
            }
            catch { }
            return CallNextHookEx(hookHandle, nCode, wParam, lParam);
        }

        private static void WorkerLoop()
        {
            automation = CreateAutomation();
            if (automation == null)
            {
                Log("UI Automation isn't available, so the helper can't read the flyout.");
                return;
            }
            try { automation.GetRootElement(); } catch (Exception ex) { Log("UI Automation warm-up failed: " + ex.Message); }

            int waitMs = 3000;   // first background preparation shortly after start-up
            while (true)
            {
                if (!wake.WaitOne(waitMs))
                {
                    // No click for a while: quietly get ready for the next one.
                    waitMs = 20000;
                    try { PrepareBrowserWindows(); }
                    catch (Exception ex) { Log("Background preparation failed: " + ex.Message); }
                    continue;
                }
                POINT pt;
                bool have;
                lock (pendingLock)
                {
                    pt = pendingPoint;
                    have = hasPending;
                    hasPending = false;
                }
                if (!have) continue;
                try
                {
                    HandleClick(pt);
                }
                catch (Exception ex)
                {
                    Log("Error while handling a click: " + ex.GetType().Name + ": " + ex.Message);
                }
            }
        }

        // ---------------------------------------------------------------- the flyout

        private static void HandleClick(POINT pt)
        {
            IntPtr hwnd = WindowFromPoint(pt);
            if (hwnd == IntPtr.Zero) return;
            IntPtr root = GetAncestor(hwnd, GA_ROOT);
            if (root == IntPtr.Zero) root = hwnd;
            if (root == GlowLayer.Handle && hoverRoot != IntPtr.Zero) root = hoverRoot;   // look through the glow

            uint pid;
            GetWindowThreadProcessId(root, out pid);
            string process = ProcessName(pid);
            string windowClass = ClassOf(root);

            if (!IsFlyoutWindow(process, windowClass))
            {
                NoteOtherFlyoutLikeWindow(process, windowClass);
                return;
            }

            Stopwatch timer = Stopwatch.StartNew();
            List<Node> chain = new List<Node>();
            Node windowTree = null;
            MediaCard hit = null;

            // The panel's accessibility tree is sometimes not ready (or briefly refuses requests,
            // error 0x80040201) at the moment of the click. If nothing useful was read, try again.
            for (int attempt = 0; attempt < 3 && hit == null; attempt++)
            {
                if (attempt > 0)
                {
                    if (LargestTree(chain, windowTree) > 3) break;   // it was readable; the card just isn't there
                    Thread.Sleep(attempt * 120);
                    chain.Clear();
                    windowTree = null;
                }
                hit = FindCard(pt, root, chain, ref windowTree);
            }

            // Not a card for a browser Windows knows by name (for example a site installed as an
            // app, which Windows shows under the site's own name): use the card if its title
            // clearly matches an open browser tab.
            if (hit == null) hit = FindAnyMediaCard(pt, chain, windowTree);

            if (hit == null)
            {
                DiagnoseMiss(pt, process, windowClass, chain, windowTree);
                return;
            }
            if (IsOnPlaybackControl(hit, pt.X, pt.Y)) return;   // clicked play/pause/next/previous

            long cardMs = timer.ElapsedMilliseconds;
            string result = JumpToTab(hit);
            Log("Media card clicked: \"" + Shorten(hit.Title, 100) + "\" (" + hit.AppName + "). " + result +
                " [read card " + cardMs + " ms, total " + timer.ElapsedMilliseconds + " ms]");
        }

        // Plan A: start at the exact spot that was clicked and work outwards until we reach a
        // container that holds the browser's name and playback buttons.
        // Plan B: read the whole flyout window and look for the card there.
        private static MediaCard FindCard(POINT pt, IntPtr root, List<Node> chain, ref Node windowTree)
        {
            MediaCard hit = FindCardFromPoint(pt, chain);
            if (hit != null) return hit;
            try { windowTree = Snapshot(automation.ElementFromHandle(root)); }
            catch (Exception ex) { Log("Could not read the flyout window: " + ex.Message); }
            if (windowTree == null) return null;
            foreach (MediaCard card in FindMediaCards(windowTree))
            {
                if (card.Root.Bounds.Contains(pt.X, pt.Y)) return card;
            }
            return null;
        }

        private static int LargestTree(List<Node> chain, Node windowTree)
        {
            int most = windowTree == null ? 0 : CountNodes(windowTree);
            if (chain.Count > 0) most = Math.Max(most, CountNodes(chain[chain.Count - 1]));
            return most;
        }

        // A Windows media card whose app name isn't a known browser. Its browser is decided later,
        // by finding a tab whose title matches (BrowserProcess is left empty).
        private static MediaCard FindAnyMediaCard(POINT pt, List<Node> chain, Node windowTree)
        {
            Node widest = windowTree;
            if (chain.Count > 0 && (widest == null || CountNodes(chain[chain.Count - 1]) > CountNodes(widest))) widest = chain[chain.Count - 1];
            if (widest == null) return null;
            Node area = MediaAreaAt(widest, pt.X, pt.Y);
            if (area == null) return null;

            List<Node> all = new List<Node>();
            Flatten(area, all);
            Node appName = null;
            foreach (Node n in all)
            {
                if (n.AutomationId == "AppNameText") { appName = n; break; }
            }

            MediaCard card = new MediaCard();
            card.Root = area;
            card.AppName = appName == null ? "" : appName.Name.Trim();
            card.BrowserProcess = "";
            CollectCardTexts(area, card, area);
            if (card.Title.Length < 6) return null;   // too little to match a tab safely
            return card;
        }

        private static MediaCard FindCardFromPoint(POINT pt, List<Node> chain)
        {
            IUIAutomationElement element;
            IUIAutomationTreeWalker walker;
            IUIAutomationElement desktop;
            try
            {
                UiaPoint p = new UiaPoint();
                p.X = pt.X;
                p.Y = pt.Y;
                element = automation.ElementFromPoint(p);
                walker = automation.get_RawViewWalker();
                desktop = automation.GetRootElement();
            }
            catch (Exception ex)
            {
                Log("Couldn't look up the clicked spot: " + ex.Message);
                return null;
            }

            for (int level = 0; element != null && level < 25; level++)
            {
                try { if (automation.CompareElements(element, desktop)) break; } catch { }

                Node tree = Snapshot(element);
                if (tree == null) break;
                chain.Add(tree);

                foreach (MediaCard card in FindMediaCards(tree))
                {
                    if (card.Root.Bounds.Contains(pt.X, pt.Y)) return card;
                }

                try { element = walker.GetParentElement(element); }
                catch { element = null; }
            }
            return null;
        }

        private static bool IsFlyoutWindow(string process, string windowClass)
        {
            foreach (string host in ShellHosts)
            {
                if (string.Equals(process, host, StringComparison.OrdinalIgnoreCase)) return true;
            }
            // Some Windows builds host flyouts inside Explorer using XAML "islands".
            if (string.Equals(process, "explorer", StringComparison.OrdinalIgnoreCase))
            {
                return windowClass.IndexOf("Xaml", StringComparison.OrdinalIgnoreCase) >= 0
                    || windowClass.IndexOf("CoreWindow", StringComparison.OrdinalIgnoreCase) >= 0
                    || windowClass.IndexOf("ControlCenter", StringComparison.OrdinalIgnoreCase) >= 0
                    || windowClass.IndexOf("Flyout", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            return false;
        }

        // Troubleshooting aid: if Windows ever moves the flyout to a new process, the log will say where.
        private static void NoteOtherFlyoutLikeWindow(string process, string windowClass)
        {
            bool flyoutLike = windowClass.IndexOf("ControlCenter", StringComparison.OrdinalIgnoreCase) >= 0
                           || windowClass.IndexOf("Flyout", StringComparison.OrdinalIgnoreCase) >= 0
                           || (windowClass == "Windows.UI.Core.CoreWindow" && process.IndexOf("Host", StringComparison.OrdinalIgnoreCase) >= 0);
            if (!flyoutLike) return;
            if ((DateTime.Now - lastOtherWindowNote).TotalMinutes < 5) return;
            lastOtherWindowNote = DateTime.Now;
            Log("Note: clicked a flyout-like window that isn't being watched: process=" + process + ", class=" + windowClass);
        }

        // Reads an element and everything inside it in one round trip.
        private static Node Snapshot(IUIAutomationElement element)
        {
            return Snapshot(element, false);
        }

        private static Node Snapshot(IUIAutomationElement element, bool quiet)
        {
            if (element == null) return null;
            try
            {
                IUIAutomationCacheRequest request = automation.CreateCacheRequest();
                request.AddProperty(Uia.NameProperty);
                request.AddProperty(Uia.ControlTypeProperty);
                request.AddProperty(Uia.BoundingRectangleProperty);
                request.AddProperty(Uia.AutomationIdProperty);
                request.AddProperty(Uia.ClassNameProperty);
                request.put_TreeScope(Uia.TreeScope_Subtree);
                request.put_TreeFilter(automation.CreateTrueCondition());   // include everything (the "raw" view)
                request.put_AutomationElementMode(Uia.ModeNone);

                IUIAutomationElement cached = element.BuildUpdatedCache(request);
                int budget = 4000;
                return BuildNode(cached, null, ref budget);
            }
            catch (Exception ex)
            {
                if (!quiet) Log("Could not read part of the flyout: " + ex.Message);
                return null;
            }
        }

        private static Node BuildNode(IUIAutomationElement e, Node parent, ref int budget)
        {
            Node n = new Node();
            n.Parent = parent;
            budget--;
            try { n.Name = AsString(e.GetCachedPropertyValue(Uia.NameProperty)); } catch { }
            try { n.Type = AsInt(e.GetCachedPropertyValue(Uia.ControlTypeProperty)); } catch { }
            try { n.Bounds = AsBox(e.GetCachedPropertyValue(Uia.BoundingRectangleProperty)); } catch { }
            try { n.AutomationId = AsString(e.GetCachedPropertyValue(Uia.AutomationIdProperty)); } catch { }
            try { n.ClassName = AsString(e.GetCachedPropertyValue(Uia.ClassNameProperty)); } catch { }
            if (budget <= 0) return n;

            IUIAutomationElementArray kids = null;
            try { kids = e.GetCachedChildren(); } catch { }
            if (kids != null)
            {
                int count = 0;
                try { count = kids.get_Length(); } catch { }
                for (int i = 0; i < count && budget > 0; i++)
                {
                    IUIAutomationElement kid = null;
                    try { kid = kids.GetElement(i); } catch { }
                    if (kid != null) n.Children.Add(BuildNode(kid, n, ref budget));
                }
            }
            return n;
        }

        private static string AsString(object value)
        {
            string s = value as string;
            return s ?? "";
        }

        private static int AsInt(object value)
        {
            return value is int ? (int)value : 0;
        }

        private static Box AsBox(object value)
        {
            double[] d = value as double[];
            if (d != null && d.Length == 4) return new Box(d[0], d[1], d[2], d[3]);
            return new Box();
        }

        private static List<MediaCard> FindMediaCards(Node tree)
        {
            List<Node> all = new List<Node>();
            Flatten(tree, all);
            List<MediaCard> cards = new List<MediaCard>();

            for (int pass = 0; pass < 2 && cards.Count == 0; pass++)
            {
                foreach (Node n in all)
                {
                    string browser = pass == 0 ? ExactBrowser(n.Name) : LooseBrowser(n.Name);
                    if (browser == null) continue;

                    Node cardRoot = FindCardAround(n);
                    if (cardRoot == null) continue;

                    bool duplicate = false;
                    foreach (MediaCard existing in cards)
                    {
                        if (existing.Root == cardRoot) { duplicate = true; break; }
                    }
                    if (duplicate) continue;

                    MediaCard card = new MediaCard();
                    card.Root = cardRoot;
                    card.AppName = n.Name.Trim();
                    card.BrowserProcess = browser;
                    CollectCardTexts(cardRoot, card, cardRoot);
                    if (card.Texts.Count > 0) cards.Add(card);
                }
            }
            return cards;
        }

        private static string ExactBrowser(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            string process;
            if (ExactAppNames.TryGetValue(name.Trim(), out process)) return process;
            return null;
        }

        private static string LooseBrowser(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            string lower = name.Trim().ToLowerInvariant();
            if (lower.Length == 0 || lower.Length > 60) return null;
            for (int i = 0; i < LooseAppNames.GetLength(0); i++)
            {
                if (lower.Contains(LooseAppNames[i, 0])) return LooseAppNames[i, 1];
            }
            return null;
        }

        // Walks up from the app-name label to the smallest container that also holds
        // the playback buttons and the title: that container is the media card.
        private static Node FindCardAround(Node appNameNode)
        {
            Node current = appNameNode;
            for (int i = 0; i < 10 && current != null; i++)
            {
                int buttons = CountOfType(current, Uia.Button);
                if (buttons > 10) return null;   // we've climbed into the rest of the flyout
                if (buttons >= 2 && !current.Bounds.IsEmpty && HasOtherText(current, appNameNode.Name)) return current;
                current = current.Parent;
            }
            return null;
        }

        private static int CountOfType(Node n, int type)
        {
            int count = n.Type == type ? 1 : 0;
            foreach (Node c in n.Children) count += CountOfType(c, type);
            return count;
        }

        private static bool HasOtherText(Node n, string appName)
        {
            if (n.Type == Uia.Text && n.Name.Trim().Length >= 2 &&
                !string.Equals(n.Name.Trim(), appName.Trim(), StringComparison.OrdinalIgnoreCase))
                return true;
            foreach (Node c in n.Children)
            {
                if (HasOtherText(c, appName)) return true;
            }
            return false;
        }

        // Collects the card's lines of text (title, artist...). Text labels are preferred;
        // names of containers and buttons are used only if the card has no text labels.
        private static void CollectCardTexts(Node n, MediaCard card, Node cardRoot)
        {
            List<Node> textNodes = new List<Node>();
            List<Node> otherNodes = new List<Node>();
            GatherTexts(n, card, cardRoot, textNodes, otherNodes);
            List<Node> source = textNodes.Count > 0 ? textNodes : otherNodes;

            Node title = null;
            foreach (Node t in source)
            {
                if (t.AutomationId == "TitleText") { title = t; break; }   // how Windows 11 labels the title
            }
            if (title == null)
            {
                foreach (Node t in source)
                {
                    if (title == null || t.Name.Trim().Length > title.Name.Trim().Length) title = t;
                }
            }

            card.Texts.Clear();
            if (title != null)
            {
                card.Title = title.Name.Trim();
                card.Texts.Add(card.Title);
            }
            foreach (Node t in source)
            {
                string name = t.Name.Trim();
                if (!card.Texts.Contains(name)) card.Texts.Add(name);
            }
        }

        private static void GatherTexts(Node n, MediaCard card, Node cardRoot, List<Node> textNodes, List<Node> otherNodes)
        {
            // Skip the playback buttons entirely (including any labels inside them).
            if (n != cardRoot && IsInteractive(n.Type) && n.Bounds.Area < cardRoot.Bounds.Area * SmallControlFraction) return;
            string name = n.Name.Trim();
            if (name.Length >= 2 && !string.Equals(name, card.AppName, StringComparison.OrdinalIgnoreCase))
            {
                if (n.Type == Uia.Text) textNodes.Add(n);
                else otherNodes.Add(n);
            }
            foreach (Node c in n.Children) GatherTexts(c, card, cardRoot, textNodes, otherNodes);
        }

        // True when the click landed on one of the card's small buttons (play, pause, next...).
        // A button that is labelled with the media title or the app name is treated as part
        // of the card itself, since clicking it is exactly what we want to handle.
        private static bool IsOnPlaybackControl(MediaCard card, double x, double y)
        {
            double cardArea = card.Root.Bounds.Area;
            List<Node> all = new List<Node>();
            Flatten(card.Root, all);
            foreach (Node n in all)
            {
                if (n == card.Root || !IsInteractive(n.Type) || n.Bounds.IsEmpty) continue;
                if (n.Bounds.Area >= cardArea * SmallControlFraction || !n.Bounds.Contains(x, y)) continue;
                if (IsLabelledWithCardText(n.Name, card)) continue;
                return true;
            }
            return false;
        }

        private static bool IsLabelledWithCardText(string controlName, MediaCard card)
        {
            string name = Normalize(controlName);
            if (name.Length == 0) return false;
            if (name.Contains(Normalize(card.AppName))) return true;
            foreach (string text in card.Texts)
            {
                string t = Normalize(text);
                if (t.Length >= 4 && name.Contains(t)) return true;
            }
            return false;
        }

        private static bool IsInteractive(int t)
        {
            // List and ListItem cover the row that holds Windows 11's play/pause/next buttons.
            return t == Uia.Button || t == Uia.SplitButton || t == Uia.Slider || t == Uia.CheckBox ||
                   t == Uia.ComboBox || t == Uia.Hyperlink || t == Uia.MenuItem || t == Uia.ScrollBar ||
                   t == Uia.RadioButton || t == Uia.Thumb || t == Uia.Spinner || t == Uia.List || t == Uia.ListItem;
        }

        private static void Flatten(Node n, List<Node> into)
        {
            into.Add(n);
            foreach (Node c in n.Children) Flatten(c, into);
        }

        // ---------------------------------------------------------------- the browser

        // Finds the browser tab for this card and switches to it. Returns a short summary for the log.
        private static string JumpToTab(MediaCard card)
        {
            Stopwatch timer = Stopwatch.StartNew();
            bool knownBrowser = card.BrowserProcess.Length > 0;
            List<IntPtr> windows = new List<IntPtr>();
            if (knownBrowser) windows = FindBrowserWindows(card.BrowserProcess);
            else foreach (string process in BrowserProcesses) windows.AddRange(FindBrowserWindows(process));
            if (windows.Count == 0) return "No open " + (knownBrowser ? card.BrowserProcess : "browser") + " windows were found.";

            // Quickest case: the playing tab is already the visible tab in one of the windows,
            // so that window's title says what's playing. No need to look at tabs at all.
            string title = Normalize(card.Title);
            if (title.Length >= 6)
            {
                foreach (IntPtr window in windows)
                {
                    if (Normalize(WindowTitle(window)).Contains(title))
                    {
                        WaitForPanelToClose(400);
                        string raised = BringToFront(window);
                        return "It was already the visible tab in its window, so that window was " +
                               (raised ?? "not brought forward (Windows refused)") + " in " + timer.ElapsedMilliseconds + " ms.";
                    }
                }
            }

            List<string> candidates = new List<string>();
            foreach (string text in card.Texts)
            {
                string normalized = Normalize(text);
                if (normalized.Length >= 3 && !candidates.Contains(normalized)) candidates.Add(normalized);
            }

            List<string> seen = new List<string>();
            TabInfo best = FindBestTab(windows, candidates, seen);
            if (best == null || best.Score < 1000)
            {
                // The remembered tab bars may be out of date (tabs moved, layout changed): look again from scratch.
                foreach (IntPtr window in windows) tabBars.Remove(window);
                seen.Clear();
                best = FindBestTab(windows, candidates, seen);
            }
            long searchMs = timer.ElapsedMilliseconds;

            if (!knownBrowser && best != null && best.Score < 1000) best = null;   // only trust a clear title match
            if (best == null && !knownBrowser)
                return "\"" + Shorten(card.AppName, 60) + "\" isn't a browser this helper knows, and no open browser tab has this title, so nothing was done.";
            if (best == null)
            {
                StringBuilder names = new StringBuilder();
                for (int i = 0; i < seen.Count && i < 10; i++) names.Append(Environment.NewLine).Append("    ").Append(Shorten(seen[i], 100));
                BringToFront(windows[0]);
                return "None of the " + seen.Count + " tabs matched, so the browser was brought forward instead. Tabs seen:" + names;
            }

            string how = SwitchToTab(best);
            return "Switched to tab \"" + Shorten(best.Name, 100) + "\" (" + how + "; searched " + seen.Count + " tabs in " + searchMs +
                   " ms, switched in " + (timer.ElapsedMilliseconds - searchMs) + " ms).";
        }

        private static TabInfo FindBestTab(List<IntPtr> windows, List<string> candidates, List<string> seen)
        {
            TabInfo best = null;
            foreach (IntPtr window in windows)
            {
                foreach (TabInfo tab in GetTabs(window))
                {
                    seen.Add(tab.Name);
                    tab.Score = ScoreTab(tab.Name, candidates);
                    // Windows are listed front-to-back, so on a tie the most recently used window wins.
                    if (tab.Score > 0 && (best == null || tab.Score > best.Score)) best = tab;
                }
                // The title matches and the tab is playing sound: no need to look at other windows.
                if (best != null && best.Score >= 1050) break;
            }
            return best;
        }

        private static List<IntPtr> FindBrowserWindows(string process)
        {
            List<IntPtr> result = new List<IntPtr>();
            EnumWindowsProc callback = delegate(IntPtr h, IntPtr l)
            {
                if (IsWindowVisible(h) && GetWindowTextLength(h) > 0 && GetWindow(h, GW_OWNER) == IntPtr.Zero)
                {
                    uint pid;
                    GetWindowThreadProcessId(h, out pid);
                    if (string.Equals(ProcessName(pid), process, StringComparison.OrdinalIgnoreCase)) result.Add(h);
                }
                return true;
            };
            EnumWindows(callback, IntPtr.Zero);
            GC.KeepAlive(callback);
            return result;
        }

        private static string WindowTitle(IntPtr window)
        {
            int length = GetWindowTextLength(window);
            if (length <= 0) return "";
            StringBuilder sb = new StringBuilder(length + 2);
            GetWindowText(window, sb, sb.Capacity);
            return sb.ToString();
        }

        // Runs in the background between clicks: finds the tab bar of any browser window
        // that hasn't been seen yet, so a click only needs one quick request per window.
        private static void PrepareBrowserWindows()
        {
            List<IntPtr> known = new List<IntPtr>(tabBars.Keys);
            foreach (IntPtr window in known)
            {
                if (!IsWindow(window)) tabBars.Remove(window);
            }

            foreach (string process in BrowserProcesses)
            {
                foreach (IntPtr window in FindBrowserWindows(process))
                {
                    if (tabBars.ContainsKey(window)) continue;
                    lock (pendingLock)
                    {
                        if (hasPending) return;   // a click is waiting: handle that first
                    }
                    Stopwatch timer = Stopwatch.StartNew();
                    List<TabInfo> tabs = SearchWindowForTabs(window);
                    if (tabs.Count > 0)
                        Log("Got a " + process + " window ready in the background (" + tabs.Count + " tabs, " + timer.ElapsedMilliseconds + " ms).");
                }
            }
        }

        private static List<TabInfo> GetTabs(IntPtr window)
        {
            List<IUIAutomationElement> bars;
            if (tabBars.TryGetValue(window, out bars) && bars.Count > 0)
            {
                List<TabInfo> tabs = ReadTabBars(bars, window);
                if (tabs.Count > 0) return tabs;
            }
            return SearchWindowForTabs(window);
        }

        // Asks for the name and type of an element's children all at once.
        private static IUIAutomationCacheRequest ChildrenRequest()
        {
            IUIAutomationCacheRequest request = automation.CreateCacheRequest();
            request.AddProperty(Uia.NameProperty);
            request.AddProperty(Uia.ControlTypeProperty);
            request.put_TreeScope(Uia.TreeScope_Element | Uia.TreeScope_Children);
            return request;   // default view (controls only) and full references, so a tab can be selected later
        }

        // Reads every tab in the remembered tab bars: one request per tab bar.
        private static List<TabInfo> ReadTabBars(List<IUIAutomationElement> bars, IntPtr window)
        {
            List<TabInfo> tabs = new List<TabInfo>();
            IUIAutomationCacheRequest request = ChildrenRequest();
            foreach (IUIAutomationElement bar in bars)
            {
                IUIAutomationElementArray kids;
                try { kids = bar.BuildUpdatedCache(request).GetCachedChildren(); }
                catch { return new List<TabInfo>(); }   // the tab bar is gone: search the window again
                AddTabs(kids, tabs, window, null);
            }
            return tabs;
        }

        // Searches a whole browser window for its tab bars (skipping web page content) and remembers them.
        private static List<TabInfo> SearchWindowForTabs(IntPtr window)
        {
            List<TabInfo> tabs = new List<TabInfo>();
            List<IUIAutomationElement> bars = new List<IUIAutomationElement>();
            try
            {
                IUIAutomationElement root = automation.ElementFromHandle(window);
                int budget = 800;   // maximum number of requests
                FindTabBars(root, ChildrenRequest(), 0, ref budget, bars, tabs, window);
            }
            catch (Exception ex)
            {
                Log("Could not read a browser window: " + ex.Message);
            }
            tabBars[window] = bars;   // remembered even when empty, so the background step doesn't keep retrying
            return tabs;
        }

        private static void FindTabBars(IUIAutomationElement element, IUIAutomationCacheRequest request, int depth, ref int budget,
                                        List<IUIAutomationElement> bars, List<TabInfo> tabs, IntPtr window)
        {
            if (depth > 30 || budget <= 0) return;
            budget--;
            IUIAutomationElementArray kids;
            try { kids = element.BuildUpdatedCache(request).GetCachedChildren(); }
            catch { return; }

            List<IUIAutomationElement> containers = new List<IUIAutomationElement>();
            if (AddTabs(kids, tabs, window, containers) > 0) bars.Add(element);
            foreach (IUIAutomationElement container in containers)
            {
                FindTabBars(container, request, depth + 1, ref budget, bars, tabs, window);
            }
        }

        // Adds the tabs among an element's children. Other children that could hold
        // tabs further down are added to "containers" (when given). Returns the number of tabs.
        private static int AddTabs(IUIAutomationElementArray kids, List<TabInfo> tabs, IntPtr window, List<IUIAutomationElement> containers)
        {
            if (kids == null) return 0;
            int count = 0;
            try { count = kids.get_Length(); } catch { }
            int found = 0;
            for (int i = 0; i < count; i++)
            {
                IUIAutomationElement kid;
                int type = 0;
                string name = "";
                try
                {
                    kid = kids.GetElement(i);
                    type = AsInt(kid.GetCachedPropertyValue(Uia.ControlTypeProperty));
                    name = AsString(kid.GetCachedPropertyValue(Uia.NameProperty));
                }
                catch { continue; }

                if (type == Uia.TabItem)
                {
                    TabInfo tab = new TabInfo();
                    tab.Element = kid;
                    tab.Name = name;
                    tab.Window = window;
                    tabs.Add(tab);
                    found++;
                }
                else if (containers != null && CanContainTabs(type))
                {
                    containers.Add(kid);
                }
            }
            return found;
        }

        // Web pages (Document) and simple controls never contain the browser's own tabs.
        private static bool CanContainTabs(int type)
        {
            return type != Uia.Document && type != Uia.Button && type != Uia.SplitButton && type != Uia.MenuItem &&
                   type != Uia.Text && type != Uia.Image && type != Uia.Edit && type != Uia.Hyperlink &&
                   type != Uia.CheckBox && type != Uia.RadioButton && type != Uia.Separator && type != Uia.ScrollBar &&
                   type != Uia.Slider && type != Uia.ProgressBar && type != Uia.ToolTip && type != Uia.ComboBox &&
                   type != Uia.TabItem;
        }

        private static int ScoreTab(string tabName, List<string> candidates)
        {
            string tab = Normalize(tabName);
            if (tab.Length == 0) return 0;
            int score = 0;
            foreach (string c in candidates)
            {
                if (c.Length >= 4 && tab.Contains(c))
                    score = Math.Max(score, 1000 + c.Length * 10);          // tab title contains the media title
                else if (tab.Length >= 8 && c.Contains(tab))
                    score = Math.Max(score, 800 + tab.Length * 5);          // media title contains the tab title
                else
                {
                    int overlap = WordOverlapPercent(c, tab);
                    if (overlap >= 60) score = Math.Max(score, 400 + overlap);
                }
            }
            // Browsers add "Audio playing" (or similar) to the name of a tab making sound.
            bool audio = tab.Contains("audio");
            if (audio) score = score > 0 ? score + 50 : 100;
            return score;
        }

        private static int WordOverlapPercent(string candidate, string tab)
        {
            List<string> candidateWords = Words(candidate);
            if (candidateWords.Count < 3) return 0;
            Dictionary<string, bool> tabWords = new Dictionary<string, bool>();
            foreach (string w in Words(tab)) tabWords[w] = true;
            int hits = 0;
            foreach (string w in candidateWords)
            {
                if (tabWords.ContainsKey(w)) hits++;
            }
            return hits * 100 / candidateWords.Count;
        }

        private static List<string> Words(string s)
        {
            List<string> words = new List<string>();
            StringBuilder sb = new StringBuilder();
            foreach (char ch in s)
            {
                if (char.IsLetterOrDigit(ch)) { sb.Append(ch); continue; }
                if (sb.Length >= 3) words.Add(sb.ToString());
                sb.Length = 0;
            }
            if (sb.Length >= 3) words.Add(sb.ToString());
            return words;
        }

        private static string Normalize(string s)
        {
            if (s == null) return "";
            StringBuilder sb = new StringBuilder();
            bool pendingSpace = false;
            foreach (char raw in s)
            {
                char ch = char.ToLowerInvariant(raw);
                if (char.IsWhiteSpace(ch)) { pendingSpace = true; continue; }
                if (pendingSpace && sb.Length > 0) sb.Append(' ');
                pendingSpace = false;
                sb.Append(ch);
            }
            string result = sb.ToString();
            // Text cut off with an ellipsis on screen shouldn't stop it from matching.
            while (true)
            {
                if (result.EndsWith("\u2026")) result = result.Substring(0, result.Length - 1).TrimEnd();
                else if (result.EndsWith("...")) result = result.Substring(0, result.Length - 3).TrimEnd();
                else break;
            }
            return result;
        }

        // Switches to the tab the way a real mouse click on it would:
        //   1. let the Windows panel finish closing and make sure the browser window is the
        //      active window (Chromium only moves keyboard focus to the page on a tab switch
        //      when its window is active; otherwise focus can stay on the tab and the browser
        //      shows that tab's hover preview);
        //   2. select the tab;
        //   3. if keyboard focus still ended up on the tab, move it to the page.
        // Returns how it went, for the log.
        private static string SwitchToTab(TabInfo tab)
        {
            Stopwatch timer = Stopwatch.StartNew();
            POINT clickedAt;
            GetCursorPos(out clickedAt);   // on the flyout, so on the screen you're using
            WaitForPanelToClose(400);
            ReadGlowSettings();
            string moved = bringToMyScreen ? ScreenMover.MoveTo(tab.Window, clickedAt.X, clickedAt.Y) : null;
            if (moved != null) Log(moved);
            string raised = BringToFront(tab.Window);
            string windowNote = raised == null ? "Windows refused to bring the window forward" : "window " + raised;
            long raiseMs = timer.ElapsedMilliseconds;

            string how = SelectTab(tab);
            long selectMs = timer.ElapsedMilliseconds - raiseMs;

            string focusNote = KeepFocusOffTabs(tab.Window, 300);
            return windowNote + " in " + raiseMs + " ms, " + how + " in " + selectMs + " ms" + (focusNote.Length > 0 ? ", " + focusNote : "");
        }

        // The Windows panel closes itself after the click (and usually hands the browser the
        // focus). Wait for that, up to maxMs, so the two don't fight over which window is active.
        private static void WaitForPanelToClose(int maxMs)
        {
            Stopwatch timer = Stopwatch.StartNew();
            while (timer.ElapsedMilliseconds < maxMs)
            {
                IntPtr fg = GetForegroundWindow();
                if (fg == IntPtr.Zero || fg == GlowLayer.Handle) { Thread.Sleep(10); continue; }
                uint pid;
                GetWindowThreadProcessId(fg, out pid);
                if (!IsFlyoutWindow(ProcessName(pid), ClassOf(fg))) return;
                Thread.Sleep(10);
            }
        }

        private static string SelectTab(TabInfo tab)
        {
            // Normal way: ask the browser to select the tab, and wait (briefly) until it says it has.
            try
            {
                IUIAutomationSelectionItemPattern selection = tab.Element.GetCurrentPattern(Uia.SelectionItemPattern) as IUIAutomationSelectionItemPattern;
                if (selection != null)
                {
                    if (selection.get_CurrentIsSelected()) return "tab was already selected";
                    selection.Select();
                    Stopwatch timer = Stopwatch.StartNew();
                    while (timer.ElapsedMilliseconds < 700)
                    {
                        if (selection.get_CurrentIsSelected()) return "tab selected";
                        Thread.Sleep(10);
                    }
                }
            }
            catch (Exception ex) { Log("Selecting the tab didn't work: " + ex.Message); }

            // The tab didn't confirm being selected: try the other ways of activating it.
            try
            {
                IUIAutomationLegacyIAccessiblePattern legacy = tab.Element.GetCurrentPattern(Uia.LegacyIAccessiblePattern) as IUIAutomationLegacyIAccessiblePattern;
                if (legacy != null)
                {
                    legacy.DoDefaultAction();
                    return "tab activated";
                }
            }
            catch (Exception ex) { Log("Activating the tab didn't work: " + ex.Message); }

            try
            {
                IUIAutomationInvokePattern invoke = tab.Element.GetCurrentPattern(Uia.InvokePattern) as IUIAutomationInvokePattern;
                if (invoke != null)
                {
                    invoke.Invoke();
                    return "tab invoked";
                }
            }
            catch (Exception ex) { Log("Invoking the tab didn't work: " + ex.Message); }

            // Last resort: click on the tab's title area (away from its mute and close icons).
            try
            {
                Box r = AsBox(tab.Element.GetCurrentPropertyValue(Uia.BoundingRectangleProperty));
                if (r.W > 8 && r.H > 8)
                {
                    ClickAt((int)(r.X + r.W * 0.4), (int)(r.Y + r.H / 2));
                    return "tab clicked";
                }
            }
            catch (Exception ex) { Log("Clicking the tab didn't work: " + ex.Message); }
            return "couldn't select the tab";
        }

        // Watches keyboard focus for a short while after the switch. If it lands on a tab
        // (which makes Edge show the tab's preview card), it is moved to the web page, which
        // is where a real click on a tab leaves it. Returns a note for the log ("" if nothing to do).
        private static string KeepFocusOffTabs(IntPtr window, int watchMs)
        {
            Stopwatch timer = Stopwatch.StartNew();
            while (timer.ElapsedMilliseconds < watchMs)
            {
                if (GetForegroundWindow() != window) return "";
                if (FocusIsOnATab())
                {
                    long foundAt = timer.ElapsedMilliseconds;
                    if (FocusPage(window))
                    {
                        Thread.Sleep(30);
                        if (!FocusIsOnATab()) return "moved keyboard focus from the tab to the page (" + foundAt + " ms after switching)";
                    }
                    // Fallback: Escape, which only the focused tab strip receives (never the web page).
                    TapKey(VK_ESCAPE);
                    Thread.Sleep(30);
                    return FocusIsOnATab() ? "keyboard focus stayed on the tab (couldn't move it)" : "sent focus from the tab back to the page with Escape";
                }
                Thread.Sleep(15);
            }
            return "";
        }

        // True if keyboard focus is on a browser tab (or on something inside one, like its close button).
        private static bool FocusIsOnATab()
        {
            try
            {
                IUIAutomationElement e = automation.GetFocusedElement();
                IUIAutomationTreeWalker walker = automation.get_ControlViewWalker();
                for (int i = 0; i < 3 && e != null; i++)
                {
                    int type = AsInt(e.GetCurrentPropertyValue(Uia.ControlTypeProperty));
                    if (type == Uia.TabItem) return true;
                    if (type == Uia.Document || type == Uia.Window) return false;
                    e = walker.GetParentElement(e);
                }
            }
            catch { }
            return false;
        }

        // Gives keyboard focus to the web page showing in the window (the largest web document in it).
        private static bool FocusPage(IntPtr window)
        {
            try
            {
                IUIAutomationCacheRequest request = automation.CreateCacheRequest();
                request.AddProperty(Uia.ControlTypeProperty);
                request.AddProperty(Uia.BoundingRectangleProperty);
                request.put_TreeScope(Uia.TreeScope_Element | Uia.TreeScope_Children);

                IUIAutomationElement best = null;
                double bestArea = 0;
                List<IUIAutomationElement> queue = new List<IUIAutomationElement>();
                queue.Add(automation.ElementFromHandle(window));
                for (int q = 0; q < queue.Count && q < 400; q++)
                {
                    IUIAutomationElementArray kids;
                    try { kids = queue[q].BuildUpdatedCache(request).GetCachedChildren(); }
                    catch { continue; }
                    if (kids == null) continue;
                    int count = 0;
                    try { count = kids.get_Length(); } catch { }
                    for (int i = 0; i < count; i++)
                    {
                        IUIAutomationElement kid;
                        int type;
                        Box box;
                        try
                        {
                            kid = kids.GetElement(i);
                            type = AsInt(kid.GetCachedPropertyValue(Uia.ControlTypeProperty));
                            box = AsBox(kid.GetCachedPropertyValue(Uia.BoundingRectangleProperty));
                        }
                        catch { continue; }
                        if (type == Uia.Document)
                        {
                            // A web page: never look inside it, just remember the biggest one.
                            if (box.Area > bestArea) { best = kid; bestArea = box.Area; }
                        }
                        else if (CanContainTabs(type) && type != Uia.Tab && type != Uia.ToolBar)
                        {
                            queue.Add(kid);
                        }
                    }
                }
                if (best == null) return false;
                best.SetFocus();
                return true;
            }
            catch (Exception ex)
            {
                Log("Couldn't move focus to the page: " + ex.Message);
                return false;
            }
        }

        private static void TapKey(byte key)
        {
            keybd_event(key, 0, 0, UIntPtr.Zero);
            keybd_event(key, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        }

        private static void ClickAt(int x, int y)
        {
            POINT original;
            GetCursorPos(out original);
            SetCursorPos(x, y);
            mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
            mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);
            Thread.Sleep(30);
            SetCursorPos(original.X, original.Y);
        }

        // Brings the window to the front with a single activation, the way clicking its
        // taskbar button would. Returns how that was done, or null if Windows refused.
        private static string BringToFront(IntPtr window)
        {
            if (GetForegroundWindow() == window) return "was already in front";

            bool minimized = IsIconic(window);
            string how = minimized ? "restored" : "brought forward";

            // Windows normally only lets the app you're using hand focus to another app.
            // Joining the active app's input (the flyout's) is the standard, invisible way to do that.
            IntPtr foreground = GetForegroundWindow();
            uint unusedPid;
            uint foregroundThread = foreground == IntPtr.Zero ? 0 : GetWindowThreadProcessId(foreground, out unusedPid);
            uint thisThread = GetCurrentThreadId();
            bool attached = false;
            if (foregroundThread != 0 && foregroundThread != thisThread)
                attached = AttachThreadInput(thisThread, foregroundThread, true);
            try
            {
                if (minimized) ShowWindow(window, SW_RESTORE);   // Windows' normal restore animation
                SetForegroundWindow(window);
            }
            finally
            {
                if (attached) AttachThreadInput(thisThread, foregroundThread, false);
            }
            if (WaitForForeground(window, 50)) return how;

            // Fallback 1: the same switch Alt+Tab uses.
            SwitchToThisWindow(window, true);
            if (WaitForForeground(window, 80)) return how + " (Alt+Tab-style switch)";

            // Fallback 2 (last resort, may cause a brief flash): a tap of the Alt key.
            keybd_event(VK_MENU, 0, 0, UIntPtr.Zero);
            SetForegroundWindow(window);
            keybd_event(VK_MENU, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            if (WaitForForeground(window, 80)) return how + " (Alt key fallback)";
            return null;
        }

        private static bool WaitForForeground(IntPtr window, int maxMs)
        {
            Stopwatch timer = Stopwatch.StartNew();
            while (true)
            {
                if (GetForegroundWindow() == window) return true;
                if (timer.ElapsedMilliseconds >= maxMs) return false;
                Thread.Sleep(5);
            }
        }

        // ---------------------------------------------------------------- hover glow

        // settings.ini next to the log (written by the Windows Unlocked Toolkit app, or by hand):
        //   GlowStyle=Shift|Accent|Sunset|Ocean|White   GlowColor=#3B6CF6   GlowBrightness=100   GlowSize=100
        //   BringToMyScreen=true|false   move the browser window to the screen you clicked on (several screens)
        private static DateTime glowSettingsStamp = DateTime.MinValue;
        private static volatile bool bringToMyScreen;

        private static void ReadGlowSettings()
        {
            try
            {
                string path = Path.Combine(logDir, "settings.ini");
                DateTime stamp = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
                if (stamp == glowSettingsStamp) return;
                glowSettingsStamp = stamp;
                string style = "Shift";
                int colour = 0x3B6CF6;
                double bright = 100, size = 100;
                bool bring = false;
                if (stamp != DateTime.MinValue)
                {
                    foreach (string raw in File.ReadAllLines(path))
                    {
                        string line = raw.Trim();
                        int eq = line.IndexOf('=');
                        if (line.StartsWith("#") || eq < 0) continue;
                        string k = line.Substring(0, eq).Trim().ToLowerInvariant(), v = line.Substring(eq + 1).Trim();
                        double d;
                        if (k == "glowstyle") style = v;
                        else if (k == "glowcolor" && v.TrimStart('#').Length == 6)
                        {
                            int c;
                            if (int.TryParse(v.TrimStart('#'), System.Globalization.NumberStyles.HexNumber, null, out c)) colour = c;
                        }
                        else if (k == "glowbrightness" && double.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out d)) bright = d;
                        else if (k == "glowsize" && double.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out d)) size = d;
                        else if (k == "bringtomyscreen") bring = v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase) || v.Equals("on", StringComparison.OrdinalIgnoreCase) || v.Equals("yes", StringComparison.OrdinalIgnoreCase);
                    }
                }
                GlowLayer.Configure(style, colour, bright / 100.0, size / 100.0);
                bringToMyScreen = bring;
                Log("Glow look: " + style + ", brightness " + bright + "%, size " + size + "%");
            }
            catch (Exception ex) { Log("Couldn't read settings.ini: " + ex.Message); }
        }

        private static readonly AutoResetEvent hoverWake = new AutoResetEvent(false);
        private static volatile bool hoverPress;
        private static volatile bool hoverRelease;
        private static volatile int hoverReleaseX, hoverReleaseY;
        private static bool glowSuppressed;   // after a click on the card: stay dark until the pointer leaves the panel
        private static volatile IntPtr hoverRoot = IntPtr.Zero;
        private static MediaCard hoverCard;
        private static long hoverReadAt;
        private static bool glowOff;
        private static long glowOffCheckedAt = -100000;
        private static readonly Stopwatch hoverClock = Stopwatch.StartNew();
        private static DateTime lastHoverError = DateTime.MinValue;

        // Watches where the pointer is. Idle (asleep) unless the mouse moves; checks ~60 times a
        // second only while the pointer is over the Windows panel.
        private static void HoverLoop()
        {
            while (automation == null) Thread.Sleep(250);
            while (true)
            {
                bool tracking = false;
                try
                {
                    tracking = HoverStep();
                }
                catch (Exception ex)
                {
                    if ((DateTime.Now - lastHoverError).TotalMinutes >= 5)
                    {
                        lastHoverError = DateTime.Now;
                        Log("Hover tracking error: " + ex.Message);
                    }
                }
                if (tracking)
                {
                    hoverWake.WaitOne(16);
                }
                else
                {
                    Thread.Sleep(30);       // gather up quick mouse movements
                    hoverWake.WaitOne();    // sleep until the mouse moves again
                }
            }
        }

        // Returns true while the pointer is over the Windows panel.
        private static bool HoverStep()
        {
            POINT p;
            if (!GetCursorPos(out p)) return false;
            IntPtr hwnd = WindowFromPoint(p);
            IntPtr root = hwnd == IntPtr.Zero ? IntPtr.Zero : GetAncestor(hwnd, GA_ROOT);
            if (root != IntPtr.Zero && root == GlowLayer.Handle && hoverRoot != IntPtr.Zero) root = hoverRoot;   // look through the glow
            bool onFlyout = false;
            if (root != IntPtr.Zero && root != GlowLayer.Handle)
            {
                uint pid;
                GetWindowThreadProcessId(root, out pid);
                onFlyout = IsFlyoutWindow(ProcessName(pid), ClassOf(root));
            }
            if (!onFlyout)
            {
                hoverRoot = IntPtr.Zero;
                hoverCard = null;
                hoverPress = false;
                hoverRelease = false;
                glowSuppressed = false;
                GlowLayer.Hide();
                return false;
            }

            // A click on the card: the panel is about to close, so put the glow out at once
            // (before the panel starts moving) and keep it out until the pointer leaves.
            if (hoverRelease)
            {
                hoverRelease = false;
                MediaCard clicked = hoverCard;
                if (clicked != null && clicked.Root.Bounds.Contains(hoverReleaseX, hoverReleaseY) &&
                    !IsOnPlaybackControl(clicked, hoverReleaseX, hoverReleaseY))
                {
                    glowSuppressed = true;
                    GlowLayer.Dismiss();
                }
            }
            if (glowSuppressed)
            {
                hoverPress = false;
                if (root != hoverRoot) glowSuppressed = false;   // a fresh panel was opened
                else return true;
            }

            long now = hoverClock.ElapsedMilliseconds;
            if (now - glowOffCheckedAt > 2000)
            {
                glowOffCheckedAt = now;
                glowOff = File.Exists(Path.Combine(logDir, "glow-off.txt"));   // created by Toggle-Glow.cmd or the toolkit app
                ReadGlowSettings();
            }
            if (glowOff)
            {
                GlowLayer.Hide();
                hoverPress = false;
                return true;
            }

            // Read the card when the pointer arrives on the panel, then re-check every second
            // (more often while no card has been found) in case the panel changes.
            if (root != hoverRoot || now - hoverReadAt > (hoverCard == null ? 400 : 1000))
            {
                hoverRoot = root;
                hoverReadAt = now;
                hoverCard = null;
                Node tree = null;
                try { tree = Snapshot(automation.ElementFromHandle(root), true); }
                catch { }
                if (tree != null)
                {
                    List<MediaCard> cards = FindMediaCards(tree);
                    foreach (MediaCard c in cards)
                    {
                        if (c.Root.Bounds.Contains(p.X, p.Y)) { hoverCard = c; break; }
                    }
                    if (hoverCard == null && cards.Count > 0) hoverCard = cards[0];
                }
            }

            bool over = hoverCard != null && hoverCard.Root.Bounds.Contains(p.X, p.Y) && !IsOnPlaybackControl(hoverCard, p.X, p.Y);
            if (over) GlowLayer.ShowAt(hoverCard.Root.Bounds, ScaleOf(root), p.X, p.Y);
            else GlowLayer.Hide();
            if (hoverPress)
            {
                hoverPress = false;
                if (over) GlowLayer.Pulse();
            }
            return true;
        }

        private static double ScaleOf(IntPtr window)
        {
            try
            {
                uint dpi = GetDpiForWindow(window);
                if (dpi > 0) return dpi / 96.0;
            }
            catch { }
            return 1.0;
        }

        // ---------------------------------------------------------------- helpers

        private static string ProcessName(uint pid)
        {
            string name;
            lock (processNames)
            {
                if (processNames.TryGetValue(pid, out name)) return name;
            }
            name = "";
            try
            {
                using (Process p = Process.GetProcessById((int)pid)) name = p.ProcessName;
            }
            catch { }
            lock (processNames)
            {
                if (processNames.Count > 500) processNames.Clear();
                processNames[pid] = name;
            }
            return name;
        }

        private static string ClassOf(IntPtr hwnd)
        {
            StringBuilder sb = new StringBuilder(256);
            GetClassName(hwnd, sb, sb.Capacity);
            return sb.ToString();
        }

        // When a click on Windows' media controls doesn't find a browser media card, write down
        // the layout of those controls (at most once a minute) so the problem can be diagnosed.
        // Privacy: only the media-controls area itself is ever written down. Clicks anywhere else
        // (Wi-Fi, volume, notifications, calendar, Start...) are never logged, and the area is
        // recognised by Windows' internal control IDs, never by the text it shows.
        private static void DiagnoseMiss(POINT pt, string process, string windowClass, List<Node> chain, Node windowTree)
        {
            Node widest = windowTree;
            if (chain.Count > 0 && (widest == null || CountNodes(chain[chain.Count - 1]) > CountNodes(widest))) widest = chain[chain.Count - 1];

            if (widest == null || CountNodes(widest) <= 3)
            {
                // Nothing readable at all. Note just the window type (no contents), at most once an hour.
                if ((DateTime.Now - lastUnreadableNote).TotalMinutes < 1) return;
                lastUnreadableNote = DateTime.Now;
                StringBuilder under = new StringBuilder();
                foreach (Node n in chain)
                {
                    under.Append(under.Length == 0 ? "" : " < ").Append(Uia.TypeName(n.Type));
                    if (n.ClassName.Length > 0) under.Append(" class=").Append(Shorten(n.ClassName, 50));
                    if (n.AutomationId.Length > 0) under.Append(" id=").Append(Shorten(n.AutomationId, 50));
                }
                Log("Note: clicked a Windows panel (" + process + ", " + windowClass + ") whose contents couldn't be read" +
                    (under.Length > 0 ? " (under the pointer: " + under + ")." : "."));
                return;
            }

            Node media = MediaAreaAt(widest, pt.X, pt.Y);
            if (media == null) return;

            if ((DateTime.Now - lastTreeDump).TotalMinutes < 1) return;
            lastTreeDump = DateTime.Now;

            StringBuilder sb = new StringBuilder();
            sb.Append("Clicked Windows' media controls (").Append(process).Append(", ").Append(windowClass)
              .Append(") at ").Append(pt.X).Append(',').Append(pt.Y).Append(" but found no browser media card there.").AppendLine();
            sb.AppendLine("Layout of the media controls:");
            int lines = 0;
            DumpNode(media, 1, sb, ref lines);
            Log(sb.ToString().TrimEnd());
        }

        // The outermost element under the point whose Windows control ID marks it as media
        // controls (for example "MediaTransportControls"), or null.
        private static Node MediaAreaAt(Node n, double x, double y)
        {
            if (!n.Bounds.IsEmpty && !n.Bounds.Contains(x, y)) return null;
            if (n.AutomationId.IndexOf("MediaTransport", StringComparison.OrdinalIgnoreCase) >= 0 ||
                n.AutomationId.IndexOf("MediaControl", StringComparison.OrdinalIgnoreCase) >= 0 ||
                n.AutomationId.IndexOf("NowPlaying", StringComparison.OrdinalIgnoreCase) >= 0)
                return n;
            foreach (Node c in n.Children)
            {
                Node found = MediaAreaAt(c, x, y);
                if (found != null) return found;
            }
            return null;
        }

        private static int CountNodes(Node n)
        {
            int count = 1;
            foreach (Node c in n.Children) count += CountNodes(c);
            return count;
        }

        private static string Describe(Node n)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(Uia.TypeName(n.Type));
            if (n.Name.Length > 0) sb.Append(" \"").Append(Shorten(n.Name, 80)).Append('"');
            if (n.AutomationId.Length > 0) sb.Append(" id=").Append(Shorten(n.AutomationId, 60));
            if (n.ClassName.Length > 0) sb.Append(" class=").Append(Shorten(n.ClassName, 60));
            sb.Append(' ').Append(n.Bounds.ToString());
            return sb.ToString();
        }

        private static void DumpNode(Node n, int depth, StringBuilder sb, ref int lines)
        {
            if (lines >= 400) return;
            lines++;
            sb.Append(' ', depth * 2).AppendLine(Describe(n));
            foreach (Node c in n.Children) DumpNode(c, depth + 1, sb, ref lines);
        }

        private static string Shorten(string s, int max)
        {
            s = s.Replace("\r", " ").Replace("\n", " ");
            return s.Length <= max ? s : s.Substring(0, max) + "...";
        }

        internal static void Log(string message)
        {
            try
            {
                lock (logLock)
                {
                    if (logPath.Length == 0) return;
                    FileInfo info = new FileInfo(logPath);
                    if (info.Exists && info.Length > 1024 * 1024)
                    {
                        string old = Path.Combine(logDir, "log.old.txt");
                        if (File.Exists(old)) File.Delete(old);
                        File.Move(logPath, old);
                    }
                    File.AppendAllText(logPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + message + Environment.NewLine);
                }
            }
            catch { }
        }
    }

    // ======================================================================
    // The hover glow: a see-through, click-through window placed over the media card,
    // drawing a soft, slowly flowing halo of light around the card's edge that is
    // brightest near the pointer. It never takes focus and never blocks a click.
    // ======================================================================
    internal static class GlowLayer
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct GPoint { public int X; public int Y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct GSize { public int CX; public int CY; }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct BLENDFUNCTION
        {
            public byte BlendOp;
            public byte BlendFlags;
            public byte SourceConstantAlpha;
            public byte AlphaFormat;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BITMAPINFOHEADER
        {
            public uint biSize;
            public int biWidth;
            public int biHeight;
            public ushort biPlanes;
            public ushort biBitCount;
            public uint biCompression;
            public uint biSizeImage;
            public int biXPelsPerMeter;
            public int biYPelsPerMeter;
            public uint biClrUsed;
            public uint biClrImportant;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WNDCLASSEX
        {
            public uint cbSize;
            public uint style;
            public IntPtr lpfnWndProc;
            public int cbClsExtra;
            public int cbWndExtra;
            public IntPtr hInstance;
            public IntPtr hIcon;
            public IntPtr hCursor;
            public IntPtr hbrBackground;
            public string lpszMenuName;
            public string lpszClassName;
            public IntPtr hIconSm;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct GMSG
        {
            public IntPtr hwnd;
            public uint message;
            public IntPtr wParam;
            public IntPtr lParam;
            public uint time;
            public GPoint pt;
        }

        private delegate IntPtr WndProcDelegate(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern ushort RegisterClassEx(ref WNDCLASSEX wc);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateWindowEx(uint exStyle, string className, string windowName, uint style,
                                                    int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr DefWindowProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool PeekMessage(out GMSG msg, IntPtr hwnd, uint min, uint max, uint remove);
        [DllImport("user32.dll")]
        private static extern bool TranslateMessage(ref GMSG msg);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr DispatchMessage(ref GMSG msg);
        [DllImport("user32.dll")]
        private static extern uint MsgWaitForMultipleObjects(uint count, IntPtr[] handles, bool waitAll, uint milliseconds, uint wakeMask);
        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hwnd, int cmd);
        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref GPoint dst, ref GSize size, IntPtr hdcSrc,
                                                       ref GPoint src, int colorKey, ref BLENDFUNCTION blend, int flags);
        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hwnd);
        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleDC(IntPtr hdc);
        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFOHEADER info, uint usage, out IntPtr bits, IntPtr section, uint offset);
        [DllImport("gdi32.dll")]
        private static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr obj);
        [DllImport("dwmapi.dll")]
        private static extern int DwmFlush();
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr GetModuleHandle(string name);
        [DllImport("user32.dll")]
        private static extern bool SystemParametersInfo(uint action, uint param, ref int value, uint winIni);

        private const uint WS_POPUP = 0x80000000;
        private const uint WS_EX_LAYERED = 0x00080000;
        private const uint WS_EX_TRANSPARENT = 0x00000020;
        private const uint WS_EX_TOPMOST = 0x00000008;
        private const uint WS_EX_TOOLWINDOW = 0x00000080;
        private const uint WS_EX_NOACTIVATE = 0x08000000;
        private const uint WM_NCHITTEST = 0x0084;
        private const int HTTRANSPARENT = -1;
        private const int SW_HIDE = 0;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_SHOWWINDOW = 0x0040;
        private const int ULW_ALPHA = 2;
        private const uint PM_REMOVE = 1;
        private const uint QS_ALLINPUT = 0x04FF;
        private const uint INFINITE = 0xFFFFFFFF;
        private const uint SPI_GETCLIENTAREAANIMATION = 0x1042;

        // ---- design (sizes are at 100% display scaling) ----
        private const double MarginPx = 56;        // how far the light reaches beyond the card
        private const double CornerPx = 8;         // Windows 11 card corner radius
        private const double CoreAmount = 0.60, CoreWidth = 3.0;     // bright halo hugging the edge
        private const double BloomAmount = 0.48, BloomWidth = 16.0;  // wide, soft bloom
        private const double RimAmount = 0.60, RimWidth = 1.2;       // lit edge just inside the card
        private const double SheenFloor = 0.50, SheenWidth = 140.0;  // brighter near the pointer
        private const double SpecularWhite = 0.28, SpecularWidth = 63.0;
        private const double Cap = 0.95;
        private const double LightCover = 0.70;   // < 1 means it brightens like light rather than covering like paint
        private const double ColourCycleSeconds = 7.0, BreatheSeconds = 2.8;
        private const double FadeInSeconds = 0.26, FadeOutSeconds = 0.15, PulseSeconds = 0.17, DismissSeconds = 0.07;

        // Apple-Intelligence-style palette, evenly spaced around the card and looping.
        private static readonly int[] PaletteColours = { 0x8D9FFF, 0xBC82F3, 0xF5B9EA, 0xFF6778, 0xFFBA71, 0xC686FF };
        private static readonly int[] SunsetColours = { 0xFF9A5A, 0xFF5E7E, 0xFFB86B, 0xFF7A6B };
        private static readonly int[] OceanColours = { 0x5AB8FF, 0x6BE3C4, 0x8D9FFF, 0x4FD1FF };
        private static readonly int[] WhiteColours = { 0xFFFFFF, 0xE4EAFF };

        // Look chosen in settings.ini (see Program.ReadGlowSettings). Read by the glow thread when "changed" is set.
        private static int[] wantPalette = PaletteColours;
        private static double wantBrightness = 1, wantSize = 1;
        private static bool lookChanged;
        private static double brightness = 1, sizeFactor = 1;   // glow thread only

        // style: Shift | Accent | Sunset | Ocean | White.  brightness and size: 1 = the original look.
        public static void Configure(string style, int accentRgb, double bright, double size)
        {
            int[] colours;
            switch ((style ?? "").Trim().ToLowerInvariant())
            {
                case "accent": colours = new[] { accentRgb, Mix(accentRgb, 0xFFFFFF, 0.35), accentRgb, Mix(accentRgb, 0x000000, 0.15) }; break;
                case "sunset": colours = SunsetColours; break;
                case "ocean": colours = OceanColours; break;
                case "white": colours = WhiteColours; break;
                default: colours = PaletteColours; break;
            }
            lock (gate)
            {
                wantPalette = colours;
                wantBrightness = Math.Max(0.1, Math.Min(1.5, bright));
                wantSize = Math.Max(0.5, Math.Min(2.0, size));
                lookChanged = true;
            }
        }

        private static int Mix(int a, int b, double t)
        {
            int r = (int)Lerp((a >> 16) & 255, (b >> 16) & 255, t), g = (int)Lerp((a >> 8) & 255, (b >> 8) & 255, t), bl = (int)Lerp(a & 255, b & 255, t);
            return (r << 16) | (g << 8) | bl;
        }

        // ---- state shared with the hover thread ----
        private static readonly object gate = new object();
        private static readonly AutoResetEvent wake = new AutoResetEvent(false);
        private static bool wantVisible;
        private static Box wantCard;
        private static double wantScale = 1;
        private static int wantCursorX, wantCursorY;
        private static bool pulseRequested;
        private static bool dismissRequested;
        private static volatile IntPtr handle = IntPtr.Zero;
        private static WndProcDelegate wndProc;   // kept alive for the window's lifetime

        // ---- render state (glow thread only) ----
        private static IntPtr screenDc, memDc, bitmap, oldBitmap, bits;
        private static int width, height, winX, winY;
        private static Box builtCard;
        private static double builtScale;
        private static int[] pixels;
        private static int[] activeIndex;
        private static float[] activeX, activeY, activeAlpha;
        private static short[] activeHue;
        private static float[] paletteR = new float[1024], paletteG = new float[1024], paletteB = new float[1024];
        private static float[] expTable = new float[4096];
        private const float ExpRange = 20f;
        private static bool reduceMotion;

        public static IntPtr Handle { get { return handle; } }

        public static void Start()
        {
            Thread thread = new Thread(Run);
            thread.IsBackground = true;
            thread.Name = "MediaTabJump glow";
            thread.Start();
        }

        // Called by the hover tracker: light up around this card.
        public static void ShowAt(Box card, double scale, int cursorX, int cursorY)
        {
            lock (gate)
            {
                wantVisible = true;
                wantCard = card;
                wantScale = scale;
                wantCursorX = cursorX;
                wantCursorY = cursorY;
            }
            wake.Set();
        }

        public static void Hide()
        {
            bool changed;
            lock (gate)
            {
                changed = wantVisible;
                wantVisible = false;
            }
            if (changed) wake.Set();
        }

        // A quick brightening when the card is pressed.
        // The card was clicked: vanish almost immediately.
        public static void Dismiss()
        {
            lock (gate)
            {
                wantVisible = false;
                dismissRequested = true;
            }
            wake.Set();
        }

        public static void Pulse()
        {
            lock (gate) { pulseRequested = true; }
            wake.Set();
        }

        private static void Run()
        {
            try
            {
                BuildTables();
                CreateWindow();
            }
            catch (Exception ex)
            {
                Program.Log("The hover glow couldn't start: " + ex.Message);
                return;
            }

            Stopwatch clock = Stopwatch.StartNew();
            double last = 0, level = 0, pulse = 0, lastPulse = -10, lastRender = -10;
            bool fastFade = false;
            double cursorX = 0, cursorY = 0;
            bool haveCursor = false, shown = false;
            IntPtr[] waitHandles = { wake.SafeWaitHandle.DangerousGetHandle() };

            while (true)
            {
                PumpMessages();

                bool visible, pulseNow;
                Box card;
                double scale;
                int targetX, targetY;
                lock (gate)
                {
                    visible = wantVisible;
                    card = wantCard;
                    scale = wantScale;
                    targetX = wantCursorX;
                    targetY = wantCursorY;
                    pulseNow = pulseRequested;
                    pulseRequested = false;
                    if (dismissRequested) { fastFade = true; dismissRequested = false; }
                    if (lookChanged)
                    {
                        lookChanged = false;
                        BuildPalette(wantPalette);
                        brightness = wantBrightness;
                        if (sizeFactor != wantSize) { sizeFactor = wantSize; builtScale = -1; }   // rebuild the light map
                    }
                }

                double now = clock.Elapsed.TotalSeconds;
                double dt = Math.Min(now - last, 0.1);
                last = now;

                if (pulseNow && (visible || level > 0)) { pulse = 1; lastPulse = now; }
                double fadeIn = reduceMotion ? 0.08 : FadeInSeconds;
                // Right after a click the panel closes, so get out of the way quickly.
                double fadeOut = fastFade ? DismissSeconds : (reduceMotion || now - lastPulse < 0.6 ? 0.10 : FadeOutSeconds);
                if (visible) fastFade = false;
                level = visible ? Math.Min(1, level + dt / fadeIn) : Math.Max(0, level - dt / fadeOut);
                pulse = Math.Max(0, pulse - dt / PulseSeconds);

                if (level <= 0)
                {
                    fastFade = false;
                    if (shown)
                    {
                        ShowWindow(handle, SW_HIDE);
                        shown = false;
                    }
                    haveCursor = false;
                    MsgWaitForMultipleObjects(1, waitHandles, false, INFINITE, QS_ALLINPUT);
                    last = clock.Elapsed.TotalSeconds;
                    continue;
                }

                try
                {
                    if (card.HasValue && (!SameBox(card, builtCard) || scale != builtScale)) Rebuild(card, scale);
                    if (pixels == null) { level = 0; continue; }

                    // Let the bright spot glide after the pointer instead of snapping to it.
                    if (!haveCursor) { cursorX = targetX; cursorY = targetY; haveCursor = true; }
                    else
                    {
                        double follow = 1 - Math.Exp(-dt * 18);
                        cursorX += (targetX - cursorX) * follow;
                        cursorY += (targetY - cursorY) * follow;
                    }

                    // About 60-75 frames a second is plenty, even on high-refresh monitors.
                    if (shown && now - lastRender < 0.013)
                    {
                        if (DwmFlush() != 0) Thread.Sleep(5);
                        continue;
                    }
                    lastRender = now;
                    double eased = level * level * (3 - 2 * level);   // smooth start and finish
                    RenderFrame(now, (float)(cursorX - winX), (float)(cursorY - winY), pulse);
                    Present(eased);
                    if (!shown)
                    {
                        SetWindowPos(handle, new IntPtr(-1), 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
                        shown = true;
                    }
                }
                catch (Exception ex)
                {
                    Program.Log("Hover glow error: " + ex.Message);
                    level = 0;
                }

                if (DwmFlush() != 0) Thread.Sleep(15);   // wait for the next screen refresh
            }
        }

        private static bool SameBox(Box a, Box b)
        {
            return a.HasValue == b.HasValue && Math.Abs(a.X - b.X) < 0.5 && Math.Abs(a.Y - b.Y) < 0.5 &&
                   Math.Abs(a.W - b.W) < 0.5 && Math.Abs(a.H - b.H) < 0.5;
        }

        private static void PumpMessages()
        {
            GMSG msg;
            while (PeekMessage(out msg, IntPtr.Zero, 0, 0, PM_REMOVE))
            {
                TranslateMessage(ref msg);
                DispatchMessage(ref msg);
            }
        }

        private static IntPtr WindowProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            if (msg == WM_NCHITTEST) return new IntPtr(HTTRANSPARENT);   // clicks always pass straight through
            return DefWindowProc(hwnd, msg, wParam, lParam);
        }

        private static void CreateWindow()
        {
            wndProc = WindowProc;
            WNDCLASSEX wc = new WNDCLASSEX();
            wc.cbSize = (uint)Marshal.SizeOf(typeof(WNDCLASSEX));
            wc.lpfnWndProc = Marshal.GetFunctionPointerForDelegate(wndProc);
            wc.hInstance = GetModuleHandle(null);
            wc.lpszClassName = "MediaTabJumpGlow";
            if (RegisterClassEx(ref wc) == 0) throw new Exception("RegisterClassEx failed (" + Marshal.GetLastWin32Error() + ")");

            IntPtr hwnd = CreateWindowEx(WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE,
                                         "MediaTabJumpGlow", "", WS_POPUP, 0, 0, 1, 1, IntPtr.Zero, IntPtr.Zero, wc.hInstance, IntPtr.Zero);
            if (hwnd == IntPtr.Zero) throw new Exception("CreateWindowEx failed (" + Marshal.GetLastWin32Error() + ")");
            handle = hwnd;

            screenDc = GetDC(IntPtr.Zero);
            memDc = CreateCompatibleDC(screenDc);
        }

        private static void BuildTables()
        {
            BuildPalette(PaletteColours);
            for (int i = 0; i < expTable.Length; i++) expTable[i] = (float)Math.Exp(-i * ExpRange / expTable.Length);
        }

        private static void BuildPalette(int[] colours)
        {
            int n = colours.Length;
            for (int i = 0; i < 1024; i++)
            {
                double u = i / 1024.0 * n;
                int a = (int)Math.Floor(u) % n, b = (a + 1) % n;
                double t = u - Math.Floor(u);
                t = t * t * (3 - 2 * t);
                paletteR[i] = (float)(Lerp((colours[a] >> 16) & 255, (colours[b] >> 16) & 255, t) / 255.0);
                paletteG[i] = (float)(Lerp((colours[a] >> 8) & 255, (colours[b] >> 8) & 255, t) / 255.0);
                paletteB[i] = (float)(Lerp(colours[a] & 255, colours[b] & 255, t) / 255.0);
            }
        }

        private static double Lerp(double a, double b, double t) { return a + (b - a) * t; }

        private static float FastExpNeg(float k)
        {
            int i = (int)(k * (expTable.Length / ExpRange));
            return i >= expTable.Length ? 0f : expTable[i];
        }

        // Works out, once per card position, how strongly each pixel glows and which colour it gets.
        private static void Rebuild(Box card, double scale)
        {
            int animationsOn = 1;
            try { SystemParametersInfo(SPI_GETCLIENTAREAANIMATION, 0, ref animationsOn, 0); } catch { }
            reduceMotion = animationsOn == 0;   // respect Windows' "Animation effects" setting

            double s = scale <= 0 ? 1 : scale;
            int margin = (int)Math.Ceiling(MarginPx * s * sizeFactor);
            int w = Math.Min((int)Math.Ceiling(card.W) + 2 * margin, 4000);
            int h = Math.Min((int)Math.Ceiling(card.H) + 2 * margin, 4000);
            int x0 = (int)Math.Floor(card.X) - margin, y0 = (int)Math.Floor(card.Y) - margin;

            if (!PrepareSurface(w, h)) { pixels = null; return; }
            BuildMaps(card, scale, x0, y0, w, h);
        }

        // Makes sure the drawing surface is w x h pixels.
        private static bool PrepareSurface(int w, int h)
        {
            if (bitmap == IntPtr.Zero || w != width || h != height)
            {
                if (bitmap != IntPtr.Zero)
                {
                    SelectObject(memDc, oldBitmap);
                    DeleteObject(bitmap);
                    bitmap = IntPtr.Zero;
                }
                BITMAPINFOHEADER info = new BITMAPINFOHEADER();
                info.biSize = (uint)Marshal.SizeOf(typeof(BITMAPINFOHEADER));
                info.biWidth = w;
                info.biHeight = -h;   // top-down rows
                info.biPlanes = 1;
                info.biBitCount = 32;
                bitmap = CreateDIBSection(screenDc, ref info, 0, out bits, IntPtr.Zero, 0);
                if (bitmap == IntPtr.Zero) return false;
                oldBitmap = SelectObject(memDc, bitmap);
            }
            return true;
        }

        // Pure calculation (no Windows calls), so it can be checked anywhere.
        private static void BuildMaps(Box card, double scale, int x0, int y0, int w, int h)
        {
            double s = scale <= 0 ? 1 : scale;
            width = w; height = h; winX = x0; winY = y0;
            pixels = new int[w * h];

            double cw = card.W, ch = card.H, r = CornerPx * s;
            double cx = card.X - x0 + cw / 2, cy = card.Y - y0 + ch / 2;
            List<int> idx = new List<int>();
            List<float> ax = new List<float>(), ay = new List<float>(), aa = new List<float>();
            List<short> hue = new List<short>();
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    double px = x + 0.5 - cx, py = y + 0.5 - cy;
                    double qx = Math.Abs(px) - (cw / 2 - r), qy = Math.Abs(py) - (ch / 2 - r);
                    double mx = Math.Max(qx, 0), my = Math.Max(qy, 0);
                    double d = Math.Sqrt(mx * mx + my * my) + Math.Min(Math.Max(qx, qy), 0) - r;   // distance from the card's edge
                    double dp = Math.Max(d, 0);
                    double sz = s * sizeFactor;
                    double outside = CoreAmount * Math.Exp(-dp / (CoreWidth * sz)) +
                                     BloomAmount * Math.Exp(-(dp * dp) / (2 * (BloomWidth * sz) * (BloomWidth * sz)));
                    double inside = RimAmount * Math.Exp(Math.Min(d, 0) / (RimWidth * s));
                    double t = Math.Min(Math.Max(d + 0.5, 0), 1);
                    double a = Math.Min(inside * (1 - t) + outside * t, Cap);
                    if (a < 1.0 / 255) continue;
                    double u = (Math.Atan2(py / ch, px / cw) + Math.PI) / (2 * Math.PI);
                    idx.Add(y * w + x);
                    ax.Add((float)(x + 0.5));
                    ay.Add((float)(y + 0.5));
                    aa.Add((float)a);
                    hue.Add((short)((int)(u * 1024) & 1023));
                }
            }
            activeIndex = idx.ToArray();
            activeX = ax.ToArray();
            activeY = ay.ToArray();
            activeAlpha = aa.ToArray();
            activeHue = hue.ToArray();
            builtCard = card;
            builtScale = scale;
        }

        private static void RenderFrame(double now, float cursorX, float cursorY, double pulse)
        {
            RenderPixels(now, cursorX, cursorY, pulse);
            Marshal.Copy(pixels, 0, bits, pixels.Length);
        }

        private static void RenderPixels(double now, float cursorX, float cursorY, double pulse)
        {
            double s = builtScale <= 0 ? 1 : builtScale;
            double phase = reduceMotion ? 0.15 : now / ColourCycleSeconds;
            int phaseIndex = (int)((phase - Math.Floor(phase)) * 1024) & 1023;
            double breathe = reduceMotion ? 1 : 0.92 + 0.08 * Math.Sin(2 * Math.PI * now / BreatheSeconds);
            float boost = (float)(breathe * (1 + 0.6 * pulse) * brightness);
            float sheenK = (float)(1 / (2 * (SheenWidth * s) * (SheenWidth * s)));
            float specK = (float)(1 / (2 * (SpecularWidth * s) * (SpecularWidth * s)));
            float sheenFloor = (float)SheenFloor, specWhite = (float)SpecularWhite, cover = (float)LightCover;

            for (int i = 0; i < activeIndex.Length; i++)
            {
                float dx = activeX[i] - cursorX, dy = activeY[i] - cursorY;
                float d2 = dx * dx + dy * dy;
                float sheen = sheenFloor + (1 - sheenFloor) * FastExpNeg(d2 * sheenK);
                float g = activeAlpha[i] * sheen * boost;
                if (g > 1) g = 1;
                float white = specWhite * FastExpNeg(d2 * specK);
                int c = (activeHue[i] + phaseIndex) & 1023;
                float rr = paletteR[c] + (1 - paletteR[c]) * white;
                float gg = paletteG[c] + (1 - paletteG[c]) * white;
                float bb = paletteB[c] + (1 - paletteB[c]) * white;
                int A = (int)(g * cover * 255 + 0.5f);
                int R = (int)(rr * g * 255 + 0.5f), G = (int)(gg * g * 255 + 0.5f), B = (int)(bb * g * 255 + 0.5f);
                if (R > 255) R = 255;
                if (G > 255) G = 255;
                if (B > 255) B = 255;
                pixels[activeIndex[i]] = (A << 24) | (R << 16) | (G << 8) | B;
            }
        }

        private static void Present(double opacity)
        {
            BLENDFUNCTION blend = new BLENDFUNCTION();
            blend.BlendOp = 0;            // AC_SRC_OVER
            blend.SourceConstantAlpha = (byte)Math.Max(0, Math.Min(255, (int)(opacity * 255 + 0.5)));
            blend.AlphaFormat = 1;        // AC_SRC_ALPHA (per-pixel, premultiplied)
            GPoint dst = new GPoint();
            dst.X = winX;
            dst.Y = winY;
            GPoint src = new GPoint();
            GSize size = new GSize();
            size.CX = width;
            size.CY = height;
            UpdateLayeredWindow(handle, screenDc, ref dst, ref size, memDc, ref src, 0, ref blend, ULW_ALPHA);
        }
    }

    // ======================================================================
    // Several screens: moves a window to the screen you're using, keeping its
    // size and place relative to that screen, and keeping it maximized,
    // minimized or normal as it was.
    // ======================================================================
    internal static class ScreenMover
    {
        [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] private struct PT { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)]
        private struct WINDOWPLACEMENT { public int length; public int flags; public int showCmd; public PT ptMin; public PT ptMax; public RECT rcNormal; }
        [StructLayout(LayoutKind.Sequential)]
        private struct MONITORINFO { public int cbSize; public RECT rcMonitor; public RECT rcWork; public uint dwFlags; }

        [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
        [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(PT pt, uint flags);
        [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);
        [DllImport("user32.dll")] private static extern bool GetWindowPlacement(IntPtr hwnd, ref WINDOWPLACEMENT wp);
        [DllImport("user32.dll")] private static extern bool SetWindowPlacement(IntPtr hwnd, ref WINDOWPLACEMENT wp);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hwnd, int cmd);

        private const uint MONITOR_DEFAULTTONEAREST = 2, MONITOR_DEFAULTTOPRIMARY = 1;
        private const int SW_SHOWMINIMIZED = 2, SW_SHOWMAXIMIZED = 3, SW_MAXIMIZE = 3, SW_SHOWNOACTIVATE = 4, SW_SHOWMINNOACTIVE = 7;

        // Returns a line for the log, or null if nothing needed doing.
        public static string MoveTo(IntPtr window, int x, int y)
        {
            try
            {
                PT p = new PT(); p.X = x; p.Y = y;
                IntPtr to = MonitorFromPoint(p, MONITOR_DEFAULTTONEAREST);
                IntPtr from = MonitorFromWindow(window, MONITOR_DEFAULTTONEAREST);
                if (to == IntPtr.Zero || from == IntPtr.Zero || to == from) return null;
                MONITORINFO a = Info(from), b = Info(to), main = Info(MonitorFromPoint(new PT(), MONITOR_DEFAULTTOPRIMARY));

                WINDOWPLACEMENT wp = new WINDOWPLACEMENT();
                wp.length = Marshal.SizeOf(typeof(WINDOWPLACEMENT));
                if (!GetWindowPlacement(window, ref wp)) return "Couldn't read where the browser window is";

                // rcNormal is in "workspace" coordinates: screen coordinates shifted by the main display's taskbar.
                int ox = main.rcWork.Left - main.rcMonitor.Left, oy = main.rcWork.Top - main.rcMonitor.Top;
                RECT r = wp.rcNormal;
                r.Left += ox; r.Right += ox; r.Top += oy; r.Bottom += oy;

                // Same place relative to the work area, shrunk if the new screen is smaller.
                int w = Math.Min(r.Right - r.Left, b.rcWork.Right - b.rcWork.Left);
                int h = Math.Min(r.Bottom - r.Top, b.rcWork.Bottom - b.rcWork.Top);
                double fx = Fraction(r.Left - a.rcWork.Left, (a.rcWork.Right - a.rcWork.Left) - (r.Right - r.Left));
                double fy = Fraction(r.Top - a.rcWork.Top, (a.rcWork.Bottom - a.rcWork.Top) - (r.Bottom - r.Top));
                int left = b.rcWork.Left + (int)Math.Round(fx * Math.Max(0, (b.rcWork.Right - b.rcWork.Left) - w));
                int top = b.rcWork.Top + (int)Math.Round(fy * Math.Max(0, (b.rcWork.Bottom - b.rcWork.Top) - h));
                wp.rcNormal.Left = left - ox; wp.rcNormal.Top = top - oy;
                wp.rcNormal.Right = left + w - ox; wp.rcNormal.Bottom = top + h - oy;

                bool maximized = wp.showCmd == SW_SHOWMAXIMIZED, minimized = wp.showCmd == SW_SHOWMINIMIZED;
                wp.showCmd = minimized ? SW_SHOWMINNOACTIVE : SW_SHOWNOACTIVATE;
                wp.flags &= 2;   // keep "restore to maximized" for a minimized window
                if (!SetWindowPlacement(window, ref wp)) return "Couldn't move the browser window to your screen";
                if (maximized) ShowWindow(window, SW_MAXIMIZE);
                return "Moved the browser window to the screen you clicked on" + (maximized ? " (kept maximized)" : "");
            }
            catch (Exception ex) { return "Couldn't move the browser window: " + ex.Message; }
        }

        private static MONITORINFO Info(IntPtr monitor)
        {
            MONITORINFO mi = new MONITORINFO();
            mi.cbSize = Marshal.SizeOf(typeof(MONITORINFO));
            GetMonitorInfo(monitor, ref mi);
            return mi;
        }

        private static double Fraction(int offset, int room)
        {
            if (room <= 0) return 0;
            return Math.Max(0, Math.Min(1, offset / (double)room));
        }
    }
}
