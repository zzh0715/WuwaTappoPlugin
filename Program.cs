using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Automation;
using System.Windows.Forms;

namespace WuwaTappoPlugin
{
    internal static class Log
    {
        private static readonly object Gate = new object();

        public static void Write(string msg)
        {
            try
            {
                lock (Gate)
                {
                    string dir = Path.GetDirectoryName(Application.ExecutablePath);
                    File.AppendAllText(Path.Combine(dir, "plugin.log"),
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + msg + Environment.NewLine);
                }
            }
            catch { }
        }
    }

    internal struct RectI
    {
        public int X;
        public int Y;
        public int W;
        public int H;

        public int CenterX { get { return X + W / 2; } }
        public int CenterY { get { return Y + H / 2; } }
    }

    internal static class Native
    {
        private const int INPUT_SIZE = 40;

        internal const int WM_MOUSEACTIVATE = 0x0021;
        internal const int WM_HOTKEY = 0x0312;
        internal const int MA_ACTIVATE = 3;
        internal const uint MOD_ALT = 0x0001;
        internal const uint MOD_CONTROL = 0x0002;
        internal const uint VK_Q = 0x51;
        internal const uint VK_H = 0x48;
        internal const int VK_LBUTTON = 0x01;

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern uint SendInput(uint nInputs, IntPtr pInputs, int cbSize);

        [DllImport("user32.dll")]
        internal static extern bool SetCursorPos(int x, int y);

        [DllImport("user32.dll")]
        internal static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        internal static extern short GetAsyncKeyState(int vKey);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll")]
        internal static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

        [DllImport("user32.dll")]
        internal static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        internal static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [DllImport("user32.dll")]
        internal static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        internal static extern bool SetForegroundWindow(IntPtr hWnd);

        internal static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);

        internal delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        internal static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        internal static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        internal static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll")]
        internal static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        internal static extern bool SetProcessDPIAware();

        [DllImport("winmm.dll")]
        internal static extern uint timeBeginPeriod(uint uPeriod);

        [DllImport("winmm.dll")]
        internal static extern uint timeEndPeriod(uint uPeriod);

        [StructLayout(LayoutKind.Sequential)]
        internal struct POINT
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;

