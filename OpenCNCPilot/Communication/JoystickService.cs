using System;
using System.IO.Ports;
using System.Timers;
using System.Linq;
using System.Globalization;

namespace OpenCNCPilot.Communication
{
    internal class JoystickService
    {
        private SerialPort _serialPort;
        private Machine _machine;
        private Timer _jogTimer;

        private double _joyX, _joyY, _joyZ;
        private bool _isJogging = false;

        // --- KONFIGURATION (Hardcoded wie besprochen) ---
        private const double MAX_FEED_X = 2000.0;
        private const double MAX_FEED_Y = 2000.0;
        private const double MAX_FEED_Z = 600.0;
        private const int TIMER_INTERVAL_MS = 50;
        private const double JOG_BUFFER_MULT = 3.0; // Faktor für flüssige Pufferfüllung

        public JoystickService(string portName, int baudRate, Machine machine)
        {
            _machine = machine;
            _serialPort = new SerialPort(portName, baudRate);
            _serialPort.DataReceived += OnDataReceived;

            _jogTimer = new Timer(TIMER_INTERVAL_MS);
            _jogTimer.Elapsed += OnJogTimerTick;
            _jogTimer.AutoReset = true;
        }

        public void Open()
        {
            try
            {
                if (!_serialPort.IsOpen)
                    _serialPort.Open();
            }
            catch (Exception ex)
            {
                // Nutzt das OCP-interne Logging, falls verfügbar
                Console.WriteLine($"Joystick Error: {ex.Message}");
            }
        }

        public void Close()
        {
            _jogTimer.Stop();
            if (_serialPort.IsOpen)
                _serialPort.Close();

            _joyX = _joyY = _joyZ = 0;
            _isJogging = false;
        }

        private void OnDataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            try
            {
                while (_serialPort.BytesToRead > 0)
                {
                    string line = _serialPort.ReadLine();
                    ParseInput(line);
                }
            }
            catch { /* Übertragungsfehler ignorieren */ }
        }

        private void ParseInput(string data)
        {
            var parts = data.Trim().Split(':');
            if (parts.Length < 3 || parts[0] != "J") return;

            for (int i = 1; i < parts.Length; i += 2)
            {
                if (i + 1 >= parts.Length) break;

                string axis = parts[i].ToUpper();
                if (!int.TryParse(parts[i + 1], out int rawValue)) continue;

                // Normalisierung 0-1023 auf -1.0 bis 1.0 (Zentrum 512)
                double norm = (rawValue - 512) / 512.0;
                if (Math.Abs(norm) < 0.07) norm = 0; // Deadzone

                if (axis == "X") _joyX = norm;
                if (axis == "Y") _joyY = norm;
                if (axis == "Z") _joyZ = norm;
            }

            // Timer starten, wenn Bewegung erkannt wird
            if (!_jogTimer.Enabled && (_joyX != 0 || _joyY != 0 || _joyZ != 0))
            {
                _jogTimer.Start();
            }
        }

        private void OnJogTimerTick(object sender, ElapsedEventArgs e)
        {
            if (_joyX == 0 && _joyY == 0 && _joyZ == 0)
            {
                if (_isJogging) StopJogging();
                return;
            }

            if (_machine.BufferState > 30) return;
            
            _isJogging = true;

            // Wir berechnen die Feedrate (mm/min)
            double currentFeed = Math.Max(Math.Abs(_joyX) * MAX_FEED_X,
                                 Math.Max(Math.Abs(_joyY) * MAX_FEED_Y,
                                          Math.Abs(_joyZ) * MAX_FEED_Z));

            // Zeitfenster: 0.1 Sekunden (100ms) Weg vorausplanen
            // Nur ein winziges Häppchen Weg für 40ms senden
            double dt = 0.04 / 60.0;

            double dx = _joyX * MAX_FEED_X * dt;
            double dy = _joyY * MAX_FEED_Y * dt;
            double dz = _joyZ * MAX_FEED_Z * dt;

            // Nur senden, wenn die Bewegung groß genug ist (Präzision)
            if (Math.Abs(dx) > 0.001 || Math.Abs(dy) > 0.001 || Math.Abs(dz) > 0.001)
            {
                string cmd = string.Format(CultureInfo.InvariantCulture,
                    "$J=G91 G21 X{0:F3} Y{1:F3} Z{2:F3} F{3:F0}",
                    dx, dy, dz, currentFeed);

                _machine.SendLine(cmd);
            }
        }

        private async void StopJogging()
        {
            _jogTimer.Stop();

            // 1. PC-Warteschlange sofort leeren (Verhindert das "Nachlaufen" der Befehle)
            _machine.ClearQueue();

            // 2. GRBL-Hardware sofort stoppen
            _machine.JogCancel();

            // 3. Joystick-Werte intern nullen
            _joyX = _joyY = _joyZ = 0;

            // 4. Warten, bis GRBL wirklich steht (Status Idle)
            int timeout = 0;
            while (_machine.Status.Contains("Jog") && timeout < 20)
            {
                await System.Threading.Tasks.Task.Delay(50);
                timeout++;
            }

            // 5. Finaler Sync
            //_machine.SendLine("G4 P0");

            _isJogging = false;
        }
    }
}