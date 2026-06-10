using System;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Timers;
using System.Windows;
using OpenCNCPilot.Properties; // Für den Zugriff auf die Settings

namespace OpenCNCPilot.Communication
{
    internal class GamePadService
    {
        // --- XInput Native Windows API Deklarationen ---
        [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
        private static extern int XInputGetState(int dwUserIndex, ref XINPUT_STATE pState);

        [StructLayout(LayoutKind.Sequential)]
        private struct XINPUT_STATE
        {
            public uint dwPacketNumber;
            public XINPUT_GAMEPAD Gamepad;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct XINPUT_GAMEPAD
        {
            public ushort wButtons;
            public byte bLeftTrigger;
            public byte bRightTrigger;
            public short sThumbLX;
            public short sThumbLY;
            public short sThumbRX;
            public short sThumbRY;
        }

        // Tastenkonstanten
        private const ushort XINPUT_GAMEPAD_LEFT_SHOULDER = 0x0100;
        private const ushort XINPUT_GAMEPAD_RIGHT_SHOULDER = 0x0200;

        // Zustandstracker für die Logik <deHarry, 2026-06-08>
        private bool _isGamepadEnabled = false;   // Der funktionale "Schalter" (An/Aus)
        private bool _prevTogglePressed = false;  // Merker für die Flankenerkennung (Rechter Stick)
        private bool _isTasterPressed = false;    // Der aktuelle Zustand des Tasters (Linker Stick)
        
        // --- Klassenvariablen ---
        private Machine _machine;
        private OpenCNCPilot.MainWindow _parent;
        private System.Timers.Timer _jogTimer;

        private const int TIMER_INTERVAL_MS = 50;
        private const double DEADZONE = 0.08;

        private double _joyX, _joyY, _joyZ;
        private bool _isJogging = false;
        private bool _isConnected = false;

        public GamePadService(Machine machine, OpenCNCPilot.MainWindow parent)
        {
            _machine = machine;
            _parent = parent;

            _jogTimer = new System.Timers.Timer(TIMER_INTERVAL_MS);
            _jogTimer.Elapsed += OnJogTimerTick;
            _jogTimer.AutoReset = true;
        }

        public void Open()
        {
            // Prüfen, ob an Index 0 ein Controller antwortet (0 = Erfolgreich)
            XINPUT_STATE state = new XINPUT_STATE();
            if (XInputGetState(0, ref state) == 0)
            {
                _isConnected = true;
                _jogTimer.Start();
                Console.WriteLine("GamePadService erfolgreich gestartet.");
            }
            else
            {
                _isConnected = false;
                Console.WriteLine("Kein XInput Controller an Index 0 gefunden.");
            }
        }

        public void Close()
        {
            _jogTimer?.Stop();
            if (_isJogging) StopJogging();

            _joyX = _joyY = _joyZ = 0;
            _isConnected = false;
        }

        private void OnJogTimerTick(object sender, ElapsedEventArgs e)
        {
            if (!_isConnected) return;

            XINPUT_STATE state = new XINPUT_STATE();

            // 1. Controller abfragen (0 = Success, alles andere = Disconnected)
            if (XInputGetState(0, ref state) != 0)
            {
                if (_isJogging) StopJogging();
                return;
            }

            // 2. Rohwerte auf -1.0 bis 1.0 normieren
            double rawX = state.Gamepad.sThumbLX / 32768.0;
            double rawY = state.Gamepad.sThumbLY / 32768.0;
            // Wir nutzen für Z den rechten Stick (Hoch/Runter)
            double rawZ = state.Gamepad.sThumbRY / 32768.0;

            // 3. Totzone anwenden
            _joyX = ApplyDeadzone(rawX);
            _joyY = ApplyDeadzone(rawY);
            _joyZ = ApplyDeadzone(rawZ);

            // 4. Der Schalter (Rechte Schultertaste 'RB') - Trigger beim LOSLASSEN (fallende Flanke)
            bool currentTogglePressed = (state.Gamepad.wButtons & XINPUT_GAMEPAD_RIGHT_SHOULDER) != 0;

            if (!currentTogglePressed && _prevTogglePressed)
            {
                _isGamepadEnabled = !_isGamepadEnabled; // Zustand umkippen (Toggle)
                Console.WriteLine($"Gamepad-Schalter gewechselt auf: {(_isGamepadEnabled ? "EIN" : "AUS")}");
            }
            _prevTogglePressed = currentTogglePressed; // Zustand für den nächsten Zyklus merken

            // 5. Der Taster (Linke Schultertaste 'LB') - Reiner Momentanzustand (wahr, solange gedrückt gehalten)
            _isTasterPressed = (state.Gamepad.wButtons & XINPUT_GAMEPAD_LEFT_SHOULDER) != 0;

            // 6. Sicherheitsverriegelung: Wenn der Schalter AUS ist, Werte sofort nullen
            if (!_isGamepadEnabled)
            {
                _joyX = _joyY = _joyZ = 0;
            }

            // 7. OCP Logik ausführen
            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_parent == null || !_parent.IsJoggingAllowed || _machine.Mode != Machine.OperatingMode.Manual)
                {
                    if (_isJogging) StopJogging();
                    return;
                }

                try
                {
                    if (_machine.BufferState > 20) return;

                    if (_joyX == 0 && _joyY == 0 && _joyZ == 0)
                    {
                        if (_isJogging) StopJogging();
                        return;
                    }

                    _isJogging = true;

                    // Settings laden (hier wird das _using OpenCNCPilot.Properties;_ benötigt)
                    double dt = Settings.Default.JoystickDt / 60.0;
                    double maxFX = Settings.Default.JoystickMaxFeedX;
                    double maxFY = Settings.Default.JoystickMaxFeedY;
                    double maxFZ = Settings.Default.JoystickMaxFeedZ;

                    double dx = _joyX * maxFX * dt;
                    double dy = (_joyY * -1.0) * maxFY * dt;
                    double dz = (_joyZ * -1.0) * maxFZ * dt;

                    double feed = Math.Max(Math.Abs(_joyX) * maxFX,
                                  Math.Max(Math.Abs(_joyY) * maxFY,
                                           Math.Abs(_joyZ) * maxFZ));

                    if (Math.Abs(dx) > 0.001 || Math.Abs(dy) > 0.001 || Math.Abs(dz) > 0.001)
                    {
                        _machine.SendLine(string.Format(CultureInfo.InvariantCulture,
                            "$J=G91 G21 X{0:F3} Y{1:F3} Z{2:F3} F{3:F0}", dx, dy, dz, feed));
                    }
                    else if (_isJogging)
                    {
                        StopJogging();
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("Fehler im GamePad Timer-Tick: " + ex.Message);
                }
            }));
        }

        private double ApplyDeadzone(double value)
        {
            if (Math.Abs(value) < DEADZONE) return 0;
            return Math.Sign(value) * ((Math.Abs(value) - DEADZONE) / (1.0 - DEADZONE));
        }

        private async void StopJogging()
        {
            if (!_isJogging) return;
            _isJogging = false;

            _machine.ClearQueue();
            _machine.JogCancel();

            _joyX = _joyY = _joyZ = 0;

            int timeout = 0;
            while (_machine.Status.Contains("Jog") && timeout < 20)
            {
                await System.Threading.Tasks.Task.Delay(50);
                timeout++;
            }
        }
    }
}