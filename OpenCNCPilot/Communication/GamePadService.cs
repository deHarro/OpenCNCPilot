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

        // --- NEU: Struktur für die Vibrationsmotoren ---
        [StructLayout(LayoutKind.Sequential)]
        public struct XINPUT_VIBRATION
        {
            public ushort wLeftMotorSpeed;  // Niederfrequenter (tiefer) Motor
            public ushort wRightMotorSpeed; // Hochfrequenter (heller) Motor
        }

        [DllImport("xinput1_4.dll", EntryPoint = "XInputSetState")]
        public static extern int XInputSetState(int dwUserIndex, ref XINPUT_VIBRATION pVibration);
        
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
        //private bool _isTasterPressed = false;    // Der aktuelle Zustand des Tasters (Linker Stick)
        private bool _isAxisLockEnabled = false;     // Merker, ob Sperre aktiv ist
        private char _freeAxis = ' ';                // 'X' oder 'Y' - welche Achse fahren darf
        private bool _prevAxisLockPressed = false;    // Für die Flankenerkennung beim Ausschalten
        private DateTime _nextLockRumbleTime = DateTime.MinValue; // Zeitwächter für den Herzschlag
        private bool _wasLockSetDuringThisPress = false; // Unterscheidet Einstellen von Ausschalten

        // --- UI Status-Merker ---
        private bool _lastUiEnabled = false;
        private bool _lastUiLocked = false;

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

            // 2. Extract raw values and apply swap/invert logic
            double stick1X, stick1Y, stick2X, stick2Y;

            // Step A: Decide which stick is for X/Y and which for Z
            if (Properties.Settings.Default.GamepadSwapSticks)
            {
                stick1X = state.Gamepad.sThumbRX / 32768.0;
                stick1Y = state.Gamepad.sThumbRY / 32768.0;
                stick2X = state.Gamepad.sThumbLX / 32768.0; // Linke X-Achse (für Z-Horizontal)
                stick2Y = state.Gamepad.sThumbLY / 32768.0; // Linke Y-Achse (für Z-Vertikal)
            }
            else
            {
                stick1X = state.Gamepad.sThumbLX / 32768.0;
                stick1Y = state.Gamepad.sThumbLY / 32768.0;
                stick2X = state.Gamepad.sThumbRX / 32768.0; // Rechte X-Achse (für Z-Horizontal)
                stick2Y = state.Gamepad.sThumbRY / 32768.0; // Rechte Y-Achse (für Z-Vertikal)
            }

            // Step B: Handle X/Y Swap and Z-Axis orientation
            double valX = Properties.Settings.Default.GamepadSwapXY ? stick1Y : stick1X;
            double valY = Properties.Settings.Default.GamepadSwapXY ? stick1X : stick1Y;

            // --- NEU: Weiche für die Z-Achsen-Bewegung (Horizontal vs. Vertikal) ---
            double valZ = Properties.Settings.Default.GamepadZOnHorizontal ? stick2X : stick2Y;

            // Step C: Apply Axis Inversion
            if (Properties.Settings.Default.GamepadInvertX) valX *= -1.0;
            if (Properties.Settings.Default.GamepadInvertY) valY *= -1.0;
            if (Properties.Settings.Default.GamepadInvertZ) valZ *= -1.0;

            // 3. Apply deadzone to the final processed values
            _joyX = ApplyDeadzone(valX);
            _joyY = ApplyDeadzone(valY);
            _joyZ = ApplyDeadzone(valZ);

            // 4. Der Schalter (Rechte Schultertaste 'RB') - Trigger beim LOSLASSEN (fallende Flanke)
            bool currentTogglePressed = (state.Gamepad.wButtons & XINPUT_GAMEPAD_RIGHT_SHOULDER) != 0;

            if (!currentTogglePressed && _prevTogglePressed)
            {
                _isGamepadEnabled = !_isGamepadEnabled; // Zustand umkippen (Toggle)
                Console.WriteLine($"Gamepad-Schalter gewechselt auf: {(_isGamepadEnabled ? "EIN" : "AUS")}");

                // --- NEU: Haptisches Feedback ---
                if (_isGamepadEnabled)
                {
                    // EIN: Kurzer, knackiger Kick mit dem hochfrequenten rechten Motor (Speed 0-65535)
                    TriggerRumble(0, 50000, 150);
                }
                else
                {
                    // AUS: Längeres, tiefes Brummen mit dem linken Motor
                    TriggerRumble(40000, 0, 400);
                }
            }
            _prevTogglePressed = currentTogglePressed; // Zustand für den nächsten Zyklus merken

            // 5. Spurhalteassistent (Linke Schultertaste 'LB')
            bool currentAxisLockPressed = (state.Gamepad.wButtons & XINPUT_GAMEPAD_LEFT_SHOULDER) != 0;

            // 5. Spurhalteassistent (Linke Schultertaste 'LB')
            if (_isGamepadEnabled)
            {
                if (_isAxisLockEnabled)
                {
                    // BEDINGUNG A: Assistent ist AKTIV und die Taste wird losgelassen
                    if (!currentAxisLockPressed && _prevAxisLockPressed)
                    {
                        // Prüfen: War das nur das Loslassen nach dem Einstellen?
                        if (_wasLockSetDuringThisPress)
                        {
                            // Ja, das war das Ende des "Setup-Haltens". Lock bleibt aktiv!
                            _wasLockSetDuringThisPress = false;
                        }
                        else
                        {
                            // Nein, das war ein echter, neuer Klick zum Ausschalten!
                            _isAxisLockEnabled = false;
                            _freeAxis = ' ';
                            Console.WriteLine("Spurhalteassistent AUS: Alle Achsen wieder frei.");
                        }
                    }
                }
                else
                {
                    // BEDINGUNG B: Assistent ist INAKTIV -> Taste HALTEN und Stick bewegen
                    if (currentAxisLockPressed)
                    {
                        // X-Ausschlag stärker -> X wird Fahr-Achse
                        if (Math.Abs(_joyX) > 0.05 && Math.Abs(_joyX) >= Math.Abs(_joyY))
                        {
                            _isAxisLockEnabled = true;
                            _freeAxis = 'X';
                            _wasLockSetDuringThisPress = true; // MERKEN: Wir haben ihn GERADE eingestellt!
                            _nextLockRumbleTime = DateTime.MinValue;
                            Console.WriteLine("Spurhalteassistent EIN: X darf fahren!");
                        }
                        // Y-Ausschlag stärker -> Y wird Fahr-Achse
                        else if (Math.Abs(_joyY) > 0.05 && Math.Abs(_joyY) > Math.Abs(_joyX))
                        {
                            _isAxisLockEnabled = true;
                            _freeAxis = 'Y';
                            _wasLockSetDuringThisPress = true; // MERKEN: Wir haben ihn GERADE eingestellt!
                            _nextLockRumbleTime = DateTime.MinValue;
                            Console.WriteLine("Spurhalteassistent EIN: Y darf fahren!");
                        }
                    }
                }
            }
            _prevAxisLockPressed = currentAxisLockPressed;


            // 6. Die blockierte Achse mathematisch nullen (Z bleibt immer unangetastet)
            if (_isGamepadEnabled && _isAxisLockEnabled)
            {
                if (_freeAxis == 'X') _joyY = 0.0; // X darf fahren -> Y knallhart blockieren
                if (_freeAxis == 'Y') _joyX = 0.0; // Y darf fahren -> X knallhart blockieren
            }


            // 7. Zyklischer haptischer Herzschlag (alle 2 Sekunden zupfen, solange Assistent aktiv ist)
            if (_isGamepadEnabled && _isAxisLockEnabled)
            {
                if (DateTime.Now >= _nextLockRumbleTime)
                {
                    TriggerRumble(0, 30000, 100);
                    _nextLockRumbleTime = DateTime.Now.AddSeconds(2);
                }
            }


            // 8. Sicherheitsabschaltung
            if (!_isGamepadEnabled)
            {
                _joyX = _joyY = _joyZ = 0.0;
                _isAxisLockEnabled = false;
                _freeAxis = ' ';
            }

            // 9. OCP Logik ausführen

            // --- NEU: SICHERHEITSCHECK FÜR DEN APP-SHUTDOWN ---
            // Wenn OCP gerade geschlossen wird, verschwindet das Application-Objekt.
            // Der Timer muss in diesem Fall sofort abbrechen, sonst knallt es beim Dispatcher.
            if (System.Windows.Application.Current == null || System.Windows.Application.Current.Dispatcher == null)
            {
                return;
            }

            // --- Das UI-Update an den Haupt-Thread schicken (nur bei Zustandswechsel) ---
            if (_isGamepadEnabled != _lastUiEnabled || _isAxisLockEnabled != _lastUiLocked)
            {
                Console.WriteLine($"[Gamepad UI] Zustandswechsel erkannt! Enabled: {_isGamepadEnabled}, Locked: {_isAxisLockEnabled}");

                _lastUiEnabled = _isGamepadEnabled;
                _lastUiLocked = _isAxisLockEnabled;

                System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    OpenCNCPilot.MainWindow theRealMainWindow = null;
                    foreach (System.Windows.Window window in System.Windows.Application.Current.Windows)
                    {
                        if (window is OpenCNCPilot.MainWindow)
                        {
                            theRealMainWindow = window as OpenCNCPilot.MainWindow;
                            break;
                        }
                    }

                    if (theRealMainWindow != null)
                    {
                        Console.WriteLine("[Gamepad UI] ERFOLG: Echtes MainWindow gefunden! Aktualisiere Visuals.");
                        theRealMainWindow.UpdateGamepadVisuals(_lastUiEnabled, _lastUiLocked);
                    }
                    else
                    {
                        Console.WriteLine("[Gamepad UI] FEHLER: Kein offenes Fenster vom Typ 'MainWindow' gefunden!");
                    }
                }));
            }

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

        private async void TriggerRumble(ushort leftMotor, ushort rightMotor, int durationMs)
        {
            // Vibration einschalten
            XINPUT_VIBRATION vibration = new XINPUT_VIBRATION
            {
                wLeftMotorSpeed = leftMotor,
                wRightMotorSpeed = rightMotor
            };
            XInputSetState(0, ref vibration); // Senden an Controller Index 0

            // Warten (ohne das Programm zu blockieren)
            await System.Threading.Tasks.Task.Delay(durationMs);

            // Vibration wieder ausschalten
            vibration.wLeftMotorSpeed = 0;
            vibration.wRightMotorSpeed = 0;
            XInputSetState(0, ref vibration);
        }
    }
}