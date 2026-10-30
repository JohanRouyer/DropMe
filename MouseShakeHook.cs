using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;

namespace DropMe;

/// <summary>
/// Global low-level mouse hook that fires <see cref="Shaken"/> when the mouse is
/// swung left/right several times quickly while the left button is held (i.e. during a drag).
/// </summary>
internal sealed class MouseShakeHook : IDisposable
{
    private const int WH_MOUSE_LL = 14;
    private const int WM_MOUSEMOVE = 0x0200;
    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_LBUTTONUP = 0x0202;

    private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT
    {
        public POINT pt;
        public uint mouseData, flags, time;
        public IntPtr dwExtraInfo;
    }

    private static readonly int PointXOffset = (int)Marshal.OffsetOf<MSLLHOOKSTRUCT>(nameof(MSLLHOOKSTRUCT.pt));
    private static readonly int TimeOffset = (int)Marshal.OffsetOf<MSLLHOOKSTRUCT>(nameof(MSLLHOOKSTRUCT.time));

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll")]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam, lParam;
        public uint time;
        public POINT pt;
    }

    [DllImport("user32.dll")]
    private static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    private static extern bool PostThreadMessage(uint idThread, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    private const uint WM_QUIT = 0x0012;

    /// <summary>Distance the cursor must travel back before a direction change counts.</summary>
    public int MinSwingPixels { get; set; } = 40;
    /// <summary>Number of direction changes needed to count as a shake.</summary>
    public int RequiredReversals { get; set; } = 4;
    /// <summary>Time window in which those direction changes must happen.</summary>
    public uint WindowMs { get; set; } = 800;
    public uint CooldownMs { get; set; } = 1000;

    public event Action? Shaken;
    public event Action? ButtonReleased;

    // Kept in a field so the GC never collects the delegate while Windows still calls it.
    private readonly LowLevelMouseProc _proc;
    private IntPtr _hook;
    private Thread? _thread;
    private uint _threadId;

    private bool _buttonDown;
    private int _direction;   // -1 left, +1 right, 0 not yet known
    private int _anchorX;     // start position before a direction is known
    private int _peakX;       // furthest point reached in the current direction
    private readonly Queue<uint> _reversals = new();
    private uint _lastTrigger;

    public MouseShakeHook() => _proc = HookProc;

    /// <summary>
    /// Installs the hook on a dedicated thread. Windows holds every mouse event until the hook
    /// returns, so it must never share a thread with the UI, which can be busy for a moment.
    /// Events are raised on that thread.
    /// </summary>
    public void Start()
    {
        using var ready = new ManualResetEventSlim();
        Exception? error = null;

        _thread = new Thread(() =>
        {
            _threadId = GetCurrentThreadId();
            _hook = SetWindowsHookEx(WH_MOUSE_LL, _proc, GetModuleHandle(null), 0);
            if (_hook == IntPtr.Zero)
                error = new Win32Exception(Marshal.GetLastWin32Error());
            ready.Set();
            if (error != null)
                return;

            while (GetMessage(out _, IntPtr.Zero, 0, 0) > 0)
            {
            }
            UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        })
        {
            IsBackground = true,
            Name = "DropMe mouse hook",
            Priority = ThreadPriority.AboveNormal,
        };
        _thread.Start();
        ready.Wait();
        if (error != null)
            throw error;
    }

    public void Dispose()
    {
        if (_threadId != 0)
        {
            PostThreadMessage(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
            _thread?.Join(500);
            _threadId = 0;
        }
    }

    private IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        // Runs for every mouse event on the system: read only the fields needed, and only when needed.
        if (nCode >= 0)
        {
            switch ((int)wParam)
            {
                case WM_LBUTTONDOWN:
                    _buttonDown = true;
                    Reset(Marshal.ReadInt32(lParam, PointXOffset));
                    break;
                case WM_LBUTTONUP:
                    _buttonDown = false;
                    ButtonReleased?.Invoke();
                    break;
                case WM_MOUSEMOVE when _buttonDown:
                    Track(Marshal.ReadInt32(lParam, PointXOffset), unchecked((uint)Marshal.ReadInt32(lParam, TimeOffset)));
                    break;
            }
        }
        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    private void Reset(int x)
    {
        _direction = 0;
        _anchorX = x;
        _peakX = x;
        _reversals.Clear();
    }

    private void Track(int x, uint time)
    {
        if (_direction == 0)
        {
            if (Math.Abs(x - _anchorX) >= MinSwingPixels)
            {
                _direction = Math.Sign(x - _anchorX);
                _peakX = x;
            }
            return;
        }

        // Extend the current swing, or register a reversal once the cursor has come back far enough.
        if ((x - _peakX) * _direction > 0)
        {
            _peakX = x;
            return;
        }
        if (Math.Abs(x - _peakX) < MinSwingPixels)
            return;

        _direction = -_direction;
        _peakX = x;
        _reversals.Enqueue(time);
        while (_reversals.Count > 0 && unchecked(time - _reversals.Peek()) > WindowMs)
            _reversals.Dequeue();

        if (_reversals.Count >= RequiredReversals && unchecked(time - _lastTrigger) > CooldownMs)
        {
            _lastTrigger = time;
            _reversals.Clear();
            Shaken?.Invoke();
        }
    }
}