            public int Width { get { return Right - Left; } }
            public int Height { get { return Bottom - Top; } }
        }

        private static void WriteKeyboard(IntPtr p, byte vk, bool up)
        {
            Marshal.WriteInt32(p, 0, 1);            // type = INPUT_KEYBOARD
            Marshal.WriteInt32(p, 4, 0);            // padding
            Marshal.WriteInt16(p, 8, (short)vk);    // wVk
            Marshal.WriteInt16(p, 10, 0);           // wScan
            Marshal.WriteInt32(p, 12, up ? 2 : 0);  // dwFlags
            Marshal.WriteInt32(p, 16, 0);           // time
            Marshal.WriteInt64(p, 24, 0);           // dwExtraInfo
        }

        private static void WriteMouse(IntPtr p, bool down)
        {
            Marshal.WriteInt32(p, 0, 0);            // type = INPUT_MOUSE
            Marshal.WriteInt32(p, 4, 0);            // padding
            Marshal.WriteInt32(p, 8, 0);            // dx
            Marshal.WriteInt32(p, 12, 0);           // dy
            Marshal.WriteInt32(p, 16, 0);           // mouseData
            Marshal.WriteInt32(p, 20, down ? 2 : 4);// dwFlags (LEFTDOWN / LEFTUP)
            Marshal.WriteInt32(p, 24, 0);           // time
            Marshal.WriteInt64(p, 32, 0);           // dwExtraInfo
        }

        internal static int KeyTaps(byte vk, int pairs)
        {
            if (pairs <= 0) return 0;
            int total = pairs * 2;
            IntPtr buf = Marshal.AllocHGlobal(INPUT_SIZE * total);
            try
            {
                for (int i = 0; i < pairs; i++)
                {
                    IntPtr p = IntPtr.Add(buf, i * INPUT_SIZE * 2);
                    WriteKeyboard(p, vk, false);
                    WriteKeyboard(IntPtr.Add(p, INPUT_SIZE), vk, true);
                }
                return (int)SendInput((uint)total, buf, INPUT_SIZE);
            }
            finally
            {
                Marshal.FreeHGlobal(buf);
            }
        }

        internal static int MouseLeftClicks(int pairs)
        {
            if (pairs <= 0) return 0;
            int total = pairs * 2;
            IntPtr buf = Marshal.AllocHGlobal(INPUT_SIZE * total);
            try
            {
                for (int i = 0; i < pairs; i++)
                {
                    IntPtr p = IntPtr.Add(buf, i * INPUT_SIZE * 2);
                    WriteMouse(p, true);
                    WriteMouse(IntPtr.Add(p, INPUT_SIZE), false);
                }
                return (int)SendInput((uint)total, buf, INPUT_SIZE);
            }
            finally
            {
                Marshal.FreeHGlobal(buf);
            }
        }

        internal static void ClickAt(int x, int y)
        {
            POINT old;
            GetCursorPos(out old);
            SetCursorPos(x, y);
            Thread.Sleep(40);
            MouseLeftClicks(1);
            Thread.Sleep(30);
            SetCursorPos(old.X, old.Y);
        }
    }

    internal static class GameWindowFinder
    {
        // 用 EnumWindows 查找游戏主窗口（不依赖可能受保护的 Process.MainWindowHandle）
        private static IntPtr cached = IntPtr.Zero;
        private static bool loggedMiss;

        public static IntPtr Find()
        {
            if (cached != IntPtr.Zero && Native.IsWindow(cached)) return cached;
            cached = IntPtr.Zero;
            IntPtr found = IntPtr.Zero;
            try
            {
                Native.EnumWindows(delegate(IntPtr h, IntPtr l)
                {
                    if (found != IntPtr.Zero) return true;
                    StringBuilder title = new StringBuilder(256);
                    if (Native.GetWindowText(h, title, 256) > 0 && title.ToString() == "Wuwa Tappo")
                    {
                        uint pid;
                        Native.GetWindowThreadProcessId(h, out pid);
                        try
                        {
                            Process p = Process.GetProcessById((int)pid);
                            if (string.Equals(p.ProcessName, "WuwaTappo", StringComparison.OrdinalIgnoreCase))
                            {
                                found = h;
                                return false;
                            }
                        }
                        catch { }
                    }
                    return true;
                }, IntPtr.Zero);
            }
            catch { }
            if (found != IntPtr.Zero)
            {
                cached = found;
                loggedMiss = false;
            }
            else if (!loggedMiss)
            {
                loggedMiss = true;
                Log.Write("未找到游戏窗口（EnumWindows）");
            }
            return cached;
        }
    }

    internal sealed class Snapshot
    {
        public bool Running;
        public int Counter = -1;
        public List<RectI> Chests = new List<RectI>();
        public List<string> Countdowns = new List<string>();
        public List<AutomationElement> ChestElements = new List<AutomationElement>();
        public string Error;
    }

    internal static class GameReader
    {
        public static Snapshot Read()
        {
            var snap = new Snapshot();
            IntPtr hwnd = GameWindowFinder.Find();
            if (hwnd == IntPtr.Zero)
            {
                snap.Error = "游戏未运行";
                return snap;
            }
            snap.Running = true;
            try
            {
                AutomationElement root = AutomationElement.FromHandle(hwnd);
                AutomationElement doc = root.FindFirst(TreeScope.Descendants,
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Document));
                if (doc == null)
                {
                    snap.Error = "未找到页面";
                    return snap;
                }
                AutomationElementCollection all = doc.FindAll(TreeScope.Descendants, Condition.TrueCondition);
                foreach (AutomationElement el in all)
                {
                    try
                    {
                        ControlType ct = el.Current.ControlType;
                        string name = el.Current.Name ?? "";
                        if (ct == ControlType.Text)
                        {
                            string trimmed = name.Trim();
                            int v;
                            if (snap.Counter < 0 && trimmed.Length > 0 && trimmed.Length <= 9 && int.TryParse(trimmed, out v))
                                snap.Counter = v;
                        }
                        else if (ct == ControlType.Image)
                        {
                            if (Regex.IsMatch(name, @"^\d{1,2}:\d{2}$"))
                                snap.Countdowns.Add(name);
                        }
                        else if (ct == ControlType.Button)
                        {
                            string cls = "";
                            try { cls = el.Current.ClassName ?? ""; } catch { }
                            if (cls == "reward-btn")
                            {
                                System.Windows.Rect r = el.Current.BoundingRectangle;
                                if (!r.IsEmpty)
                                {
                                    snap.Chests.Add(new RectI
                                    {
                                        X = (int)r.X,
                                        Y = (int)r.Y,
                                        W = (int)r.Width,
                                        H = (int)r.Height
                                    });
                                    snap.ChestElements.Add(el);
                                }
                            }
                        }
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                snap.Error = ex.Message;
            }
            return snap;
        }
    }

    internal static class AnchorReader
    {
        // 读取游戏右侧图标面板（pet-dock-content）的物理矩形作为开关锚点
        public static Rectangle? Read(IntPtr gameHwnd)
        {
            try
            {
                AutomationElement root = AutomationElement.FromHandle(gameHwnd);
                AutomationElement doc = root.FindFirst(TreeScope.Descendants,
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Document));
                if (doc == null) return null;
                AutomationElement panel = doc.FindFirst(TreeScope.Descendants,
                    new PropertyCondition(AutomationElement.ClassNameProperty, "pet-dock-content"));
                if (panel == null) return null;
                System.Windows.Rect r = panel.Current.BoundingRectangle;
                if (r.IsEmpty || r.Width <= 0 || r.Height <= 0) return null;
                return new Rectangle((int)r.X, (int)r.Y, (int)r.Width, (int)r.Height);
            }
            catch
            {
                return null;
            }
        }
    }

    internal sealed class SpamEngine
    {
        private const byte SpamKey = 0x87; // F24
        private const int FixedRate = 40;  // 固定 40 次/秒，对正常输入干扰小

        private Thread thread;
        private volatile bool running;
        private volatile int measured;
        private long sentTotal;

        public int MeasuredRate { get { return measured; } }
        public bool IsRunning { get { return running; } }
        public int Rate { get { return FixedRate; } }

        public void Start()
        {
            if (running) return;
            running = true;
            thread = new Thread(Loop);
            thread.IsBackground = true;
            thread.Start();
        }

        public void Stop()
        {
            if (!running) return;
            running = false;
            if (thread != null)
            {
                thread.Join(2000);
                thread = null;
            }
            measured = 0;
        }

        private void Loop()
        {
            // 均匀发送：每隔 ~25ms 发一对 down/up（40 次/秒）
            const int TickMs = 25;
            Stopwatch sw = Stopwatch.StartNew();
            long lastSent = 0;
            long lastT = 0;
            while (running)
            {
                long tickStart = sw.ElapsedMilliseconds;
                Native.KeyTaps(SpamKey, 1);
                Interlocked.Increment(ref sentTotal);
                long now = sw.ElapsedMilliseconds;
                if (now - lastT >= 1000)
                {
                    measured = (int)((sentTotal - lastSent) * 1000.0 / (now - lastT));
                    lastSent = sentTotal;
                    lastT = now;
                }
                long elapsed = now - tickStart;
                if (elapsed < TickMs) Thread.Sleep((int)(TickMs - elapsed));
            }
        }
    }

    internal sealed class AutoChestEngine
    {
        private const int RetryAfterMinutes = 10;

        private Thread thread;
        private volatile bool enabled = true;
        private volatile Snapshot last = new Snapshot();
        private readonly Dictionary<int, DateTime> backoff = new Dictionary<int, DateTime>();
        private readonly object gate = new object();

        public Snapshot Last { get { return last; } }
        public volatile Action OnChestOpened;   // 成功领取后回调（用于刷新气泡缓存）
        public volatile Action<bool> SetSpam;   // 自动连点控制（true=开启，false=关闭）
        private volatile bool autoSpamOn;
        private DateTime autoSpamSuppressUntil = DateTime.MinValue;
        private readonly int boostTestCap;      // 测试用：环境变量 WWTP_BOOST_TEST 指定触发阈值（0=正常）

        public AutoChestEngine()
        {
            int cap = 0;
            try { int.TryParse(Environment.GetEnvironmentVariable("WWTP_BOOST_TEST"), out cap); } catch { cap = 0; }
            boostTestCap = cap > 0 ? cap : 0;
        }

        public void Start()
        {
            thread = new Thread(Loop);
            thread.IsBackground = true;
            thread.Start();
        }

        public void SetEnabled(bool on)
        {
            enabled = on;
        }

        // 用户手动停止连点时调用：重置自动连点状态并短暂抑制，尊重用户意图
        public void ResetAutoSpam()
        {
            autoSpamOn = false;
            autoSpamSuppressUntil = DateTime.Now.AddMinutes(2);
        }

        private void SetSpamSafe(bool on)
        {
            try
            {
                Action<bool> s = SetSpam;
                if (s != null) s(on);
            }
            catch { }
        }

        // 取所有倒计时气泡中最小的剩余秒数（"mm:ss"）；没有倒计时返回 -1
        private static int MinCountdownSeconds(Snapshot snap)
        {
            int min = -1;
            if (snap.Countdowns == null) return min;
            foreach (string cd in snap.Countdowns)
            {
                try
                {
                    string[] parts = cd.Split(':');
                    if (parts.Length != 2) continue;
                    int total = int.Parse(parts[0]) * 60 + int.Parse(parts[1]);
                    if (min < 0 || total < min) min = total;
                }
                catch { }
            }
            return min;
        }

        private void Loop()
        {
            while (true)
            {
                Snapshot snap;
                try { snap = GameReader.Read(); }
                catch (Exception ex) { snap = new Snapshot { Error = ex.Message }; }
                SetLast(snap);

                // ===== 自动连点补足计数：倒计时剩余 ≤5 分钟（或两个宝箱已就绪）且计数 <2000 时，自动刷到 2000 =====
                int chestCount = snap.Chests.Count;
                int remaining = MinCountdownSeconds(snap);
                int boostTarget = boostTestCap > 0 ? boostTestCap : 2000;   // 测试环境变量可覆盖目标
                bool nearReset = remaining >= 0 && remaining <= 300;   // 倒计时剩余 ≤5 分钟
                bool counterLow = snap.Counter >= 0 && snap.Counter < boostTarget;
                bool needBoost = counterLow && (nearReset || chestCount >= 2);
                if (needBoost && !autoSpamOn && DateTime.Now >= autoSpamSuppressUntil)
                {
                    autoSpamOn = true;
                    SetSpamSafe(true);
                    Log.Write("倒计时剩余 " + (remaining < 0 ? "?" : remaining + " 秒")
                        + "，计数不足 " + boostTarget + "（当前 " + snap.Counter + "），自动开启连点补足");
                }
                else if (autoSpamOn && !needBoost)
                {
                    autoSpamOn = false;
                    SetSpamSafe(false);
                    if (snap.Counter >= boostTarget)
                        Log.Write("计数已补足（" + snap.Counter + "），关闭连点");
                    else
                        Log.Write("条件解除，关闭自动连点");
                }

                if (enabled && snap.Running && !autoSpamOn)
                {
                    var pending = new List<AutomationElement>(snap.ChestElements);
                    foreach (AutomationElement el in pending)
                    {
                        System.Windows.Rect r;
                        try { r = el.Current.BoundingRectangle; } catch { continue; }
                        if (r.IsEmpty) continue;
                        RectI c = new RectI
                        {
                            X = (int)r.X,
                            Y = (int)r.Y,
                            W = (int)r.Width,
                            H = (int)r.Height
                        };
                        int key = c.X;
                        DateTime now = DateTime.Now;
                        lock (backoff)
                        {
                            DateTime t;
                            if (backoff.TryGetValue(key, out t) && now < t) continue;
                        }
                        if (snap.Counter >= 0 && snap.Counter < 1000)
                        {
                            Log.Write("宝箱就绪但计数不足 1000（当前 " + snap.Counter + "），跳过");
                            continue;
                        }
                        TryClaim(snap, el, c, key);
                        Thread.Sleep(500);
                    }
                }
                Thread.Sleep(1000);
            }
        }

        private void SetLast(Snapshot snap)
        {
            lock (gate) { last = snap; }
        }

        private void TryClaim(Snapshot snap, AutomationElement el, RectI c, int key)
        {
            int cx = c.CenterX;
            int cy = c.CenterY;
            int before = snap.Counter;
            bool invoked = false;
            // 优先用 UI Automation 的 InvokePattern：注入鼠标点击会被游戏的输入层过滤，
            // 而 UIA 通道是游戏确实响应的（已验证设置面板可用它打开）。
            try
            {
                InvokePattern ip = (InvokePattern)el.GetCurrentPattern(InvokePattern.Pattern);
                ip.Invoke();
                invoked = true;
            }
            catch (Exception ex)
            {
                Log.Write("UIA Invoke 失败（回退鼠标点击）: " + ex.Message);
            }
            if (!invoked)
            {
                try
                {
                    Native.ClickAt(cx, cy);
                }
                catch (Exception ex)
                {
                    Log.Write("点击宝箱异常: " + ex.Message);
                    return;
                }
            }
            Thread.Sleep(1500);
            Snapshot after;
            try { after = GameReader.Read(); }
            catch (Exception ex)
            {
                Log.Write("开箱后读取异常: " + ex.Message);
                return;
            }
            SetLast(after);

            bool stillThere = false;
            foreach (RectI b in after.Chests)
            {
                if (Math.Abs(b.X - c.X) <= 24 && Math.Abs(b.Y - c.Y) <= 24)
                {
                    stillThere = true;
                    break;
                }
            }
            bool counterDropped = after.Counter >= 0 && before >= 0 && after.Counter <= before - 900;
            bool success = !stillThere || counterDropped;

            Log.Write("开箱尝试 (" + cx + "," + cy + ") 方式=" + (invoked ? "UIA" : "鼠标")
                + " 前计数=" + before + " 后计数=" + after.Counter
                + " 按钮仍存在=" + stillThere + " => " + (success ? "成功" : "失败，10 分钟后重试"));

            lock (backoff)
            {
                if (success) backoff.Remove(key);
                else backoff[key] = DateTime.Now.AddMinutes(RetryAfterMinutes);
            }
            if (success)
            {
                try
                {
                    Action cb = OnChestOpened;
                    if (cb != null) cb();
                }
                catch { }
            }
        }
    }

    internal sealed class PluginForm : Form
    {
        // ---- 开关样式（与游戏 UI 一致的奶米/暖棕配色）----
        private static readonly Color CardColor = Color.FromArgb(245, 238, 227);   // 面板奶米色
        private static readonly Color TrackOffColor = Color.FromArgb(239, 226, 200); // 关：浅奶黄轨道
        private static readonly Color TrackOffBorder = Color.FromArgb(223, 201, 162);
        private static readonly Color TrackOnColor = Color.FromArgb(187, 129, 99);  // 开：暖棕轨道
        private static readonly Color TrackOnBorder = Color.FromArgb(166, 110, 82);
        private static readonly Color KnobColor = Color.FromArgb(255, 252, 246);    // 白色滑块

        private const int CardW = 58;
        private const int CardH = 28;
        private const int TrackX = 4;
        private const int TrackY = 4;
        private const int TrackW = 50;
        private const int TrackH = 20;
        private const int KnobD = 16;

        private readonly SpamEngine spam = new SpamEngine();
        private readonly AutoChestEngine chest = new AutoChestEngine();
        private readonly System.Windows.Forms.Timer uiTimer;

        private NotifyIcon tray;
        private volatile IntPtr gameHwnd = IntPtr.Zero;
        private Point offset = new Point(0, -36);   // 开关相对气泡锚点（气泡左上角）的偏移：气泡左上、上方 8px
        private Point lastBubbleAnchor = Point.Empty;
        private Rectangle lastPanelRect = Rectangle.Empty;
        private Point lastWinPoint = Point.Empty;
        private bool lastGameRunning;
        private AutomationElement cachedDoc;
        private IntPtr cachedDocHwnd = IntPtr.Zero;
        private volatile AutomationElement[] cachedBubbles;
        private IntPtr cachedBubblesHwnd = IntPtr.Zero;
        private DateTime lastBubbleRefresh = DateTime.MinValue;
        private int petDx;
        private int petDy;
        private long fastFollowUntilTicks;
        private int gameLostCount;
        private bool loggedFindMiss;
        private volatile bool userHidden;
        private readonly string settingsPath = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "plugin.settings.txt");

        private bool dragging;
        private bool dragMoved;
        private Point dragStart;
        private Point dragWindowStart;
        // 点击状态（轮询线程统一处理开/关，窗口消息只负责拖动）
        private volatile bool pressActive;
        private bool pressWasDown = true;
        private bool pressWasRunning;
        private int pressStartX, pressStartY;
        private bool pressDragging;
        private int tickCounter;

        public PluginForm()
        {
            Text = "WuwaTappo 助手";
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            TopMost = true;
            ShowInTaskbar = false;
            BackColor = CardColor;
            Size = new Size(CardW, CardH);
            Width = CardW;
            Height = CardH;
            StartPosition = FormStartPosition.Manual;
            Location = new Point(Screen.PrimaryScreen.WorkingArea.Right - CardW - 100, 60);
            DoubleBuffered = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);

            // 圆角窗口
            try
            {
                int r = 12;
                IntPtr hRgn = CreateRoundRectRgn(0, 0, CardW, CardH, r, r);
                Region = Region.FromHrgn(hRgn);
                DeleteObject(hRgn);
            }
            catch { }

            MouseDown += OnDown;
            MouseMove += OnMove;
            MouseUp += OnUp;
            Paint += OnPaint;

            FormClosing += delegate(object s, FormClosingEventArgs e)
            {
                if (e.CloseReason == CloseReason.UserClosing)
                {
                    e.Cancel = true;
                    HideToTray();
                }
            };

            tray = new NotifyIcon
            {
                Icon = SystemIcons.Application,
                Text = "WuwaTappo 助手",
                Visible = true
            };
            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Items.Add("显示/隐藏开关 (Ctrl+Alt+H)", null, delegate { ToggleVisibleByHotkey(); });
            menu.Items.Add("停止连点", null, delegate { StopSpamByUser(); });
            menu.Items.Add("退出", null, delegate
            {
                spam.Stop();
                tray.Visible = false;
                tray.Dispose();
                Application.Exit();
            });
            tray.ContextMenuStrip = menu;
            tray.DoubleClick += delegate { ShowForm(); };

            chest.Start();
            chest.SetSpam = delegate(bool on)
            {
                if (on) spam.Start(); else spam.Stop();
            };
            chest.OnChestOpened = delegate
            {
                // 领取宝箱后游戏会重建气泡元素，立即丢弃缓存以便刷新到新位置
                cachedBubbles = null;
            };
            LoadSettings();
            StartClickWatcher();
            Log.Write("插件启动（连点固定 " + spam.Rate + " 次/秒，强制终止热键 Ctrl+Alt+Q）");

            uiTimer = new System.Windows.Forms.Timer { Interval = 20 };
            uiTimer.Tick += delegate { RefreshUi(); };
            uiTimer.Start();
        }

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateRoundRectRgn(int x1, int y1, int x2, int y2, int w, int h);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try
            {
                if (Native.RegisterHotKey(Handle, 1, Native.MOD_CONTROL | Native.MOD_ALT, Native.VK_Q))
                    Log.Write("已注册强制终止热键 Ctrl+Alt+Q");
                else
                    Log.Write("强制终止热键注册失败（Ctrl+Alt+Q 可能被占用）");
                if (Native.RegisterHotKey(Handle, 2, Native.MOD_CONTROL | Native.MOD_ALT, Native.VK_H))
                    Log.Write("已注册显示/隐藏热键 Ctrl+Alt+H");
                else
                    Log.Write("显示/隐藏热键注册失败（Ctrl+Alt+H 可能被占用）");
            }
            catch (Exception ex)
            {
                Log.Write("注册热键异常: " + ex.Message);
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_HOTKEY && m.WParam.ToInt64() == 1)
            {
                StopSpamByUser();
                Log.Write("【强制终止】热键 Ctrl+Alt+Q 已停止连点");
                return;
            }
            if (m.Msg == Native.WM_HOTKEY && m.WParam.ToInt64() == 2)
            {
                ToggleVisibleByHotkey();
                return;
            }
            if (m.Msg == Native.WM_MOUSEACTIVATE)
            {
                m.Result = (IntPtr)Native.MA_ACTIVATE;
                return;
            }
            base.WndProc(ref m);
        }

        private void ToggleVisibleByHotkey()
        {
            try
            {
                if (Visible)
                {
                    userHidden = true;
                    Hide();
                    Log.Write("【热键】隐藏开关");
                }
                else
                {
                    userHidden = false;
                    Show();
                    Activate();
                    BringToFront();
                    Native.SetWindowPos(Handle, Native.HWND_TOPMOST, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);
                    Log.Write("【热键】显示开关");
                }
            }
            catch (Exception ex)
            {
                Log.Write("切换显示异常: " + ex.Message);
            }
        }

        // ---------------- 绘制 ----------------
        private void OnPaint(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;

            bool on = spam.IsRunning;
            Rectangle track = new Rectangle(TrackX, TrackY, TrackW, TrackH);
            int radius = TrackH / 2;

            // 轨道
            using (GraphicsPath path = RoundRect(track, radius))
            {
                using (SolidBrush b = new SolidBrush(on ? TrackOnColor : TrackOffColor))
                    g.FillPath(b, path);
                using (Pen p = new Pen(on ? TrackOnBorder : TrackOffBorder, 1.5f))
                    g.DrawPath(p, path);
            }

            // 滑块
            int knobX = on ? (track.Right - KnobD - 2) : (track.Left + 2);
            int knobY = track.Top + (track.Height - KnobD) / 2;
            Rectangle knob = new Rectangle(knobX, knobY, KnobD, KnobD);
            using (SolidBrush b = new SolidBrush(KnobColor))
                g.FillEllipse(b, knob);
            using (Pen p = new Pen(Color.FromArgb(60, 220, 200, 170), 1f))
                g.DrawEllipse(p, knob);
        }

        private static GraphicsPath RoundRect(Rectangle r, int radius)
        {
            int d = radius * 2;
            GraphicsPath path = new GraphicsPath();
            path.AddArc(r.Left, r.Top, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        // ---------------- 交互：点击切换 / 拖动移动 ----------------
        private void OnDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            dragging = true;
            dragMoved = false;
            dragStart = Cursor.Position;
            dragWindowStart = Location;
        }

        private void OnMove(object sender, MouseEventArgs e)
        {
            if (!dragging) return;
            Point cur = Cursor.Position;
            if (Math.Abs(cur.X - dragStart.X) > 4 || Math.Abs(cur.Y - dragStart.Y) > 4)
                dragMoved = true;
            if (dragMoved)
                Location = new Point(dragWindowStart.X + (cur.X - dragStart.X), dragWindowStart.Y + (cur.Y - dragStart.Y));
        }

        private void OnUp(object sender, MouseEventArgs e)
        {
            if (!dragging) return;
            dragging = false;
            if (dragMoved) UpdateOffsetFromLocation();
        }

        // ---------------- 连点开关 ----------------
        // 开关切换完全由下面的轮询线程负责（按下瞬间生效，与窗口消息无关），
        // 窗口消息只用于拖动窗口，因此不会出现"一次点击被识别成两次"。
        private void ToggleNow()
        {
            if (spam.IsRunning)
            {
                spam.Stop();
                chest.ResetAutoSpam();
                Log.Write("【点击开关】已停止连点");
            }
            else
            {
                spam.Start();
                Log.Write("连点开启（固定 " + spam.Rate + " 次/秒）");
            }
            try { BeginInvoke(new Action(delegate { Invalidate(); })); } catch { }
        }

        private void RollbackPress()
        {
            if (pressWasRunning)
            {
                if (!spam.IsRunning) spam.Start();
            }
            else
            {
                if (spam.IsRunning) spam.Stop();
            }
            try { BeginInvoke(new Action(delegate { Invalidate(); })); } catch { }
            Log.Write("拖动中：连点状态已还原");
        }

        private void StopSpamByUser()
        {
            if (!spam.IsRunning) return;
            spam.Stop();
            chest.ResetAutoSpam();
            Log.Write("连点停止");
            try { BeginInvoke(new Action(delegate { Invalidate(); })); } catch { }
        }

        private bool InToggleWindow(int x, int y)
        {
            try
            {
                Native.RECT r;
                if (!Native.GetWindowRect(Handle, out r)) return false;
                // 窗口矩形可能大于可视区（Region 裁剪），按可视尺寸 CardW x CardH 判断
                return x >= r.Left && x <= r.Left + CardW && y >= r.Top && y <= r.Top + CardH;
            }
            catch
            {
                return false;
            }
        }

        // ---------------- 点击监听 ----------------
        // 连点期间大量注入键盘事件会挤占系统输入队列，导致窗口消息延迟，
        // 所以开关切换用 GetAsyncKeyState 在"按下瞬间"处理，无需等待消息；
        // 若按下后光标移动（拖动窗口），则回滚该次切换。
        private void StartClickWatcher()
        {
            Thread t = new Thread(ClickWatchLoop);
            t.IsBackground = true;
            t.Start();
        }

        private void ClickWatchLoop()
        {
            bool wasDown = true;
            while (true)
            {
                Thread.Sleep(8);
                try
                {
                    bool down = (Native.GetAsyncKeyState(Native.VK_LBUTTON) & 0x8000) != 0;
                    Native.POINT p;
                    Native.GetCursorPos(out p);
                    if (down && !wasDown)
                    {
                        // 按下沿：在开关上按下 → 立即切换
                        if (InToggleWindow(p.X, p.Y))
                        {
                            pressActive = true;
                            pressDragging = false;
                            pressWasRunning = spam.IsRunning;
                            pressStartX = p.X;
                            pressStartY = p.Y;
                            ToggleNow();
                        }
                    }
                    if (down && pressActive && !pressDragging)
                    {
                        if (Math.Abs(p.X - pressStartX) > 8 || Math.Abs(p.Y - pressStartY) > 8)
                        {
                            pressDragging = true;
                            RollbackPress();
                        }
                    }
                    // 拖动桌宠期间（气泡位置刚变化过）：用鼠标位置快速跟随，消除 UIA 采样延迟
                    if (down && !pressActive && DateTime.Now.Ticks < fastFollowUntilTicks)
                    {
                        int tx = p.X + petDx + offset.X;
                        int ty = p.Y + petDy + offset.Y;
                        try
                        {
                            BeginInvoke(new Action(delegate
                            {
                                if (!dragging) Location = new Point(tx, ty);
                            }));
                        }
                        catch { }
                    }
                    if (!down && wasDown)
                    {
                        pressActive = false;
                        pressDragging = false;
                    }
                    wasDown = down;
                }
                catch { }
            }
        }

        // ---------------- 位置：物理写死相对世界（相对游戏窗口，不随 UI 状态跳动） ----------------
        private void RefreshUi()
        {
            UpdatePositionFromGame();
            tickCounter++;
            if (tickCounter % 25 == 0)
            {
                // 每 ~500ms 保持置顶（防止被游戏的置顶窗口盖住）
                try { Native.SetWindowPos(Handle, Native.HWND_TOPMOST, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010); } catch { }
            }
            if (tickCounter % 100 == 0) UpdateTrayText();
        }

        private void UpdatePositionFromGame()
        {
            if (dragging || pressDragging) return;
            IntPtr game = FindGameWindow();
            if (game == IntPtr.Zero)
            {
                // 防抖动：连续确认游戏不在（~2 秒）后，插件完全退出进程
                gameLostCount++;
                if (gameLostCount >= 100 && lastGameRunning)
                {
                    Log.Write("游戏已退出，插件进程退出");
                    try
                    {
                        tray.Visible = false;
                        tray.Dispose();
                    }
                    catch { }
                    Application.Exit();
                }
                return;
            }
            gameLostCount = 0;
            if (!lastGameRunning)
            {
                // 游戏启动/重启 → 显示开关并强制置顶（用户主动隐藏时除外）
                lastGameRunning = true;
                if (!Visible && !userHidden)
                {
                    Show();
                    Activate();
                    BringToFront();
                    Native.SetWindowPos(Handle, Native.HWND_TOPMOST, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);
                    Log.Write("游戏已运行，显示开关");
                }
            }

            // 主锚点：倒计时气泡（随桌宠移动），宝箱就绪时气泡消失则保持位置不动
            Rectangle? bubble = CountdownAnchor(game);
            if (bubble.HasValue)
            {
                Point anchor = new Point(bubble.Value.Left, bubble.Value.Top);
                if (lastBubbleAnchor == Point.Empty || anchor != lastBubbleAnchor)
                {
                    Point target = new Point(anchor.X + offset.X, anchor.Y + offset.Y);
                    if (InScreen(target)) Location = target;
                    // 气泡位置变化 → 记录"光标→锚点"偏移，进入快速跟随窗口：
                    // 拖动桌宠期间用鼠标位置(8ms)跟随，消除 UIA 采样延迟
                    if (lastBubbleAnchor != Point.Empty)
                    {
                        Native.POINT cur;
                        if (Native.GetCursorPos(out cur))
                        {
                            petDx = anchor.X - cur.X;
                            petDy = anchor.Y - cur.Y;
                            fastFollowUntilTicks = DateTime.Now.AddMilliseconds(600).Ticks;
                        }
                    }
                }
                lastBubbleAnchor = anchor;
                lastPanelRect = Rectangle.Empty;
                return;
            }

            // 无气泡：已经定位过就保持不动
            if (lastBubbleAnchor != Point.Empty) return;

            // 首次定位兜底：面板/窗口/屏幕安全位置
            Rectangle? panel = AnchorReader.Read(game);
            if (panel.HasValue)
            {
                Point target = new Point(panel.Value.Left + offset.X, panel.Value.Top + offset.Y);
                if (InScreen(target)) Location = target;
                lastPanelRect = panel.Value;
                return;
            }
            Native.RECT wr;
            if (Native.GetWindowRect(game, out wr))
            {
                Point target = new Point(wr.Left + offset.X, wr.Top + offset.Y);
                if (InScreen(target))
                {
                    Location = target;
                    lastWinPoint = new Point(wr.Left, wr.Top);
                    return;
                }
            }
            // 最终兜底：屏幕右侧安全位置（避免跑到屏幕外看不到）
            Rectangle work = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(work.Right - CardW - 80, work.Top + 40);
        }

        private bool InScreen(Point p)
        {
            Rectangle vs = SystemInformation.VirtualScreen;
            return p.X >= vs.Left && p.Y >= vs.Top && p.X <= vs.Right - CardW && p.Y <= vs.Bottom - CardH;
        }

        private Rectangle? CountdownAnchor(IntPtr gameHwnd)
        {
            AutomationElement doc = GetDoc(gameHwnd);
            if (doc == null) return null;
            AutomationElement[] bubbles = cachedBubbles;
            bool needRefresh = bubbles == null
                || cachedBubblesHwnd != gameHwnd
                || (bubbles.Length == 0 && (DateTime.Now - lastBubbleRefresh).TotalSeconds > 1.5)
                || (DateTime.Now - lastBubbleRefresh).TotalSeconds > 3;
            if (needRefresh)
            {
                RefreshBubbleCache(doc, gameHwnd);
                bubbles = cachedBubbles;
            }
            if (bubbles == null || bubbles.Length == 0) return null;
            Rectangle? best = null;
            foreach (AutomationElement el in bubbles)
            {
                try
                {
                    System.Windows.Rect r = el.Current.BoundingRectangle;
                    if (r.IsEmpty || r.Width <= 0 || r.Height <= 0) continue;
                    if (r.X == 0 && r.Y == 0) continue;                    // 无效位置
                    if (r.X < 0 || r.Y < 0 || r.X > 8000 || r.Y > 8000) continue;
                    Rectangle rect = new Rectangle((int)r.X, (int)r.Y, (int)r.Width, (int)r.Height);
                    if (!best.HasValue || rect.Top < best.Value.Top) best = rect;
                }
                catch
                {
                    cachedBubbles = null;   // 元素失效 → 下次刷新
                    return null;
                }
            }
            return best;
        }

        private void RefreshBubbleCache(AutomationElement doc, IntPtr gameHwnd)
        {
            try
            {
                AutomationElementCollection imgs = doc.FindAll(TreeScope.Descendants,
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Image));
                var list = new System.Collections.Generic.List<AutomationElement>();
                foreach (AutomationElement el in imgs)
                {
                    try
                    {
                        string nm = el.Current.Name ?? "";
                        if (Regex.IsMatch(nm, @"^\d{1,2}:\d{2}$")) list.Add(el);
                    }
                    catch { }
                }
                cachedBubbles = list.ToArray();
                cachedBubblesHwnd = gameHwnd;
                lastBubbleRefresh = DateTime.Now;
            }
            catch
            {
                cachedBubbles = null;
            }
        }

        private AutomationElement GetDoc(IntPtr game)
        {
            if (cachedDoc != null && cachedDocHwnd == game) return cachedDoc;
            try
            {
                AutomationElement root = AutomationElement.FromHandle(game);
                AutomationElement doc = root.FindFirst(TreeScope.Descendants,
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Document));
                if (doc != null)
                {
                    cachedDoc = doc;
                    cachedDocHwnd = game;
                }
                return doc;
            }
            catch
            {
                return null;
            }
        }

        private void UpdateOffsetFromLocation()
        {
            if (lastBubbleAnchor != Point.Empty)
            {
                offset = new Point(Location.X - lastBubbleAnchor.X, Location.Y - lastBubbleAnchor.Y);
                SaveSettings();
                return;
            }
            if (lastPanelRect != Rectangle.Empty)
            {
                offset = new Point(Location.X - lastPanelRect.Left, Location.Y - lastPanelRect.Top);
                SaveSettings();
                return;
            }
            IntPtr game = FindGameWindow();
            if (game == IntPtr.Zero) return;
            Native.RECT wr;
            if (!Native.GetWindowRect(game, out wr)) return;
            offset = new Point(Location.X - wr.Left, Location.Y - wr.Top);
            SaveSettings();
        }

        private void LoadSettings()
        {
            try
            {
                if (!File.Exists(settingsPath)) return;
                string[] parts = File.ReadAllText(settingsPath).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2)
                {
                    // 新格式：相对气泡锚点的偏移
                    offset = new Point(int.Parse(parts[0].Trim()), int.Parse(parts[1].Trim()));
                }
                // 旧格式（3 段）忽略，使用默认偏移
            }
            catch { }
        }

        private void SaveSettings()
        {
            try
            {
                File.WriteAllText(settingsPath, offset.X + "," + offset.Y);
            }
            catch { }
        }

        private IntPtr FindGameWindow()
        {
            return GameWindowFinder.Find();
        }

        private void UpdateTrayText()
        {
            try { tray.Text = "WuwaTappo 助手"; } catch { }
        }

        private void HideToTray()
        {
            Hide();
            Log.Write("窗口最小化到托盘");
        }

        private void ShowForm()
        {
            userHidden = false;
            Show();
            Activate();
            BringToFront();
        }
    }

    internal static class Program
    {
        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        [STAThread]
        private static void Main()
        {
            try { SetProcessDPIAware(); } catch { }
            try { Native.timeBeginPeriod(1); } catch { }
            bool created;
            Mutex mutex = new Mutex(true, "WuwaTappoPlugin_SingleInstance", out created);
            for (int attempt = 0; !created && attempt < 12; attempt++)
            {
                // 旧实例可能正在退出（关闭后马上重新打开）——等待它释放后再接管
                Thread.Sleep(250);
                mutex.Dispose();
                mutex = new Mutex(true, "WuwaTappoPlugin_SingleInstance", out created);
            }
            using (mutex)
            {
                if (!created)
                {
                    // 等待后仍被占用：唤起已有实例的窗口
                    IntPtr old = Native.FindWindow(null, "WuwaTappo 助手");
                    if (old != IntPtr.Zero)
                    {
                        Native.ShowWindow(old, 9);
                        Native.SetForegroundWindow(old);
                    }
                    return;
                }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new PluginForm());
            }
        }
    }
}
