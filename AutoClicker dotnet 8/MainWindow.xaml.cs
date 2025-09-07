using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace AutoClicker
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private const int HOTKEY_ID = 9000;
        private IntPtr _windowHandle;
        private HwndSource _source;
        private CancellationTokenSource _cts;
        private bool _isRunning = false;

        private const uint MOD_ALT = 0x0001;
        private const uint MOD_CONTROL = 0x0002;
        private const uint MOD_SHIFT = 0x0004;
        private const uint WM_HOTKEY = 0x0312;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, UIntPtr dwExtraInfo);

        private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        private const uint MOUSEEVENTF_LEFTUP = 0x0004;
        private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
        private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
        private const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
        private const uint MOUSEEVENTF_MIDDLEUP = 0x0040;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnregisterHotKey(IntPtr hwnd, int id);

        public MainWindow()
        {
            InitializeComponent();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            _windowHandle = new WindowInteropHelper(this).Handle;
            _source = HwndSource.FromHwnd(_windowHandle);
            _source.AddHook(HwndHook);

            CmbButton.ItemsSource = new List<string> { "Left", "Right", "Middle" };
            CmbButton.SelectedIndex = 0;

            var keys = new List<Key>
            {
                Key.F1, Key.F2, Key.F3, Key.F4, Key.F5, Key.F6,
                Key.F7, Key.F8, Key.F9, Key.F10, Key.F11, Key.F12,
            };
            for (char c = 'A'; c <= 'Z'; c++)
                keys.Add((Key)Enum.Parse(typeof(Key), c.ToString()));

            CmbHotkeyKey.ItemsSource = keys;
            CmbHotkeyKey.SelectedItem = Key.F6;
            ChkCtrl.IsChecked = true;

            RegisterHotKeyFromUI();
        }
        private void BtnStartStop_Click(object sender, RoutedEventArgs e) => ToggleStartStop();

        private void BtnApplyHotkey_Click(object sender, RoutedEventArgs e) => RegisterHotKeyFromUI();

        private bool RegisterHotKeyFromUI()
        {
            UnregisterHotKey();

            uint modifiers = 0;
            if (ChkAlt.IsChecked == true) modifiers |= MOD_ALT;
            if (ChkCtrl.IsChecked == true) modifiers |= MOD_CONTROL;
            if (ChkShift.IsChecked == true) modifiers |= MOD_SHIFT;

            if (CmbHotkeyKey.SelectedItem == null)
            {
                MessageBox.Show("Select a hotkey first.");
                return false;
            }

            Key selectedKey = (Key)CmbHotkeyKey.SelectedItem;
            uint vk = (uint)KeyInterop.VirtualKeyFromKey(selectedKey);

            bool ok = RegisterHotKey(_windowHandle, HOTKEY_ID, modifiers, vk);
            if (!ok)
            {
                MessageBox.Show("Failed to register hotkey. It might be already in use.");
                TxtStatus.Text = "Hotkey registration failed.";
                return false;
            }

            TxtStatus.Text = $"Hotkey set ({(ChkCtrl.IsChecked == true ? "Ctrl+" : "")}{(ChkAlt.IsChecked == true ? "Alt+" : "")}{(ChkShift.IsChecked == true ? "Shift+" : "")}{selectedKey})";
            return true;
        }

        private void UnregisterHotKey()
        {
            try { UnregisterHotKey(_windowHandle, HOTKEY_ID); }
            catch { }
        }

        private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_HOTKEY)
            {
                int id = wParam.ToInt32();
                if (id == HOTKEY_ID)
                {
                    Dispatcher.Invoke(ToggleStartStop);
                    handled = true;
                }
            }
            return IntPtr.Zero;
        }

        private void ToggleStartStop()
        {
            if (_isRunning) StopClicking();
            else StartClicking();
        }

        private async Task StartClicking()
        {
            if (_isRunning) return;

            if (!int.TryParse(TxtInterval.Text, out int interval) || interval < 1)
            {
                MessageBox.Show("Please enter a valid interval (>= 1 ms).");
                return;
            }

            string button = (string)CmbButton.SelectedItem;
            bool hold = ChkHold.IsChecked == true;

            _cts = new CancellationTokenSource();
            CancellationToken token = _cts.Token;
            _isRunning = true;
            BtnStartStop.Content = "Stop";
            TxtStatus.Text = $"Auto-clicker running ({button})";

            BtnStartStop.IsEnabled = false;
            await Task.Delay(2000);
            BtnStartStop.IsEnabled = true;

            Task.Run(async () =>
            {
                try
                {
                    while (!token.IsCancellationRequested)
                    {
                        DoClick(button, hold);
                        await Task.Delay(interval, token);
                    }
                }
                catch (TaskCanceledException) { }
                finally
                {
                    Dispatcher.Invoke(() =>
                    {
                        _isRunning = false;
                        BtnStartStop.Content = "Start";
                        TxtStatus.Text = "Auto-clicker stopped.";
                    });
                }
            }, token);
        }

        private void StopClicking()
        {
            if (!_isRunning) return;
            _cts.Cancel();
        }

        private void DoClick(string button, bool hold)
        {
            switch (button)
            {
                case "Left":
                    mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
                    if (!hold) mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);
                    else Thread.Sleep(50);
                    break;

                case "Right":
                    mouse_event(MOUSEEVENTF_RIGHTDOWN, 0, 0, 0, UIntPtr.Zero);
                    if (!hold) mouse_event(MOUSEEVENTF_RIGHTUP, 0, 0, 0, UIntPtr.Zero);
                    else Thread.Sleep(50);
                    break;

                case "Middle":
                    mouse_event(MOUSEEVENTF_MIDDLEDOWN, 0, 0, 0, UIntPtr.Zero);
                    if (!hold) mouse_event(MOUSEEVENTF_MIDDLEUP, 0, 0, 0, UIntPtr.Zero);
                    else Thread.Sleep(50);
                    break;
            }
            Thread.Sleep(10);
        }
        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            UnregisterHotKey();
            if (_source != null) _source.RemoveHook(HwndHook);
            if (_cts != null) _cts.Cancel();
        }
    }
}
