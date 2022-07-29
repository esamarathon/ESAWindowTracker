using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ESAWindowTracker
{
    internal class WindowTracker
    {
        public static void Register(IServiceCollection services)
        {
            services.AddTransient<WindowTracker>();
        }

        private readonly ILogger<WindowTracker> logger;
        private readonly IOptionsSnapshot<Config> options;

        public WindowTracker(ILogger<WindowTracker> logger, IOptionsSnapshot<Config> options)
        {
            this.logger = logger;
            this.options = options;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [DllImport("user32.dll", SetLastError = true)]
        static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll", SetLastError = true)]
        static extern bool GetClientRect(IntPtr hwnd, out RECT lpRect);
        [DllImport("user32.dll", SetLastError = true)]
        static extern bool ClientToScreen(IntPtr hWnd, ref System.Drawing.Point lpPoint);
        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern int GetWindowTextLength(IntPtr hWnd);
        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        public ResponseMsg? GetCurrentInfo()
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero)
            {
                logger.LogError("Failed getting foreground window.");
                return null;
            }

            if (!GetClientRect(hwnd, out RECT clientrect))
            {
                logger.LogError("Failed getting client rect.");
                return null;
            }

            Point top_left = new Point(clientrect.Left, clientrect.Top);
            Point bottom_right = new Point(clientrect.Right, clientrect.Bottom);

            if (!ClientToScreen(hwnd, ref top_left))
            {
                logger.LogError("Failed converting client to screen.");
                return null;
            }
            if (!ClientToScreen(hwnd, ref bottom_right))
            {
                logger.LogError("Failed converting client to screen.");
                return null;
            }

            StringBuilder titleBuilder = new StringBuilder(GetWindowTextLength(hwnd) + 1);
            if (GetWindowText(hwnd, titleBuilder, titleBuilder.Capacity) <= 0)
            {
                logger.LogError("Failed getting Window Title.");
                return null;
            }

#if DEBUG
            logger.LogInformation($"Rect for {titleBuilder}: {top_left.X},{top_left.Y},{bottom_right.X},{bottom_right.Y}");
#endif

            var opts = options.Value;

            return new ResponseMsg
            {
                PCID = opts.PCID,
                Eventshort = opts.EventShort,

                WindowTitle = titleBuilder.ToString(),

                WindowLeft = top_left.X,
                WindowTop = top_left.Y,

                WindowRight = bottom_right.X,
                WindowBottom = bottom_right.Y
            };
        }
    }
}
