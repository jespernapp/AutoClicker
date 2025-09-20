// MainWindow.xaml.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Input;
using Gma.System.MouseKeyHook; // NuGet: Gma.System.MouseKeyHook
using System.Text.Json;
using Microsoft.Win32;
using System.Windows.Forms;

namespace AutoClicker
{
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

        [DllImport("user32.dll")]
        private static extern bool SetCursorPos(int X, int Y);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint fsModifiers, uint vk);
        private const int HOTKEY_ID_RECORD = 9001; // For recording (Ctrl+Alt+R)
        private const int HOTKEY_ID_PLAY = 9002;   // For playback (Ctrl+Alt+P)

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnregisterHotKey(IntPtr hwnd, int id);

        // recorder/playback fields
        private IKeyboardMouseEvents _globalHook;
        private List<MacroEvent> _macroEvents = new List<MacroEvent>();
        private DateTime _recordStart;
        private CancellationTokenSource _playbackCts;

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

            //Hotkey for starting/stopping recording (Ctrl+Alt+R)
            RegisterHotKey(_windowHandle, HOTKEY_ID_RECORD, MOD_CONTROL | MOD_ALT, (uint)KeyInterop.VirtualKeyFromKey(Key.R));
            RegisterHotKey(_windowHandle, HOTKEY_ID_PLAY, MOD_CONTROL | MOD_ALT, (uint)KeyInterop.VirtualKeyFromKey(Key.P));

            // initialize macro UI state (if these controls exist in XAML)
            try
            {
                BtnStop.IsEnabled = false;
                BtnPlay.IsEnabled = false;
            }
            catch { /* ignore if controls missing */ }
        }

        private void BtnStartStop_Click(object sender, RoutedEventArgs e) => ToggleStartStop();
        private void BtnApplyHotkey_Click(object sender, RoutedEventArgs e) => RegisterHotKeyFromUI();

        // Replace all ambiguous usages of MessageBox.Show with System.Windows.MessageBox.Show

        private bool RegisterHotKeyFromUI()
        {
            UnregisterHotKey();

            uint modifiers = 0;
            if (ChkAlt.IsChecked == true) modifiers |= MOD_ALT;
            if (ChkCtrl.IsChecked == true) modifiers |= MOD_CONTROL;
            if (ChkShift.IsChecked == true) modifiers |= MOD_SHIFT;

            if (CmbHotkeyKey.SelectedItem == null)
            {
                System.Windows.MessageBox.Show("Select a hotkey first.");
                return false;
            }

            Key selectedKey = (Key)CmbHotkeyKey.SelectedItem;
            uint vk = (uint)KeyInterop.VirtualKeyFromKey(selectedKey);

            bool ok = RegisterHotKey(_windowHandle, HOTKEY_ID, modifiers, vk);
            if (!ok)
            {
                System.Windows.MessageBox.Show("Failed to register hotkey. It might be already in use.");
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
                else if (id == HOTKEY_ID_RECORD)
                {
                    if (BtnRecord.IsEnabled)
                    {
                        Dispatcher.Invoke(() => BtnRecord_Click(BtnRecord, null));
                        handled = true;
                    }
                    else
                    {
                        Dispatcher.Invoke(() => BtnStop_Click(BtnStop, null));
                        handled = true;
                    }
                }
                else if (id == HOTKEY_ID_PLAY)
                {
                    if (BtnPlay.IsEnabled)
                    {
                        Dispatcher.Invoke(() => BtnPlay_Click(BtnPlay, null));
                        handled = true;
                    }
                    else
                    {
                        Dispatcher.Invoke(() => BtnStop_Click(BtnStop, null));
                        handled = true;
                    }
                }
            }
            return IntPtr.Zero;
        }

        private void ToggleStartStop()
        {
            if (_isRunning) StopClicking();
            else _ = StartClicking();
        }

        private async Task StartClicking()
        {
            if (_isRunning) return;

            if (!int.TryParse(TxtInterval.Text, out int interval) || interval < 1)
            {
                System.Windows.MessageBox.Show("Please enter a valid interval (>= 1 ms).");
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
                        DoClickInternal(button, hold);
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

        private void DoClickInternal(string button, bool hold)
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

        // --- Recorder / Playback utilities ---

        private string FormatEvent(MacroEvent ev) =>
            $"{(ev.IsDown ? "Down" : "Up")} {ev.Button} at ({ev.X},{ev.Y}) +{ev.DelayMs} ms";

        private void GlobalHook_MouseDownExt(object sender, MouseEventExtArgs e)
        {
            var ev = new MacroEvent
            {
                DelayMs = (int)(DateTime.UtcNow - _recordStart).TotalMilliseconds,
                X = e.X,
                Y = e.Y,
                Button = e.Button.ToString(),
                IsDown = true
            };
            _macroEvents.Add(ev);
            Dispatcher.Invoke(() =>
            {
                try { LstEvents.Items.Add(FormatEvent(ev)); } catch { }
            });
        }

        private void GlobalHook_MouseUpExt(object sender, MouseEventExtArgs e)
        {
            var ev = new MacroEvent
            {
                DelayMs = (int)(DateTime.UtcNow - _recordStart).TotalMilliseconds,
                X = e.X,
                Y = e.Y,
                Button = e.Button.ToString(),
                IsDown = false
            };
            _macroEvents.Add(ev);
            Dispatcher.Invoke(() =>
            {
                try { LstEvents.Items.Add(FormatEvent(ev)); } catch { }
            });
        }

        private async void BtnRecord_Click(object sender, RoutedEventArgs e)
        {
            BtnRecord.IsEnabled = false;
            BtnStop.IsEnabled = true;
            BtnPlay.IsEnabled = false;
            try { LstEvents.Items.Clear(); } catch { }
            TxtStatus.Text = "Recording will start in 1.5s. Move to the target window.";
            await Task.Delay(1500);

            _macroEvents.Clear();
            _recordStart = DateTime.UtcNow;
            _globalHook = Hook.GlobalEvents();
            _globalHook.MouseDownExt += GlobalHook_MouseDownExt;
            _globalHook.MouseUpExt += GlobalHook_MouseUpExt;
            TxtStatus.Text = "Recording...";
        }

        private void BtnStop_Click(object sender, RoutedEventArgs e)
        {
            // If recording, stop recording
            if (_globalHook != null)
            {
                _globalHook.MouseDownExt -= GlobalHook_MouseDownExt;
                _globalHook.MouseUpExt -= GlobalHook_MouseUpExt;
                _globalHook.Dispose();
                _globalHook = null;

                TxtStatus.Text = $"Recording stopped. {_macroEvents.Count} events recorded.";
                BtnStop.IsEnabled = false;
                BtnRecord.IsEnabled = true;
                BtnPlay.IsEnabled = _macroEvents.Count > 0;
                return;
            }

            // If playback is running, cancel it
            if (_playbackCts != null)
            {
                _playbackCts.Cancel();
                TxtStatus.Text = "Playback cancelled by user.";
            }
        }

        private async void BtnPlay_Click(object sender, RoutedEventArgs e)
        {
            if (_macroEvents.Count == 0)
            {
                TxtStatus.Text = "No macro recorded/loaded.";
                return;
            }

            BtnPlay.IsEnabled = false;
            BtnRecord.IsEnabled = false;
            BtnStop.IsEnabled = true;
            TxtStatus.Text = "Playing macro...";

            _playbackCts = new CancellationTokenSource();
            var token = _playbackCts.Token;

            try
            {
                int lastMs = 0;
                foreach (var ev in _macroEvents)
                {
                    int wait = ev.DelayMs - lastMs;
                    if (wait > 0) await Task.Delay(wait, token);

                    SetCursorPos(ev.X, ev.Y);
                    if (ev.IsDown) DoMouseDown(ev.Button);
                    else DoMouseUp(ev.Button);

                    lastMs = ev.DelayMs;
                }
                TxtStatus.Text = "Playback finished.";
            }
            catch (TaskCanceledException)
            {
                TxtStatus.Text = "Playback cancelled.";
            }
            finally
            {
                BtnPlay.IsEnabled = true;
                BtnRecord.IsEnabled = true;
                BtnStop.IsEnabled = false;
                _playbackCts = null;
            }
        }

        private void DoMouseDown(string button)
        {
            switch (button)
            {
                case "Left":
                    mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
                    break;
                case "Right":
                    mouse_event(MOUSEEVENTF_RIGHTDOWN, 0, 0, 0, UIntPtr.Zero);
                    break;
                case "Middle":
                    mouse_event(MOUSEEVENTF_MIDDLEDOWN, 0, 0, 0, UIntPtr.Zero);
                    break;
            }
        }

        private void DoMouseUp(string button)
        {
            switch (button)
            {
                case "Left":
                    mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);
                    break;
                case "Right":
                    mouse_event(MOUSEEVENTF_RIGHTUP, 0, 0, 0, UIntPtr.Zero);
                    break;
                case "Middle":
                    mouse_event(MOUSEEVENTF_MIDDLEUP, 0, 0, 0, UIntPtr.Zero);
                    break;
            }
        }

        // Remove this using directive, as it causes ambiguity:
        // using System.Windows.Forms;

        // Use fully qualified names for OpenFileDialog and SaveFileDialog in BtnSave_Click and BtnLoad_Click:

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            if (_macroEvents.Count == 0) { TxtStatus.Text = "No macro to save."; return; }

            var dlg = new Microsoft.Win32.SaveFileDialog { Filter = "Macro files|*.json" };
            if (dlg.ShowDialog() == true)
            {
                var json = JsonSerializer.Serialize(_macroEvents, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(dlg.FileName, json);
                TxtStatus.Text = $"Saved macro to {dlg.FileName}";
            }
        }

        private void BtnLoad_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "Macro files|*.json" };
            if (dlg.ShowDialog() == true)
            {
                try
                {
                    var json = File.ReadAllText(dlg.FileName);
                    _macroEvents = JsonSerializer.Deserialize<List<MacroEvent>>(json) ?? new List<MacroEvent>();
                    try { LstEvents.Items.Clear(); } catch { }
                    foreach (var ev in _macroEvents) try { LstEvents.Items.Add(FormatEvent(ev)); } catch { }
                    TxtStatus.Text = $"Loaded {_macroEvents.Count} events from {dlg.FileName}";
                    BtnPlay.IsEnabled = _macroEvents.Count > 0;
                }
                catch (Exception ex)
                {
                    TxtStatus.Text = $"Failed to load: {ex.Message}";
                }
            }
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            UnregisterHotKey();
            UnregisterHotKey(_windowHandle, HOTKEY_ID_RECORD);
            UnregisterHotKey(_windowHandle, HOTKEY_ID_PLAY);
            if (_source != null) _source.RemoveHook(HwndHook);
            if (_cts != null) _cts.Cancel();

            if (_globalHook != null)
            {
                _globalHook.Dispose();
                _globalHook = null;
            }

            if (_playbackCts != null) _playbackCts.Cancel();
        }
    }

    // Simple MacroEvent class
    public class MacroEvent
    {
        public int DelayMs { get; set; } = 0; // ms since start (or previous event depending on usage)
        public int X { get; set; }
        public int Y { get; set; }
        public string Button { get; set; } = "Left";
        public bool IsDown { get; set; } = true;
    }
}
