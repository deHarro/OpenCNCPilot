/* 
    Adapter für einen Joystick mit zwei Daumenjoysticks, deHarry, 2026-02-11
    Einzelheiten zum Joystick siehe www.harald-sattler.de/html/joystick-steuerung.htm
    
    Der Joystick wird über einen Arduino Nano betrieben, der die RAW-Werte der Sticks
    etwas glättet und auf die Mitte zentriert, dann über die serielle Schnittstelle an 
    OpenCNCPilot (OCP) sendet.

    OCP hat eine neue Klasse JoystickService erhalten, die sowohl die Kommunikation zum 
    Joystick abwickelt, als auch die Berechnung für die Jog-Commands Richtung GRBL erledigt.

    Größte Herausforderung bei dem Projekt war die korrekte Behandlung der verschiedenen
    involvierten Buffer zwischen Joystick, OCP und GRBL.

    Der programmtechnische Anschluss an OCP erfolgt über die Klasse Machine.
    Hier wurde ein RaiseEvent eingebaut, damit JoystickService mitbekommt, wenn GRBL mit
    "ok" oder "error" antwortet.

    In MainWindow.Xaml.cs wurden ebenfalls ein paar Zeilen Code eingebaut. Zum einen eine 
    private Variable auf Klassenebene "_joystick", die dazu verwendet wird, einen zweiten 
    Kommunikationsanschluss für den Joystick zu bedienen. Zum anderen die Routinen um diesen
    Port über die GUI parametrieren, öffnen und schließen zu können.

    Im Settings Dialog wurden die Auswahlboxen für den Port und die Baudrate eingebaut, die 
    Werte werden in den Settings gespeichert.

    Ohne brachiale Unterstützung durch Gemini hätte ich keine Chance gehabt, das Projekt 
    umzusetzen, da ich in Sachen C# vollkommen unbeleckt bin, umgekehrt hatte Gemini keine 
    Chance, ohne meine geduldigen Erklärungen, wie etwas Bestimmtes in OCP gelöst ist, und 
    meine Anleitung auf einen grünen Zweig zu kommen. Teamwork at it's best ;)

    Die ausgefuchste Logik im "alten" Joystick Code ist ziemlich hinfällig geworden, die 
    komplette Mathematik wird jetzt in OCP erledigt, der Joystick Arduino liefert nur noch 
    die geglätteten Poti-Werte der drei Daumen-Joysticks über die Schnittstelle.

*/

using System;
using System.Globalization;
using System.IO.Ports;
using System.Timers;


namespace OpenCNCPilot.Communication
{
    internal class JoystickService
    {
        private Machine _machine;
        private SerialPort _serialPort;
        private OpenCNCPilot.MainWindow _parent;
        private Timer _jogTimer;

        private double _joyX, _joyY, _joyZ;
        private bool _isJogging = false;

        // --- KONFIGURATION (erst mal Hardcoded) ---
       private double MAX_FEED_X = 2000.0;
       private double MAX_FEED_Y = 2000.0;
       private double MAX_FEED_Z = 600.0;

        private const int TIMER_INTERVAL_MS = 50;           // Zeitraster für die Jog-Befehle Richtung GRBL

        public JoystickService(string portName, int baudRate, Machine machine, OpenCNCPilot.MainWindow parent)
        {
            _machine = machine;
            _parent = parent; // Zuweisung

            _serialPort = new SerialPort(portName, baudRate);
            _serialPort.DataReceived += OnDataReceived;
            //_machine.Connection.LineReceived += Connection_LineReceived;

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
                    Console.WriteLine($"Empfang: {line}");
                    ParseInput(line);
                }
            }
            catch { /* Übertragungsfehler ignorieren */ }
        }

        private void ParseInput(string data)
        {
            // Wir schieben die komplette Verarbeitung in den UI-Thread
            System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    var parts = data.Trim().Split(':');
                    if (parts.Length < 3 || parts[0] != "J") return;

                    for (int i = 1; i < parts.Length; i += 2)
                    {
                        if (i + 1 >= parts.Length) break;

                        string axis = parts[i].ToUpper();
                        if (!int.TryParse(parts[i + 1], out int rawValue)) continue;

                        double norm = (rawValue - 512) / 512.0;
                        if (Math.Abs(norm) < 0.07) norm = 0;

                        if (axis == "X") _joyX = norm;
                        if (axis == "Y") _joyY = norm;
                        if (axis == "Z") _joyZ = norm;
                    }

                    if (!_jogTimer.Enabled && (_joyX != 0 || _joyY != 0 || _joyZ != 0))
                    {
                        _jogTimer.Start();
                    }
                }
                catch (Exception ex)
                {
                    // Hier könnten wir einen Haltepunkt setzen, falls es trotzdem kracht
                    Console.WriteLine("Fehler beim Parsen: " + ex.Message);
                }
            }));
        }

        private void OnJogTimerTick(object sender, ElapsedEventArgs e)
        {
            System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                // Nur wenn "Enable Keyboard Jogging" aktiv ist, darf der Joystick senden
                if (_parent == null || !_parent.IsJoggingAllowed)
                {
                    if (_isJogging) StopJogging();
                    return;
                }

                // Modus-Check Manual Mode?
                if (_machine.Mode != Machine.OperatingMode.Manual)
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

                    double dt = 0.03 / 60.0;
                    double dx = _joyX * MAX_FEED_X * dt;
                    double dy = _joyY * MAX_FEED_Y * dt;
                    double dz = _joyZ * MAX_FEED_Z * dt;
                    double feed = Math.Max(Math.Abs(_joyX) * MAX_FEED_X,
                                  Math.Max(Math.Abs(_joyY) * MAX_FEED_Y,
                                           Math.Abs(_joyZ) * MAX_FEED_Z));

                    if (Math.Abs(dx) > 0.001 || Math.Abs(dy) > 0.001 || Math.Abs(dz) > 0.001)
                    {
                        // Dieser Aufruf ist jetzt sicher, da wir im UI-Thread sind
                        _machine.SendLine(string.Format(CultureInfo.InvariantCulture,
                            "$J=G91 G21 X{0:F3} Y{1:F3} Z{2:F3} F{3:F0}", dx, dy, dz, feed));
                    }
                }
                catch (Exception ex)
                {
                    // Wenn es JETZT kracht, fangen wir es hier
                    System.Diagnostics.Debug.WriteLine("Fehler im Timer-Tick: " + ex.Message);
                }
            }));
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