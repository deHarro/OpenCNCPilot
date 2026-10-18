using Microsoft.Win32;
using OpenCNCPilot.Communication;
using OpenCNCPilot.GCode;
using OpenCNCPilot.Util;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Collections.ObjectModel;
using System.Windows.Input;				// Für MouseButtonEventArgs
using System.Windows.Media;				// Für VisualTreeHelper / HitTest
using System.Windows.Media.Media3D;		// Für Point3D und RayMeshGeometry3DHitTestResult
using HelixToolkit.Wpf;                 // Falls du noch Helix-Typen nutzt
using System.Threading.Tasks;

namespace OpenCNCPilot
{
	public partial class MainWindow : Window, INotifyPropertyChanged
	{
		Machine machine = new Machine();

		OpenFileDialog openFileDialogGCode = new OpenFileDialog() { Filter = Constants.FileFilterGCode };
		SaveFileDialog saveFileDialogGCode = new SaveFileDialog() { Filter = Constants.FileFilterGCode };
		OpenFileDialog openFileDialogHeightMap = new OpenFileDialog() { Filter = Constants.FileFilterHeightMap };
		SaveFileDialog saveFileDialogHeightMap = new SaveFileDialog() { Filter = Constants.FileFilterHeightMap };

		ObservableCollection<GCodeLayer> AllLayers = new ObservableCollection<GCodeLayer>();

		// Statt Objektreferenz speichern wir die reinen Kameraparameter absolut fehlerfrei
		private bool _hasSavedCamera = false;
		private Point3D _savedPosition;
		private Vector3D _savedLookDirection;
		private Vector3D _savedUpDirection;
		private bool _savedIsOrtho;
		private double _savedOrthoWidth;
		private double _savedPerspectiveFOV;

		private Vector3D _cncUpDirection = new Vector3D(0, 1, 0); // Merkt sich dauerhaft die CNC-Ausrichtung

		bool isViewFlat = false;            // wurde der Viewport flach auf X/Y-Ebene gelegt? (Button "Lay flat 3D Viewport" in der Debug-Box)

		// Wir nutzen Visual3D als Basisklasse, da LinesVisual3D, QuadVisual3D und MeshElement3D alle davon erben.
		private Dictionary<System.Windows.Media.Media3D.Visual3D, List<int>> _lineMapping = new Dictionary<System.Windows.Media.Media3D.Visual3D, List<int>>();

		private Point3D? _firstMeasurePoint = null; // Speichert den ersten Klick für die Messung
		private double _lastX, _lastY;              // Für den "Send to Manual"-Button
		private double diagonal;

		DKWHandler dkw = new DKWHandler();

		private HelixToolkit.Wpf.SphereVisual3D _clickMarker;
		private HelixToolkit.Wpf.SphereVisual3D _measureMarker;

		GCodeFile ToolPath { get; set; } = GCodeFile.Empty;
		HeightMap Map { get; set; }

		bool HeightMapApplied { get; set; } = false;

		GrblSettingsWindow settingsWindow = new GrblSettingsWindow();

		public event PropertyChangedEventHandler PropertyChanged;

		private void RaisePropertyChanged(string propertyName)
		{
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
		}

		// private variable on class level		<deHarry, 2026-02-16>
		private OpenCNCPilot.Communication.JoystickService _joystick;
        // private variable on class level		<deHarry, 2026-06-08>
        private OpenCNCPilot.Communication.GamePadService _gamepad;

        public MainWindow()
		{
			AppDomain.CurrentDomain.UnhandledException += UnhandledException;
			System.Threading.Thread.CurrentThread.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;	// behandelt Punkt/Komma in Dezimalzahlen wie Windows
			InitializeComponent();

			_clickMarker   = new HelixToolkit.Wpf.SphereVisual3D { Radius = 0.5, Fill = System.Windows.Media.Brushes.Red };
			viewport.Children.Add(_clickMarker);
			_measureMarker = new HelixToolkit.Wpf.SphereVisual3D { Radius = 0.5, Fill = System.Windows.Media.Brushes.Blue };
			viewport.Children.Add(_measureMarker);

			txtProbeDiam.Text = Properties.Settings.Default.DKWProbeDiameter.ToString("F2");
			txtMillDiam.Text = Properties.Settings.Default.DKWMillDiameter.ToString("F2");

/*			// global variable for JoystickService, <deHarry, 2026-02-06>
			// Hier wird der Port für den Joystick instantiiert
			// Prüfung auf: nicht null, nicht leer, kein reines Leerzeichen UND nicht "None"
			if (!string.IsNullOrWhiteSpace(Properties.Settings.Default.JoystickPort) && !Properties.Settings.Default.JoystickPort.Equals("None", StringComparison.OrdinalIgnoreCase))
			{
				try
				{
					_joystick = new OpenCNCPilot.Communication.JoystickService(
						Properties.Settings.Default.JoystickPort,
						Properties.Settings.Default.JoystickBaudrate,
						machine, this
					);
					_joystick.Open();
				}
				catch (Exception ex)
				{
					// WICHTIG: Setze das Objekt auf null, damit die Work-Loop später Bescheid weiß
					_joystick = null;
					// Optional: Ein Log-Eintrag, damit der User weiß, warum der Joystick nicht geht
					Console.WriteLine($"Joystick-Fehler an {Properties.Settings.Default.JoystickPort}: {ex.Message}");
				}
			}
			else
			{
				// Explizit null setzen, wenn kein Port gewählt wurde
				_joystick = null;
			}
*/

			// Weiche für Joystick oder Gamepad beim Programmstart, <deHarry, 2026-06-08>
			_joystick?.Close();
			_joystick = null;
			_gamepad?.Close();
			_gamepad = null;

			if (Properties.Settings.Default.UseXInputGamePad)
			{
				try
				{
					_gamepad = new OpenCNCPilot.Communication.GamePadService(machine, this);
					_gamepad.Open();
					Console.WriteLine("Gamepad-Modus beim Start aktiviert.");
				}
				catch (Exception ex)
				{
					_gamepad = null;
					Console.WriteLine($"Gamepad-Fehler beim Start: {ex.Message}");
				}
			}
			else if (!string.IsNullOrWhiteSpace(Properties.Settings.Default.JoystickPort) && !Properties.Settings.Default.JoystickPort.Equals("None", StringComparison.OrdinalIgnoreCase))
			{
				try
				{
					_joystick = new OpenCNCPilot.Communication.JoystickService(
						Properties.Settings.Default.JoystickPort,
						Properties.Settings.Default.JoystickBaudrate,
						machine, this
					);
					_joystick.Open();
					Console.WriteLine("Serieller Joystick-Modus beim Start aktiviert.");
				}
				catch (Exception ex)
				{
					_joystick = null;
					Console.WriteLine($"Joystick-Fehler an {Properties.Settings.Default.JoystickPort}: {ex.Message}");
				}
			}



			Properties.Settings.Default.PropertyChanged += (s, e) => {
				if (e.PropertyName == "MarkerSize")
				{
					double newRadius = GetDynamicMarkerSize() / 2.0;

					if (_clickMarker != null) _clickMarker.Radius = newRadius;
					if (_measureMarker != null) _measureMarker.Radius = newRadius;
				}
			};

			/*			// automatically  open/close joystick port, <deHarry, 2026-02-06>
						machine.ConnectionStateChanged += () =>
						{
							if (!string.IsNullOrWhiteSpace(Properties.Settings.Default.JoystickPort) && !Properties.Settings.Default.JoystickPort.Equals("None", StringComparison.OrdinalIgnoreCase))
							{
								try
								{
									if (machine.Connected)
									{
										_joystick.Open();
									}
									else
									{
										if (_joystick != null) _joystick.Close();
									}
								}
								catch (Exception ex)
								{
									// WICHTIG: Setze das Objekt auf null, damit die Work-Loop später Bescheid weiß
									_joystick = null;
									// Optional: Ein Log-Eintrag, damit der User weiß, warum der Joystick nicht geht
									Console.WriteLine($"Joystick-Fehler an {Properties.Settings.Default.JoystickPort}: {ex.Message}");
								}
							}
						};
						// automatically  open/close joystick port, <deHarry, 2026-02-06>
			*/

			// automatically  open/close joystick or gamepad, <deHarry, 2026-06-08>
			machine.ConnectionStateChanged += async () =>
			{
				try
				{
					if (machine.Connected)
					{
						// --- NEU: Wir warten 2 Sekunden, bis der Startup-Handshake durch ist ---
						await System.Threading.Tasks.Task.Delay(2000);

						// Erst jetzt, wenn alles ruhig ist, die Werte abholen
						FetchGrblMaxFeeds();

						if (Properties.Settings.Default.UseXInputGamePad)
						{
							if (_gamepad == null) _gamepad = new OpenCNCPilot.Communication.GamePadService(machine, this);
							_gamepad.Open();
						}
						else if (!string.IsNullOrWhiteSpace(Properties.Settings.Default.JoystickPort) && !Properties.Settings.Default.JoystickPort.Equals("None", StringComparison.OrdinalIgnoreCase))
						{
							if (_joystick == null) _joystick = new OpenCNCPilot.Communication.JoystickService(Properties.Settings.Default.JoystickPort, Properties.Settings.Default.JoystickBaudrate, machine, this);
							_joystick.Open();
						}
					}
					else
					{
						if (_joystick != null) { _joystick.Close(); _joystick = null; }
						if (_gamepad != null) { _gamepad.Close(); _gamepad = null; }
					}
				}
				catch (Exception ex)
				{
					_joystick = null;
					_gamepad = null;
					Console.WriteLine($"Fehler beim Ändern des Verbindungsstatus für Controller: {ex.Message}");
				}
			};
			// automatically  open/close joystick or gamepad, <deHarry, 2026-06-08>

			openFileDialogGCode.FileOk += OpenFileDialogGCode_FileOk;
			saveFileDialogGCode.FileOk += SaveFileDialogGCode_FileOk;
			openFileDialogHeightMap.FileOk += OpenFileDialogHeightMap_FileOk;
			saveFileDialogHeightMap.FileOk += SaveFileDialogHeightMap_FileOk;

			machine.ConnectionStateChanged += Machine_ConnectionStateChanged;

			machine.NonFatalException += Machine_NonFatalException;
			machine.Info += Machine_Info;
			machine.LineReceived += Machine_LineReceived;
			machine.LineReceived += settingsWindow.LineReceived;
			machine.StatusReceived += Machine_StatusReceived;
			machine.LineSent += Machine_LineSent;

			machine.PositionUpdateReceived += Machine_PositionUpdateReceived;
			machine.StatusChanged += Machine_StatusChanged;
			machine.DistanceModeChanged += Machine_DistanceModeChanged;
			machine.UnitChanged += Machine_UnitChanged;
			machine.PlaneChanged += Machine_PlaneChanged;
			machine.BufferStateChanged += Machine_BufferStateChanged;
			machine.OperatingModeChanged += Machine_OperatingMode_Changed;
			machine.FileChanged += Machine_FileChanged;
			machine.FilePositionChanged += Machine_FilePositionChanged;
			machine.ProbeFinished += Machine_ProbeFinished;
			machine.OverrideChanged += Machine_OverrideChanged;
			machine.PinStateChanged += Machine_PinStateChanged;

			Machine_OperatingMode_Changed();
			Machine_PositionUpdateReceived();

			Properties.Settings.Default.SettingChanging += Default_SettingChanging;
			FileRuntimeTimer.Tick += FileRuntimeTimer_Tick;

			machine.ProbeFinished += Machine_ProbeFinished_UserOutput;

			LoadMacros();

			settingsWindow.SendLine += machine.SendLine;

			machine.Calculator.GetGCode += () => ToolPath;

			CheckBoxUseExpressions_Changed(null, null);
			ButtonRestoreViewport_Click(null, null);

			UpdateCheck.CheckForUpdate();

			if (App.Args.Length > 0)
			{
				if (File.Exists(App.Args[0]))
				{
					openFileDialogGCode.FileName = App.Args[0];
					OpenFileDialogGCode_FileOk(null, null);
				}
			}
		}

		public Vector3 LastProbePosMachine { get; set; }
		public Vector3 LastProbePosWork { get; set; }

		private void Machine_ProbeFinished_UserOutput(Vector3 position, bool success)
		{
			LastProbePosMachine = machine.LastProbePosMachine;
			LastProbePosWork = machine.LastProbePosWork;

			RaisePropertyChanged("LastProbePosMachine");
			RaisePropertyChanged("LastProbePosWork");
		}

		private void UnhandledException(object sender, UnhandledExceptionEventArgs ea)
		{
			Exception e = (Exception)ea.ExceptionObject;

			string info = "Unhandled Exception:\r\nMessage:\r\n";
			info += e.Message;
			info += "\r\nStackTrace:\r\n";
			info += e.StackTrace;
			info += "\r\nToString():\r\n";
			info += e.ToString();

			MessageBox.Show(info);
			Console.WriteLine(info);

			try
			{
				System.IO.File.WriteAllText("OpenCNCPilot_Crash_Log.txt", info);
			}
			catch { }

			Environment.Exit(1);
		}

		private void Default_SettingChanging(object sender, System.Configuration.SettingChangingEventArgs e)
		{
			if (e.SettingName.Equals("JogFeed") ||
				e.SettingName.Equals("JogDistance") ||
				e.SettingName.Equals("ProbeFeed") ||
				e.SettingName.Equals("ProbeSafeHeight") ||
				e.SettingName.Equals("ProbeMinimumHeight") ||
				e.SettingName.Equals("ProbeMaxDepth") ||
				e.SettingName.Equals("SplitSegmentLength") ||
				e.SettingName.Equals("ViewportArcSplit") ||
				e.SettingName.Equals("ArcToLineSegmentLength") ||
				e.SettingName.Equals("ProbeXAxisWeight") ||
				e.SettingName.Equals("ConsoleFadeTime"))
			{
				if (((double)e.NewValue) <= 0)
					e.Cancel = true;
			}

			if (e.SettingName.Equals("SerialPortBaud") ||
				e.SettingName.Equals("StatusPollInterval") ||
				e.SettingName.Equals("ControllerBufferSize"))
			{
				if (((int)e.NewValue) <= 0)
					e.Cancel = true;
			}
		}

		private void Hyperlink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
		{
			System.Diagnostics.Process.Start(e.Uri.AbsoluteUri);
		}

		public string Version
		{
			get
			{
				var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
				return $"{version}";
			}
		}

		public string WindowTitle
		{
			get
			{
				if (CurrentFileName.Length < 1)
					return $"OpenCNCPilot v{Version} by martin2250";
				else
					return $"OpenCNCPilot v{Version} by martin2250 - {CurrentFileName}";
			}
		}

		public bool IsJoggingAllowed                // deHarry, 2026-02-19
		{
			get
			{
				// Wir kombinieren: Checkbox an UND Fokus in der TextBox
				return CheckBoxEnableJog.IsChecked == true && TextBoxJog.IsKeyboardFocused;
			}
		}

		private void Window_Drop(object sender, DragEventArgs e)
		{
			if (e.Data.GetDataPresent(DataFormats.FileDrop))
			{
				string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);

				if (files.Length > 0)
				{
					string file = files[0];

					if (file.EndsWith(".hmap"))
					{
						if (machine.Mode == Machine.OperatingMode.Probe || Map != null)
							return;

						OpenHeightMap(file);
					}
					else
					{
						if (machine.Mode == Machine.OperatingMode.SendFile)
							return;

						try
						{
							machine.SetFile(System.IO.File.ReadAllLines(file));
						}
						catch (Exception ex)
						{
							MessageBox.Show(ex.Message);
						}
					}
				}
			}
		}

		private void Window_DragEnter(object sender, DragEventArgs e)
		{
			if (e.Data.GetDataPresent(DataFormats.FileDrop))
			{
				string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);

				if (files.Length > 0)
				{
					string file = files[0];

					if (file.EndsWith(".hmap"))
					{
						if (machine.Mode != Machine.OperatingMode.Probe && Map == null)
						{
							e.Effects = DragDropEffects.Copy;
							return;
						}
					}
					else
					{
						if (machine.Mode != Machine.OperatingMode.SendFile)
						{
							e.Effects = DragDropEffects.Copy;
							return;
						}
					}
				}
			}

			e.Effects = DragDropEffects.None;
		}

		private void viewport_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
		{
			if (e.Key == System.Windows.Input.Key.Space)
			{
				machine.FeedHold();
				e.Handled = true;
			}
		}

		private void ButtonRapidOverride_Click(object sender, RoutedEventArgs e)
		{
			Button b = sender as Button;

			if (b == null)
				return;

			switch (b.Content as string)
			{
				case "100%":
					machine.SendControl(0x95);
					break;
				case "50%":
					machine.SendControl(0x96);
					break;
				case "25%":
					machine.SendControl(0x97);
					break;
			}
		}

		private void ButtonFeedOverride_Click(object sender, RoutedEventArgs e)
		{
			Button b = sender as Button;

			if (b == null)
				return;

			switch (b.Tag as string)
			{
				case "100%":
					machine.SendControl(0x90);
					break;
				case "+10%":
					machine.SendControl(0x91);
					break;
				case "-10%":
					machine.SendControl(0x92);
					break;
				case "+1%":
					machine.SendControl(0x93);
					break;
				case "-1%":
					machine.SendControl(0x94);
					break;
			}
		}

		private void ButtonSpindleOverride_Click(object sender, RoutedEventArgs e)
		{
			Button b = sender as Button;

			if (b == null)
				return;

			switch (b.Tag as string)
			{
				case "100%":
					machine.SendControl(0x99);
					break;
				case "+10%":
					machine.SendControl(0x9A);
					break;
				case "-10%":
					machine.SendControl(0x9B);
					break;
				case "+1%":
					machine.SendControl(0x9C);
					break;
				case "-1%":
					machine.SendControl(0x9D);
					break;
			}
		}

		private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
		{
			if (e.Key == Key.Escape)
			{
				// 1. Laufende Messung abbrechen
				_firstMeasurePoint = null;

				// 2. Texte in der Mess-Box zurücksetzen (statt Debug-Fenster)
				if (TxtDistance != null)
				{
					TxtDistance.Text = "Messung abgebrochen";
				}

				// beim ESC wird auch die Koordinaten-Anzeige geleert
				if (TxtPickedCoords != null) TxtPickedCoords.Text = "X: 0.000 Y: 0.000";

				// Wir markieren das Event als erledigt
				e.Handled = true;
			}
		}

		private void ButtonResetViewport_Click(object sender, RoutedEventArgs e)
		{
			viewport.Camera.Position = new System.Windows.Media.Media3D.Point3D(50, -150, 250);
			viewport.Camera.LookDirection = new System.Windows.Media.Media3D.Vector3D(-50, 150, -250);
			viewport.Camera.UpDirection = new System.Windows.Media.Media3D.Vector3D(0, 0, 1);
		}



		private void UpdateButtonStyles()
		{
			// Mess-Button: Kräftiges Blau wenn aktiv
			if (BtnMeasure.IsChecked == true)
				BtnMeasure.Background = Brushes.DeepSkyBlue;
			else
				BtnMeasure.ClearValue(Control.BackgroundProperty);

			// Link-Button: Kräftiges Grün wenn aktiv
			if (BtnLinkToFile.IsChecked == true)
				BtnLinkToFile.Background = Brushes.LimeGreen;
			else
				BtnLinkToFile.ClearValue(Control.BackgroundProperty);
		}

		private void ExpanderMeasure_Expanded(object sender, RoutedEventArgs e)
		{
			// Automatisch Lay-Flat ausführen. Wir rufen die vorhandene OCP-Funktion auf, die die Kamera flach stellt
			if (viewport != null)
			{
				ButtonLayFlatViewport_Click(null, null);
			}
		}

		private void ExpanderMeasure_Collapsed(object sender, RoutedEventArgs e)
		{
			// Buttons deaktivieren beim Schließen
			if (BtnMeasure != null) BtnMeasure.IsChecked = false;
			if (BtnLinkToFile != null) BtnLinkToFile.IsChecked = false;

			// Farben zurücksetzen
			BtnMeasure?.ClearValue(Control.BackgroundProperty);
			BtnLinkToFile?.ClearValue(Control.BackgroundProperty);

			// Alles im Viewport aufräumen
			ResetMeasurementUI();

			// Ursprüngliche Kamera wiederherstellen
			RestoreOriginalCamera();
		}


		private void BtnMeasure_Click(object sender, RoutedEventArgs e)
		{
			var btn = sender as System.Windows.Controls.Primitives.ToggleButton;
			if (btn == null) return;

			if (btn.IsChecked == true)
			{
				ResetMeasurementUI();

				// Wir rufen Lay-Flat IMMER auf, um sicherzugehen
				ButtonLayFlatViewport_Click(null, null);
				isViewFlat = true;

				// Aktiv-Farbe setzen (überschreibt das Standard-Blau)
				btn.Background = System.Windows.Media.Brushes.LimeGreen;

				if (BtnLinkToFile != null)
				{
					BtnLinkToFile.IsChecked = false;
					BtnLinkToFile.ClearValue(Control.BackgroundProperty);
				}

				_firstMeasurePoint = null;
				TxtDistance.Text = "Click 1st point in viewport";
			}
			else
			{
				btn.ClearValue(Control.BackgroundProperty);
				isViewFlat = false;
				ResetMeasurementUI();
				RestoreOriginalCamera(); // <--- Hier ergänzen
			}
		}

		private void BtnLinkToFile_Click(object sender, RoutedEventArgs e)
		{
			var btn = sender as System.Windows.Controls.Primitives.ToggleButton;
			if (btn == null) return;

			if (btn.IsChecked == true)
			{
				ResetMeasurementUI();

				// 1. Ansicht flach ausrichten
				ButtonLayFlatViewport_Click(null, null);
				isViewFlat = true;

				// Aktiv-Farbe für Link-Button setzen
				btn.Background = System.Windows.Media.Brushes.LimeGreen;

				// 2. Mess-Modus deaktivieren und AUFRÄUMEN
				if (BtnMeasure != null)
				{
					BtnMeasure.IsChecked = false;
					BtnMeasure.ClearValue(Control.BackgroundProperty);
				}
				
				// WICHTIG: Hier rufen wir das Cleanup für den Mess-Marker auf!
				CleanupMeasureMarker();

				// 3. UI-Feedback
				TxtDistance.Text = "---";
				_firstMeasurePoint = null;
				btn.Background = Brushes.LimeGreen;

				// 4. G-Code Logik
				if (ExpanderFile != null) ExpanderFile.IsExpanded = true;
			}
			else
			{
				btn.ClearValue(Control.BackgroundProperty);
				isViewFlat = false;
				ResetMeasurementUI();
				RestoreOriginalCamera(); // <--- Hier ergänzen
			}
		}

		private void CleanupMeasureMarker()
		{
			if (_measureMarker != null)
			{
				if (viewport.Children.Contains(_measureMarker))
				{
					viewport.Children.Remove(_measureMarker);
				}
				_measureMarker = null; // Wichtig: auf null setzen!
			}
			_firstMeasurePoint = null;
		}

		private void ResetMeasurementUI()
		{
			// 1. Blauen Marker (Mess-Anker) entfernen
			if (_measureMarker != null)
			{
				if (viewport.Children.Contains(_measureMarker))
					viewport.Children.Remove(_measureMarker);
				_measureMarker = null;
			}

			// 2. Roten Marker (Auswahl) ausblenden oder entfernen
			// Wenn er beim Messen-Beenden ganz weg soll:
			if (_clickMarker != null && viewport.Children.Contains(_clickMarker))
			{
				viewport.Children.Remove(_clickMarker);
				_clickMarker = null; // Wird bei normalem Click neu erstellt
			}

			// 3. Logik-Variablen zurücksetzen
			_firstMeasurePoint = null;

			// 4. Textfeld leeren
			if (TxtDistance != null)
				TxtDistance.Text = "---";
		}


		private void BtnCopyCoords_Click(object sender, RoutedEventArgs e)
		{
			if (!string.IsNullOrEmpty(TxtPickedCoords.Text))
			{
				// Wir kopieren den Inhalt der TextBox direkt ins Clipboard
				System.Windows.Clipboard.SetText(TxtPickedCoords.Text);

				// Kleiner User-Feedback-Trick: Den Text kurz selektieren
				TxtPickedCoords.Focus();
				TxtPickedCoords.SelectAll();
			}
		}

		private void BtnSendToManual_Click(object sender, RoutedEventArgs e)
		{
			var culture = System.Globalization.CultureInfo.InvariantCulture;
			double safetyZ = 5.0;

			// 1. Die Befehle einzeln bauen (für die Punkt-Korrektur)
			string cmdZ = string.Format(culture, "G0 Z{0:F3}", safetyZ);
			string cmdXY = string.Format(culture, "G0 X{0:F3} Y{1:F3}", _lastX, _lastY);

			// 2. Den kombinierten String für die Weiterverwendung erstellen
			string gcode = cmdZ + Environment.NewLine + cmdXY;

			if (TextBoxManual != null)
			{
				// In die OCP Manual-Box schreiben
				TextBoxManual.Text = gcode;
				TextBoxManual.Focus();
			}
			else
			{
				// Falls die Box nicht da ist: Ab ins Clipboard
				System.Windows.Clipboard.SetText(gcode);
			}
		}

		private double? GetCoord(string line, char axis)
		{
			int idx = line.IndexOf(axis);
			if (idx == -1) return null;

			string s = "";
			for (int i = idx + 1; i < line.Length; i++)
			{
				if (char.IsDigit(line[i]) || line[i] == '.' || line[i] == '-')
					s += line[i];
				else break;
			}

			if (double.TryParse(s, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double res))
				return res;

			return null;
		}

		private void SelectLineInUI(int lineNumber)
		{
			// Wir prüfen zuerst, ob die File-Box (ListBox) überhaupt da ist.
			if (ListViewFile != null)
			{
				// Sicherstellen, dass die Zeilennummer im gültigen Bereich liegt
				if (lineNumber >= 0 && lineNumber < ListViewFile.Items.Count)
				{
					// 1. Die Zeile markieren
					ListViewFile.SelectedIndex = lineNumber;

					// 2. Die Liste automatisch dorthin scrollen, damit die Zeile sichtbar ist
					// Wir zwingen das Scrollen in die nächste UI-Verarbeitungsschleife
					Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ContextIdle, new Action(() =>
					{
						ListViewFile.ScrollIntoView(ListViewFile.Items[lineNumber]);

						// Falls OCP die Selektion visuell "verschluckt", erzwingen wir ein Update
						var container = ListViewFile.ItemContainerGenerator.ContainerFromIndex(lineNumber) as FrameworkElement;
						container?.BringIntoView();
					}));

					// Den Fokus setzen, damit die Zeile farblich hervorgehoben wird
					ListViewFile.Focus();
				}
			}
		}

		private void viewport_MouseDown(object sender, MouseButtonEventArgs e)
		{
			// Nur weitermachen, wenn einer der Modi wirklich aktiv ist
			bool messModusAktiv = BtnMeasure?.IsChecked == true;
			bool linkModusAktiv = BtnLinkToFile?.IsChecked == true;

			if (!messModusAktiv && !linkModusAktiv)
				return;

			Point mousePos = e.GetPosition(viewport);

			// Variablen initialisieren
			double bestDist = 20.0;
			int bestLineNumber = -1;
			Point3D bestHitPoint = new Point3D();
			bool hitFound = false; // <-- Hier definiert

			// 1. bewährter Magnet-Loop
			foreach (var entry in _lineMapping)
			{
				var visual = entry.Key as HelixToolkit.Wpf.LinesVisual3D;
				if (visual == null || visual.Points == null || !visual.IsRendering) continue;

				var indices = entry.Value;
				for (int i = 0; i < visual.Points.Count - 1; i += 2)
				{
					Point3D p1 = visual.Points[i];
					Point3D p2 = visual.Points[i + 1];

					Point screenP1 = Point3DToPoint2D(p1);
					Point screenP2 = Point3DToPoint2D(p2);

					double dist = FindDistanceToSegment(mousePos, screenP1, screenP2);

					// G0 Bestrafung
					double effectiveDist = (visual == ModelRapid) ? dist + 50.0 : dist;

					if (effectiveDist < bestDist)
					{
						bestDist = effectiveDist;
						bestHitPoint = (p1.Z > p2.Z) ? p1 : p2;
						bestLineNumber = indices[i / 2];
						hitFound = true;					// <-- Jetzt wissen wir: Wir haben G-Code getroffen
					}
				}
			}

			// 2. Punkt bestimmen (G-Code Treffer oder freie Fläche)
			Point3D pointToUse;
			if (hitFound)
			{
				pointToUse = bestHitPoint;
			}
			else
			{
				// Manueller Ersatz für FindRay, weil HelixToolkit zickt:
				var viewport3D = viewport.Viewport;
				var hitResult = VisualTreeHelper.HitTest(viewport3D, mousePos) as RayMeshGeometry3DHitTestResult;
				if (hitResult != null)
					pointToUse = hitResult.PointHit;
				else
					pointToUse = new Point3D(0, 0, 0); // Letzter Ausweg
			}

			// 3. Die Mess-Logik mit BtnMeasure
			if (BtnMeasure.IsChecked == true)
			{
				// Radius einmal zentral für diesen Klick berechnen
				double currentRadius = GetDynamicMarkerSize() / 2.0;

				// SICHERHEIT: Falls der rote Marker gelöscht wurde, neu erstellen
				if (_clickMarker == null)
				{
					_clickMarker = new HelixToolkit.Wpf.SphereVisual3D { Fill = System.Windows.Media.Brushes.Red };
					viewport.Children.Add(_clickMarker);
				}

				if (_firstMeasurePoint == null)
				{
					// --- ERSTER PUNKT ---
					_firstMeasurePoint = pointToUse;

					if (TxtDistance != null)
						TxtDistance.Text = "Click 2nd point in viewport";

					// Blauen Marker (Anker) verwalten
					if (_measureMarker != null && viewport.Children.Contains(_measureMarker))
						viewport.Children.Remove(_measureMarker);

					_measureMarker = new HelixToolkit.Wpf.SphereVisual3D
					{
						Radius = currentRadius,
						Fill = System.Windows.Media.Brushes.Blue,
						Center = pointToUse
					};

					viewport.Children.Add(_measureMarker);

					// Roten Marker (Feedback) ebenfalls auf Punkt 1 setzen und Größe anpassen
					if (_clickMarker != null)
					{
						_clickMarker.Radius = currentRadius;
						_clickMarker.Center = pointToUse;
						_clickMarker.Visible = true;
					}
				}
				else
				{
					// --- ZWEITER PUNKT ---
					Point3D p1 = _firstMeasurePoint.Value;
					Point3D p2 = pointToUse;

					// SHIFT-Check für Orthogonal-Modus (H/V)
					if (Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift))
					{
						double deltaX = Math.Abs(p2.X - p1.X);
						double deltaY = Math.Abs(p2.Y - p1.Y);

						if (deltaX > deltaY)
						{
							p2 = new Point3D(p2.X, p1.Y, p1.Z);
						}
						else
						{
							p2 = new Point3D(p1.X, p2.Y, p1.Z);
						}
					}

					double dx = p2.X - p1.X;
					double dy = p2.Y - p1.Y;
					double dz = p2.Z - p1.Z;
					double dist3D = Math.Sqrt(dx * dx + dy * dy + dz * dz);

					if (TxtDistance != null)
						TxtDistance.Text = $"Distance: {dist3D:F3}\n(X:{dx:F2} Y:{dy:F2} Z:{dz:F2})";

					// Roten Marker auf den (korrigierten) Endpunkt setzen und Größe anpassen
					if (_clickMarker != null)
					{
						_clickMarker.Radius = currentRadius;
						_clickMarker.Center = p2;
						_clickMarker.Visible = true;
					}

					_firstMeasurePoint = null;
				}
				return;
			}

			// 4. Normale Selektion (nur wenn nicht gemessen wird)
			if (hitFound)
			{
				ProcessSelection(bestLineNumber, bestHitPoint);
			}
		}

		private Point Point3DToPoint2D(Point3D p)
		{
			// Wir holen uns die "Total Transform" Matrix (Kamera + Viewport).
			// Diese Methode ist der stabilste Teil von HelixToolkit.
			var matrix = HelixToolkit.Wpf.Viewport3DHelper.GetTotalTransform(viewport.Viewport);

			// Wir jagen den 3D-Punkt durch die Matrix
			var transformedPoint = matrix.Transform(p);

			// Das Ergebnis ist ein 2D-Punkt in Pixeln
			return new Point(transformedPoint.X, transformedPoint.Y);
		}

		private double FindDistanceToSegment(Point p, Point a, Point b)
		{
			double dx = b.X - a.X;
			double dy = b.Y - a.Y;
			if (dx == 0 && dy == 0) return Math.Sqrt(Math.Pow(p.X - a.X, 2) + Math.Pow(p.Y - a.Y, 2));

			double t = ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / (dx * dx + dy * dy);
			t = Math.Max(0, Math.Min(1, t)); // Begrenzung auf das Segment

			double closestX = a.X + t * dx;
			double closestY = a.Y + t * dy;

			return Math.Sqrt(Math.Pow(p.X - closestX, 2) + Math.Pow(p.Y - closestY, 2));
		}

		private void ProcessSelection(int lineNumber, Point3D hitPoint)
		{
			if (lineNumber <= 0 || lineNumber > machine.File.Count) return;

			string gcodeLine = machine.File[lineNumber - 1];

			// 1. Wir parsen die Koordinaten aus dem Text (falls vorhanden)
			double? x = GetCoord(gcodeLine, 'X');
			double? y = GetCoord(gcodeLine, 'Y');

			// 2. Fallback: Wenn X oder Y im Text fehlen (wie beim Drill),
			// nutzen wir die Koordinaten des 3D-Treffers.
			double finalX = x ?? hitPoint.X;
			double finalY = y ?? hitPoint.Y;

			// Wir aktualisieren unsere Historie, damit der nächste Klick auf diesen Werten aufbauen kann.
			_lastX = finalX;
			_lastY = finalY;

			// 3. Die Nadel (der rote Ball) setzen
			if (_clickMarker == null)			// Falls sie durch Reset gelöscht wurde: Neu erschaffen
			{
				_clickMarker = new HelixToolkit.Wpf.SphereVisual3D { Fill = System.Windows.Media.Brushes.Red };
				viewport.Children.Add(_clickMarker);
			}

			// Größe aus Settings holen
			_clickMarker.Radius = GetDynamicMarkerSize() / 2.0;
			_clickMarker.Center = new Point3D(finalX, finalY, 0);   // Kugelmarker in die Platinenebene legen -> keine Parallaxe
			_clickMarker.Visible = true;

			// 4. UI-Synchronisation
			SelectLineInUI(lineNumber - 1);
		}

		//----- GetDynamicMarkerSize ---------------------------------------------------
		private double GetDynamicMarkerSize()
		{
			// Fallback, falls noch kein G-Code geladen wurde
			if (diagonal <= 0) return 1.0;

			// Die Logik: Der Marker soll ca. 1-2% der Modellgröße entsprechen,
			// aber niemals kleiner als 0.5mm und niemals größer als 5mm sein.
			double adaptiveSize = diagonal * 0.015;

			return Math.Max(0.5, Math.Min(adaptiveSize, 5.0));
		}

		//----- ButtonLayFlatViewport ---------------------------------------------------
		private void ButtonLayFlatViewport_Click(object sender, RoutedEventArgs e)
		{
			if (viewport == null || viewport.Camera == null) return;

			// 1. Original-Kamera EINMALIG als Werte-Snapshot sichern (bevor wir sie verändern)
			if (!_hasSavedCamera && viewport.Camera is ProjectionCamera projCam)
			{
				_savedPosition = projCam.Position;
				_savedLookDirection = projCam.LookDirection;
				_savedUpDirection = projCam.UpDirection;
				_savedIsOrtho = projCam is OrthographicCamera;

				if (_savedIsOrtho && projCam is OrthographicCamera ortho)
				{
					_savedOrthoWidth = ortho.Width;
				}
				else if (!_savedIsOrtho && projCam is PerspectiveCamera pers)
				{
					_savedPerspectiveFOV = pers.FieldOfView;
				}
				_hasSavedCamera = true;
			}

			// --- ORIENTIERUNG BEIBEHALTEN ---
			// Wir nehmen direkt den aktuellen Up-Vektor der Kamera. 
			// Hier ist deine für die Fräse eingestellte Drehung (z.B. um 90°) sicher hinterlegt!
			Vector3D currentUp = viewport.Camera.UpDirection;
			// --- IMMER DIE FESTE CNC-AUSRICHTUNG NUTZEN ---
			Vector3D upDir = _cncUpDirection; 
			//Vector3D upDir = new Vector3D(currentUp.X, currentUp.Y, 0);

			if (upDir.Length > 0.001)
			{
				upDir.Normalize(); // Bringt den Vektor exakt auf Länge 1.0 -> behält die 90°-Drehung, verhindert aber jegliches Schrägstehen
			}
			else
			{
				upDir = new Vector3D(0, 1, 0); // Fallback, falls der Vektor kollabiert ist
			}

			/*			// Wir schauen, in welche Richtung die Kamera horizontal blickt (X und Y).
						// Das ist die Richtung, die der User mit "ButtonRotateOrigin" eingestellt hat.
						double targetUpX = viewport.Camera.LookDirection.X;
						double targetUpY = viewport.Camera.LookDirection.Y;

						// Falls wir schon flach schauen (X und Y fast 0), nehmen wir die aktuelle UpDirection,
						// um die Drehung nicht zu verlieren.
						if (Math.Abs(targetUpX) < 0.01 && Math.Abs(targetUpY) < 0.01)
						{
							targetUpX = viewport.Camera.UpDirection.X;
							targetUpY = viewport.Camera.UpDirection.Y;
						}

						// Wir "frieren" diese Richtung für die Oben-Ausrichtung ein (Z auf 0)
						// Falls beides 0 ist (Notfall-Fallback), nehmen wir Y als oben.
						if (Math.Abs(targetUpX) < 0.01 && Math.Abs(targetUpY) < 0.01)
						{
							targetUpY = 1;
						}

						// Sauber normalisieren, um jegliches Schrägstehen auf dem Monitor zu verhindern
						Vector3D upDir = new Vector3D(targetUpX, targetUpY, 0);
						if (upDir.Length > 0.001)
							upDir.Normalize();
						else
							upDir = new Vector3D(0, 1, 0);
			*/

			// 2. Bounding Box ohne "FindBounds" berechnen
			var totalBounds = System.Windows.Media.Media3D.Rect3D.Empty;

			foreach (var child in viewport.Children)
			{
				// Wir suchen gezielt nach den Linien des G-Codes
				// In OCP sind das oft LinesVisual3D
				if (child is HelixToolkit.Wpf.LinesVisual3D lines)
				{
					// Wir ignorieren die Maschinengrenzen anhand ihrer Farbe oder ihres Namens
					// Die Maschinengrenzen sind in OCP oft Grau oder Weiß. 
					// Wenn die G-Code-Linien eine andere Farbe haben, können wir filtern:
					if (lines.Color == System.Windows.Media.Colors.LightGray) continue;

					// Falls das Objekt eine Content-Eigenschaft hat, nehmen wir deren Bounds
					var bounds = lines.Content.Bounds;
					if (!bounds.IsEmpty)
						totalBounds.Union(bounds);
				}
			}

			// --- JETZT AUF ORTHOGONALE KAMERA UMSCHALTEN ---
			double centerX = totalBounds.IsEmpty ? 0 : totalBounds.X + totalBounds.SizeX / 2.0;
			double centerY = totalBounds.IsEmpty ? 0 : totalBounds.Y + totalBounds.SizeY / 2.0;
			double maxDim = totalBounds.IsEmpty ? 100 : Math.Max(totalBounds.SizeX, totalBounds.SizeY);
			if (maxDim <= 0) maxDim = 100;

			var orthoCam = new OrthographicCamera
			{
				Position = new Point3D(centerX, centerY, maxDim + 200.0), // Weit genug oben auf Z
				LookDirection = new Vector3D(0, 0, -1),                   // Exakt senkrecht nach unten
				UpDirection = upDir,                                      // Perfekt waagerecht/senkrecht zum Monitor
				Width = maxDim * 1.25                                     // Passt den Zoom perfekt an die Platine an
			};

			viewport.Camera = orthoCam;
			isViewFlat = true;
			TxtDistance.Foreground = System.Windows.Media.Brushes.DarkGreen;

			// 3. Zoom ausführen
			if (!totalBounds.IsEmpty)
			{
				// Wir berechnen 15% Puffer
				double margin = 0.15;
				double offsetX = totalBounds.SizeX * margin;
				double offsetY = totalBounds.SizeY * margin;

				// Wir erstellen eine neue, größere Box basierend auf der alten
				var paddedBounds = new System.Windows.Media.Media3D.Rect3D(
					totalBounds.X - offsetX / 2,
					totalBounds.Y - offsetY / 2,
					totalBounds.Z,
					totalBounds.SizeX + offsetX,
					totalBounds.SizeY + offsetY,
					totalBounds.SizeZ
				);

				// Jetzt zoomen wir auf die gepufferte Box
				viewport.ZoomExtents(paddedBounds, 0);      // "0" verhindert das Hereingleiten von links oben
			}
			else
			{
				// Fallback: Wenn wir nichts spezifisches finden, nimm alles
				viewport.ZoomExtents();
			}

			isViewFlat = true;
			TxtDistance.Foreground = System.Windows.Media.Brushes.DarkGreen;
		}

		// Wird automatisch aufgerufen, sobald du die Kamera mit der Maus drehst/bewegst
		private void viewport_CameraChanged(object sender, RoutedEventArgs e)
		{
			if (TxtPickedCoords == null || viewport.Camera == null) return;

			var look = viewport.Camera.LookDirection;

			// Normalerweise ist die Ansicht flach, wenn X fast 0 ist.
			// Wir erlauben jetzt eine massive Abweichung. 
			// Erst wenn die Kamera wirklich spürbar gekippt wird, schalten wir ab.
			bool stillFlat = Math.Abs(look.X) < 0.2;

			if (stillFlat)
			{
				isViewFlat = true;
				TxtPickedCoords.Foreground = System.Windows.Media.Brushes.DarkGreen;
				// WICHTIG: Hier keinen Text überschreiben, sonst löscht jeder Zoom die Zahlen!
			}
			else
			{
				isViewFlat = false;
				TxtPickedCoords.Foreground = System.Windows.Media.Brushes.Gray;
				TxtPickedCoords.Text = "X: --- | Y: --- (3D-Modus)";
				TxtDistance.Text = "Distance: ---";
			}
		}

		// ----- ButtonRotateOrigin ---------------------------------------------------
		private void ButtonRotateOrigin_Click(object sender, RoutedEventArgs e)
		{
			if (viewport == null || viewport.Camera == null) return;

			// Aktuelles "Oben" holen
			var up = viewport.Camera.UpDirection;

			// Drehung 90° um die Z-Achse: (x, y) -> (-y, x)
			Vector3D newUp = new Vector3D(up.Y, -up.X, 0);

			// --- WICHTIG: NORMALISIEREN ---
			// Das stellt sicher, dass der Vektor wieder genau die Länge 1.0 hat.
			// Ohne das wird der Vektor bei jedem Klick kürzer, bis das Bild verschwindet.
			if (newUp.Length > 0)
				newUp.Normalize();
			else
				newUp = new Vector3D(0, 1, 0); // Notfall-Fallback

			// --- NEU: In unserer CNC-Variablen speichern ---
			_cncUpDirection = newUp; 
			
			viewport.Camera.UpDirection = newUp;

			// Wenn wir im Flach-Modus sind, Zoom neu berechnen
			if (isViewFlat)
			{
				ButtonLayFlatViewport_Click(null, null);
			}
		}

		// ----- ButtonRestoreViewport ---------------------------------------------------
		private void ButtonRestoreViewport_Click(object sender, RoutedEventArgs e)
		{
			string[] scoords = Properties.Settings.Default.ViewPortPos.Split(';');

			try
			{
				IEnumerable<double> coords = scoords.Select(s => double.Parse(s));
				double[] cArray = coords.ToArray();

				viewport.Camera.Position = new Vector3(coords.Take(3).ToArray()).ToPoint3D();
				viewport.Camera.LookDirection = new Vector3(coords.Skip(3).Take(3).ToArray()).ToVector3D();  // deHarro, 2024-09-08, nur 3 Werte für Vektor
				if (cArray.Length >= 9)
				{
					viewport.Camera.UpDirection = new Vector3(cArray.Skip(6).Take(3).ToArray()).ToVector3D();
					_cncUpDirection = viewport.Camera.UpDirection; // <--- Hier direkt für die CNC sichern				
				}
			}
			catch
			{
				ButtonResetViewport_Click(null, null);
			}

			if (viewport.Camera is System.Windows.Media.Media3D.ProjectionCamera c)
			{
				// Prüfen, ob Blickrichtung und Oben-Vektor parallel sind
				double dot = Math.Abs(c.LookDirection.X * c.UpDirection.X +
									  c.LookDirection.Y * c.UpDirection.Y +
									  c.LookDirection.Z * c.UpDirection.Z);

				// Falls sie fast parallel sind (Skalarprodukt nahe Maximum), erzwinge Y-Up
				if (dot > 0.99)
				{
					c.UpDirection = new System.Windows.Media.Media3D.Vector3D(0, 1, 0);
				}
			}
		}

		// ----- ButtonSaveViewport ---------------------------------------------------
		private void ButtonSaveViewport_Click(object sender, RoutedEventArgs e)
		{
			List<double> coords = new List<double>();

			coords.AddRange(new Vector3(viewport.Camera.Position).Array);
			coords.AddRange(new Vector3(viewport.Camera.LookDirection).Array);
			// UpDirection hinzufügen (3 zusätzliche Werte)
			coords.AddRange(new Vector3(viewport.Camera.UpDirection).Array);

			Properties.Settings.Default.ViewPortPos = string.Join(";", coords.Select(d => d.ToString()));
			Properties.Settings.Default.Save();
		}

		private void ButtonSaveTLOPos_Click(object sender, RoutedEventArgs e)
		{
			if (machine.Mode != Machine.OperatingMode.Manual)
				return;

			double Z = (Properties.Settings.Default.TLSUseActualPos) ? machine.MachinePosition.Z : LastProbePosMachine.Z;

			Properties.Settings.Default.ToolLengthSetterPos = Z;
		}

		private void ButtonApplyTLO_Click(object sender, RoutedEventArgs e)
		{
			if (machine.Mode != Machine.OperatingMode.Manual)
				return;

			double Z = (Properties.Settings.Default.TLSUseActualPos) ? machine.MachinePosition.Z : LastProbePosMachine.Z;

			double delta = Z - Properties.Settings.Default.ToolLengthSetterPos;

			machine.SendLine($"G43.1 Z{delta.ToString(Constants.DecimalOutputFormat)}");
		}

		private void ButtonClearTLO_Click(object sender, RoutedEventArgs e)
		{
			if (machine.Mode != Machine.OperatingMode.Manual)
				return;

			machine.SendLine("G49");
		}

		protected override void OnSourceInitialized(EventArgs e)
		{
			base.OnSourceInitialized(e);
			HwndSource source = PresentationSource.FromVisual(this) as HwndSource;
			source.AddHook(WndProc);
		}

		private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
		{
			if (msg == App.WM_COPYDATA)
			{
				App.COPYDATASTRUCT _dataStruct = Marshal.PtrToStructure<App.COPYDATASTRUCT>(lParam);
				string _strMsg = Marshal.PtrToStringUni(_dataStruct.lpData, _dataStruct.cbData / 2);
				if (File.Exists(_strMsg))
				{
					Activate();
					openFileDialogGCode.FileName = _strMsg;
					OpenFileDialogGCode_FileOk(null, null);
				}
			}

			return IntPtr.Zero;
		}


		public void UpdateLineMapping(System.Windows.Media.Media3D.Visual3D visual, List<int> lines)
		{
			// Wir speichern die Liste der Zeilennummern für dieses 3D-Objekt
			_lineMapping[visual] = lines;
			System.Diagnostics.Debug.WriteLine($"Mapping aktualisiert: {lines.Count} Zeilen für Visual {visual.GetType().Name} registriert.");
		}


		// Diese kleine Klasse hilft uns, Name und Inhalt der Datei zu speichern
		public class GCodeLayer : INotifyPropertyChanged
		{
			public string Name { get; set; }
			public string[] Content { get; set; }
			public string Filename { get; set; }
			public bool IsActive { get; set; } = true;

			// Logik für das Ausgrauen der Pfeile
			private bool _isNotFirst = true;
			public bool IsNotFirst
			{
				get => _isNotFirst;
				set
				{
					if (_isNotFirst == value) return;
					_isNotFirst = value;
					OnPropertyChanged("IsNotFirst");
				}
			}

			private bool _isNotLast = true;
			public bool IsNotLast
			{
				get => _isNotLast;
				set
				{
					if (_isNotLast == value) return;
					_isNotLast = value;
					OnPropertyChanged("IsNotLast");
				}
			}

			// --- Ab hier: Das "Sprachrohr" zum UI ---
			public event PropertyChangedEventHandler PropertyChanged;
			protected void OnPropertyChanged(string name)
			{
				PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
			}
		} // Ende GCodeLayer

		public void ApplyGlobalViewportStandard()
		{

			if (viewport == null) return;

			// Wir bleiben beim bewährten ContextIdle
			this.Dispatcher.InvokeAsync(() =>
			{
				// 1. Kamera-Konflikt lösen
				if (viewport.Camera is System.Windows.Media.Media3D.ProjectionCamera cam)
				{
					// Nur eingreifen, wenn Look und Up fast parallel sind (Z-Z-Bug)
					if (Math.Abs(cam.UpDirection.Z) > 0.9 && Math.Abs(cam.LookDirection.X) < 0.1)
					{
						cam.UpDirection = new System.Windows.Media.Media3D.Vector3D(0, 1, 0);
					}
				}

				// 2. Bounding Box berechnen
				var totalBounds = System.Windows.Media.Media3D.Rect3D.Empty;

				foreach (var child in viewport.Children)
				{
					if (child is HelixToolkit.Wpf.LinesVisual3D visualLines)
					{
						if (visualLines.Color == System.Windows.Media.Colors.LightGray) continue;

						if (visualLines.Content != null)
						{
							var bounds = visualLines.Content.Bounds;
							if (!bounds.IsEmpty)
								totalBounds.Union(bounds);
						}
					}
				}

				double newMarkerRadius = 0.5; // Absoluter Notfall-Fallback

				if (!totalBounds.IsEmpty)
				{
					// Diagonale des aktuell sichtbaren Modells berechnen
					diagonal = Math.Sqrt(Math.Pow(totalBounds.SizeX, 2) + Math.Pow(totalBounds.SizeY, 2) + Math.Pow(totalBounds.SizeZ, 2));

					// Basiswert aus Settings holen (MarkerSize)
					double baseFromSettings = Properties.Settings.Default.MarkerSize;
					if (baseFromSettings <= 0) baseFromSettings = 0.5;

					// 2. Die Berechnung (Linearer Ansatz)
					// Wir lassen den Marker pro 100mm Diagonale um einen festen Prozentsatz wachsen.
					// Beispiel: 0.2 (20%) Zuwachs pro 100mm.
					if (diagonal > 1.0)
					{
						double growthRate = 0.2;
						double growthFactor = 1.0 + (diagonal / 100.0 * growthRate);

					// 3. Verrechnung
						newMarkerRadius = baseFromSettings * growthFactor;
					}
					else
					{
						// Fallback: Nur der Setting-Wert, wenn kein Modell da ist
						newMarkerRadius = baseFromSettings;
					}
					// 4. Leitplanken (Sicherheit geht vor)
					newMarkerRadius = Math.Max(0.5, Math.Min(newMarkerRadius, 4.0));
				}
				else
				{
					diagonal = 0;
					newMarkerRadius = Properties.Settings.Default.MarkerSize;
				}

				// Radien anwenden
				if (_clickMarker != null)
				{ 
					_clickMarker.Radius = newMarkerRadius; 
					_clickMarker.Visible = false; 
				}
				if (_measureMarker != null)
				{
					_measureMarker.Radius = newMarkerRadius;
					_measureMarker.Visible = false;
				}
				// ----------------------------------------

				// 3. Gezielter Zoom 
				if (!totalBounds.IsEmpty)
				{
					double margin = 0.15;
					double offsetX = totalBounds.SizeX * margin;
					double offsetY = totalBounds.SizeY * margin;

					var paddedBounds = new System.Windows.Media.Media3D.Rect3D(
						totalBounds.X - offsetX / 2,
						totalBounds.Y - offsetY / 2,
						totalBounds.Z,
						totalBounds.SizeX + offsetX,
						totalBounds.SizeY + offsetY,
						totalBounds.SizeZ
					);

					viewport.ZoomExtents(paddedBounds, 0);
				}
				else
				{
					// Falls kein G-Code da ist (Start), zeige den Tisch-Ursprung
					// verhindert, dass man beim Start ins Leere schaut.
					viewport.ZoomExtents(new System.Windows.Media.Media3D.Rect3D(-10, -10, 0, 120, 120, 1), 0);
				}
			}, System.Windows.Threading.DispatcherPriority.ContextIdle);
		} // Ende ApplyGlobalViewportStandard

		private void SyncMachineWithLayers()
		{
			// 1. G-Code aus allen aktiven Layern zusammenbauen
			string totalGCode = GetCombinedGCode();
			if (string.IsNullOrWhiteSpace(totalGCode)) return;

			// 2. Zeilen splitten
			string[] finalLines = totalGCode.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

			// 3. Der Maschine übergeben (Das triggert den internen Parser)
			machine.SetFile(finalLines);

			// 4. UI-Längenanzeige
			if (RunFileLength != null)
				RunFileLength.Text = finalLines.Length.ToString();

			// 5. Memory-Leak-Bremse für große PCB-Dateien
			if (finalLines.Length > 20000)
			{
				GC.Collect(1);			// Garbage colletion ausführen
			}
		}

		// maxFeedRate für X-, Y- und Z-Achse von GRBL abfragen und in den Settings von OCP eintragen.
		// Wird für Joystick und Gamepad benötigt.
		// Dadurch entfällt das manuelle Einpflegen der Werte in die OCP Settings.
		private void FetchGrblMaxFeeds()
		{
			if (!machine.Connected) return;

			bool receivedAnySetting = false;

			Action<string> grblSettingsHandler = null;
			grblSettingsHandler = (line) =>
			{
				// Wir suchen nach den Zeilen $110=..., $111=... und $112=...
				if (line.StartsWith("$") && line.Contains("="))
				{
					string[] parts = line.Split('=');
					if (parts.Length == 2)
					{
						string id = parts[0].Trim();
						if (double.TryParse(parts[1], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double val))
						{
							if (id == "$110") Properties.Settings.Default.JoystickMaxFeedX = val;
							if (id == "$111") Properties.Settings.Default.JoystickMaxFeedY = val;
							if (id == "$112") Properties.Settings.Default.JoystickMaxFeedZ = val;
							receivedAnySetting = true; // Wir haben echte Settings gesehen!
						}
					}
				}
				// Nur abmelden, wenn wir vorher auch wirklich schon Einstellungsdaten verarbeitet haben
				else if (line == "ok" && receivedAnySetting)
				{
					Properties.Settings.Default.Save();
					machine.LineReceived -= grblSettingsHandler;
					Console.WriteLine("GRBL Max-Feeds erfolgreich im Hintergrund aktualisiert.");
				}
			};

			// Handler aktivieren und Befehl senden
			machine.LineReceived += grblSettingsHandler;
			machine.SendLine("$$");
		}


		#region DKW Modul Brücke

		private void OnSetRefZ(object sender, RoutedEventArgs e)
		{
			// Wir nutzen MachinePosition.Z statt LastAbsZ
			if (dkw.Start.X == 0 && dkw.Start.Y == 0)
			{
				dkw.RefZProbe = machine.MachinePosition.Z;
				textDKWStatus.Text = "Ref-Z: Nadel (Taster) gespeichert.";
			}
			else
			{
				dkw.RefZMill = machine.MachinePosition.Z;
				textDKWStatus.Text = "Ref-Z: Fräser gespeichert.";
			}
		}

		// --- DKW Modul Logik ---

		private void ProbeAndSavePoint(int pointIndex)
		{
			// 1. Aktuelle Werkstück-Koordinaten berechnen
			// (Maschinenposition abzüglich des Nullpunkt-Offsets)
			double x = machine.MachinePosition.X - machine.WorkOffset.X;
			double y = machine.MachinePosition.Y - machine.WorkOffset.Y;
			double z = machine.MachinePosition.Z - machine.WorkOffset.Z;

			// 2. Falls der Simulations-Modus AUS ist (Checkbox nicht angewählt)
			if (chkDKWTestMode.IsChecked == false)
			{
				if (!machine.Connected)
				{
					textDKWStatus.Text = "Status: Nicht verbunden!";
					return;
				}

				// Sicherheitshöhe (Absolut) merken
				double safetyZ = machine.MachinePosition.Z;

				// Echter Tastvorgang
				machine.SendLine("G91 G38.2 Z-10 F20"); // 10mm nach unten tasten
				machine.SendLine($"G90 G0 Z{safetyZ}");  // Zurück auf Sicherheitshöhe

				// Nach dem Tasten die neuen Koordinaten nehmen
				x = machine.MachinePosition.X - machine.WorkOffset.X;
				y = machine.MachinePosition.Y - machine.WorkOffset.Y;
				z = machine.MachinePosition.Z - machine.WorkOffset.Z;
			}

			// 3. Visualisierung & Speicherung (jetzt einheitlich)
			var displayPt = new Point3DCollection { new Point3D(x, y, z + 0.5) };

			if (pointIndex == 0)
			{
				dkw.SetStart(x, y, z);
				DKWStartPoint.Points = displayPt;
				// Aufräumen bei Neustart
				DKWMidPoint.Points = null;
				DKWEndPoint.Points = null;
				DKWArcPreview.Points = null;
			}
			else if (pointIndex == 1)
			{
				dkw.SetMid(x, y, z);
				DKWMidPoint.Points = displayPt;
			}
			else if (pointIndex == 2)
			{
				dkw.SetEnd(x, y, z);
				DKWEndPoint.Points = displayPt;

				if (dkw.CalculatedRadius > 0)
				{
					DKWArcPreview.Points = dkw.GetArcPoints();
				}
			}

			textDKWStatus.Text = dkw.Status;
			UpdateButtonStates();

			Application.Current.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, new Action(() =>
			{
				// Falls OCP eine spezielle TextBox für das Jogging nutzt (oft 'FocusHolder'):
				TextBoxJog.Focus(); 
			}));
		}

		private void OnSetStart(object sender, RoutedEventArgs e) => ProbeAndSavePoint(0);
		private void OnSetMid(object sender, RoutedEventArgs e) => ProbeAndSavePoint(1);
		private void OnSetEnd(object sender, RoutedEventArgs e) => ProbeAndSavePoint(2);

		/*		private void UpdateButtonStates()
				{
					if (dkw == null) return;

					// Prüfen, ob wir valide Geometrie haben
					bool geometryReady = dkw.CalculatedRadius > 0;

					// Prüfen, ob wir bereits Messpunkte aus dem Auto-Probe haben
					bool hasMeasuredPoints = dkw.MeasuredWallPoints.Count > 0;

					// Set-Buttons: Mid braucht Start, End braucht Mid
					if (btnSetMid != null) btnSetMid.IsEnabled = (dkw.Start.X != 0 || dkw.Start.Y != 0);
					if (btnSetEnd != null) btnSetEnd.IsEnabled = (dkw.Mid.X != 0 || dkw.Mid.Y != 0);

					// START AUTO-PROBE: Aktivieren, sobald der Kreis berechnet wurde
					if (btnStartAutoProbe != null) btnStartAutoProbe.IsEnabled = geometryReady;

					// START MILLING: Erst aktivieren, wenn wir die Schiene tatsächlich getastet haben
					if (btnStartMilling != null) btnStartMilling.IsEnabled = hasMeasuredPoints;
				}
		*/

		private void UpdateButtonStates()
		{
			if (dkw == null) return;

			// 1. Status-Flags ermitteln
			bool hasStart = dkw.Start.X != 0 || dkw.Start.Y != 0;
			bool hasMid = dkw.Mid.X != 0 || dkw.Mid.Y != 0;
			bool geometryReady = dkw.CalculatedRadius > 0;
			bool hasMeasuredPoints = dkw.MeasuredWallPoints != null && dkw.MeasuredWallPoints.Count > 0;

			// 2. Buttons aktivieren/deaktivieren
			if (btnSetMid != null) btnSetMid.IsEnabled = hasStart;
			if (btnSetEnd != null) btnSetEnd.IsEnabled = hasMid;

			// START AUTO-PROBE: Nur wenn Radius berechnet
			if (btnStartAutoProbe != null) btnStartAutoProbe.IsEnabled = geometryReady;

			// START MILLING: Erst wenn Messdaten vorhanden sind
			if (btnStartMilling != null) btnStartMilling.IsEnabled = hasMeasuredPoints;

			// 3. Dynamische Status-Texte (Englisch)
			if (textDKWStatus != null)
			{
				if (!hasStart)
					textDKWStatus.Text = "Status: Please set Start point.";
				else if (!hasMid)
					textDKWStatus.Text = "Status: Start set - next: set Mid point.";
				else if (!geometryReady)
					textDKWStatus.Text = "Status: Mid set - next: set End point.";
				else if (!hasMeasuredPoints)
					textDKWStatus.Text = $"Status: Ready - Radius: {dkw.CalculatedRadius:F1} mm. Next: START AUTO-PROBE.";
				else
					textDKWStatus.Text = "Status: Probing complete. Ready to START MILLING.";
			}
		}

		private async void OnStartAutoProbe(object sender, RoutedEventArgs e)
		{
			if (dkw == null || machine == null) return; 
			if (dkw.CalculatedRadius <= 0) return;

			if (ButtonDisconnect != null) ButtonDisconnect.IsEnabled = false;

			// Das ist unser digitaler "Wartezettel"
			TaskCompletionSource<bool> grblReady = null;

			// Der Handler, der auf das RaiseEvent(LineReceived, line) reagiert
			// Der Handler als einfache Action (passend zur Machine-Klasse)
			Action<string> lineHandler = null;
			lineHandler = (line) =>
			{
				if (line == "ok" || line.StartsWith("[PRB:"))
				{
					grblReady?.TrySetResult(true);
				}
			};

			// Event abonnieren
			machine.LineReceived += lineHandler; 
			
			try
			{
				// Werte aus der GUI holen
				if (double.TryParse(txtProbeDiam.Text, out double pDiam)) dkw.ProbeDiameter = pDiam;

				// Sicherheitshöhe (2mm über aktueller Z-Maschinenposition)
				double safetyZ = machine.MachinePosition.Z + 2.0;

				var tasks = dkw.GenerateProbeTasks(2.0); // Alle 2mm ein Punkt
				textDKWStatus.Text = $"Starte Messung: {tasks.Count} Punkte.";

				foreach (var task in tasks)
				{
					// 1. Über Punkt fahren (G54 System)
					machine.SendLine($"G0 Z{safetyZ:F3}");
					machine.SendLine($"G0 X{task.EntryPoint.X + machine.WorkOffset.X:F3} Y{task.EntryPoint.Y + machine.WorkOffset.Y:F3}");

					// 2. Eintauchen
					//machine.SendLine($"G0 Z{task.EntryPoint.Z + machine.WorkOffset.Z:F3}");
					// 2. Vorsichtiges Eintauchen (G1 statt G0 ist sicherer für den 0,6mm Draht!)
					machine.SendLine($"G1 Z{task.EntryPoint.Z + machine.WorkOffset.Z:F3} F100");

					// 3. Tasten (G38.2)
					// Hier nutzen wir ein Delay als einfachste Form der Synchronisation
					// (Für eine echte Event-Abfrage müssten wir tiefer in die OCP-Kommunikation)
					machine.SendLine($"G38.2 X{task.TargetPoint.X + machine.WorkOffset.X:F3} Y{task.TargetPoint.Y + machine.WorkOffset.Y:F3} F20");

					// Wir geben GRBL Zeit zum Antworten (einfaches Delay für den ersten Test)
					//await System.Threading.Tasks.Task.Delay(1500); // Warten auf GRBL-Antwort

					// WARTEN: Hier pausiert der Code, bis das 'ok' vom RaiseEvent kommt (max. 10 Sek)
					var completedTask = await Task.WhenAny(grblReady.Task, Task.Delay(10000));

					if (completedTask != grblReady.Task)
					{
						textDKWStatus.Text = "Timeout: GRBL antwortet nicht!";
						break;
					}

					// 4. Punkt speichern (Wir nutzen die Position nach dem Stop)
					dkw.AddMeasuredPoint(machine.MachinePosition.X, machine.MachinePosition.Y, machine.MachinePosition.Z,
										 machine.WorkOffset.X, machine.WorkOffset.Y);

					// Visualisierung aktualisieren
					if (DKWMeasuredPoints != null)
						DKWMeasuredPoints.Points = new Point3DCollection(dkw.MeasuredWallPoints);

					// 5. Kurzer Rückzug
					machine.SendLine($"G0 X{task.EntryPoint.X + machine.WorkOffset.X:F3} Y{task.EntryPoint.Y + machine.WorkOffset.Y:F3}");
				}

				machine.SendLine($"G0 Z{safetyZ:F3}");
				textDKWStatus.Text = "Messung beendet.";
			}
			catch (Exception ex)
			{
				textDKWStatus.Text = "Fehler: " + ex.Message;
			}
			finally
			{
				// GANZ WICHTIG: Event-Handler wieder entfernen, sonst hagelt es Fehlermeldungen
				machine.LineReceived -= lineHandler;                
				
				// SPERRE AUFHEBEN (wird IMMER ausgeführt, egal was passiert)
				if (ButtonDisconnect != null) ButtonDisconnect.IsEnabled = true;
				UpdateButtonStates();
			}
		}

		private async void OnStartMilling(object sender, RoutedEventArgs e)
		{
			if (dkw.MeasuredWallPoints.Count < 2)
			{
				MessageBox.Show("Keine Messdaten vorhanden. Bitte zuerst AUTO-PROBE ausführen.");
				return;
			}

			// 1. Werte aus der GUI parsen
			double totalRemoval = 0; // Wie viel muss seitlich weg?
			double stepoverH = 0;    // Horizontale Zustellung
			double stepdownV = 0;    // Vertikale Zustellung pro Durchgang

			double.TryParse(txtWallRemoval.Text, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out totalRemoval);
			double.TryParse(txtStepover.Text, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out stepoverH);
			double.TryParse(txtStepdown.Text, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out stepdownV);

			// Sicherheitschecks für die Berechnungen
			if (totalRemoval < 0) totalRemoval = 0;
			if (stepoverH <= 0) stepoverH = totalRemoval > 0 ? totalRemoval : 0.1;
			if (stepdownV <= 0) stepdownV = 100; // Wenn 0 oder falsch, alles in einem Z-Schritt (großer Wert)

			if (ButtonDisconnect != null) ButtonDisconnect.IsEnabled = false;

			try
			{
				double millRadius = dkw.MillDiameter / 2.0;
				double safetyZ = machine.MachinePosition.Z + 2.0;
				double feedRate = 200;

				// Berechnung der Durchgänge
				int passesH = (int)Math.Ceiling(totalRemoval / stepoverH);

				// Wir ermitteln die tiefste Stelle aus den Messpunkten
				double minZ = 0;
				foreach (var p in dkw.MeasuredWallPoints) if (p.Z < minZ) minZ = p.Z;

				// Anzahl der vertikalen Schnitte (Z-Zustellung)
				int passesV = (int)Math.Ceiling(Math.Abs(minZ) / stepdownV);
				if (passesV < 1) passesV = 1;

				textDKWStatus.Text = "Fräsvorgang startet...";
				machine.SendLine($"G0 Z{safetyZ:F3}");

				// --- SCHLEIFE 1: VERTIKALE EBENEN (Z) ---
				for (int v = 1; v <= passesV; v++)
				{
					// Aktuelle Frästiefe berechnen (Z ist negativ)
					double currentZ = -(v * stepdownV);
					if (currentZ < minZ) currentZ = minZ; // Nicht tiefer als gemessen

					// --- SCHLEIFE 2: HORIZONTALE BAHNEN (XY-Versatz) ---
					for (int h = 1; h <= passesH; h++)
					{
						double currentOffset = totalRemoval - (h * stepoverH);
						if (currentOffset < 0) currentOffset = 0;

						textDKWStatus.Text = $"Z:{currentZ:F1} | Bahn {h}/{passesH} (Offset: {currentOffset:F2})";

						// --- SCHLEIFE 3: DER PFAD (Punkte) ---
						for (int i = 0; i < dkw.MeasuredWallPoints.Count; i++)
						{
							Point3D wallPt = dkw.MeasuredWallPoints[i];

							double dx = wallPt.X - dkw.CircleCenter.X;
							double dy = wallPt.Y - dkw.CircleCenter.Y;
							double dist = Math.Sqrt(dx * dx + dy * dy);

							double ux = dx / dist;
							double uy = dy / dist;

							double targetDist = millRadius + currentOffset;
							double millX = wallPt.X - (ux * targetDist);
							double millY = wallPt.Y - (uy * targetDist);

							double gX = millX + machine.WorkOffset.X;
							double gY = millY + machine.WorkOffset.Y;
							double gZ = currentZ + machine.WorkOffset.Z; // Nutze die aktuelle Ebenen-Tiefe

							if (i == 0)
							{
								// Anfahren und Eintauchen auf aktuelle Ebene
								machine.SendLine($"G0 X{gX:F3} Y{gY:F3}");
								machine.SendLine($"G1 Z{gZ:F3} F100");
							}
							else
							{
								machine.SendLine($"G1 X{gX:F3} Y{gY:F3} F{feedRate}");
							}

							await System.Threading.Tasks.Task.Delay(50);
						}

						// Nach jeder horizontalen Bahn kurz abheben zur Sicherheit
						machine.SendLine($"G0 Z{safetyZ:F3}");
					}
				}

				textDKWStatus.Text = "Fräsvorgang erfolgreich beendet.";
			}
			catch (Exception ex)
			{
				MessageBox.Show("Fehler beim Fräsen: " + ex.Message);
			}
			finally
			{
				if (ButtonDisconnect != null) ButtonDisconnect.IsEnabled = true;
				UpdateButtonStates();
			}
		}
		#endregion

		public void UpdateGamepadVisuals(bool isEnabled, bool isLocked)
		{
			if (!isEnabled)
			{
				// Controller komplett AUS -> Icon verstecken und Animation killen
				GamepadOverlay.Visibility = Visibility.Collapsed;
				GamepadRing.BeginAnimation(UIElement.OpacityProperty, null);
				return;
			}

			// Controller ist EIN -> Icon einblenden
			GamepadOverlay.Visibility = Visibility.Visible;

			if (isLocked)
			{
				// Achsensperre aktiv -> Ring Grün und sanftes Blinken starten
				GamepadRing.Stroke = System.Windows.Media.Brushes.LimeGreen;

				var blinkAnimation = new System.Windows.Media.Animation.DoubleAnimation(
					fromValue: 0.2,
					toValue: 1.0,
					duration: new System.Windows.Duration(TimeSpan.FromMilliseconds(400)))
				{
					AutoReverse = true,
					RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever
				};
				GamepadRing.BeginAnimation(UIElement.OpacityProperty, blinkAnimation);
			}
			else
			{
				// Normaler Modus -> Ring Rot, vollflächig sichtbar
				GamepadRing.Stroke = System.Windows.Media.Brushes.Red;
				GamepadRing.BeginAnimation(UIElement.OpacityProperty, null); // Blinken stoppen
				GamepadRing.Opacity = 1.0;
			}
		}

		public void UpdateJoystickVisuals(bool isEnabled)
		{
			if (!isEnabled)
			{
				// Joystick AUS -> Icon verstecken
				JoystickOverlay.Visibility = Visibility.Collapsed;
			}
			else
			{
				// Joystick EIN -> Icon mit blauem Ring einblenden
				JoystickOverlay.Visibility = Visibility.Visible;
			}
		}
		private void RestoreOriginalCamera()
		{
			if (_hasSavedCamera && viewport != null)
			{
				ProjectionCamera restoredCam;

				if (_savedIsOrtho)
				{
					restoredCam = new OrthographicCamera
					{
						Position = _savedPosition,
						LookDirection = _savedLookDirection,
						UpDirection = _savedUpDirection,
						Width = _savedOrthoWidth
					};
				}
				else
				{
					restoredCam = new PerspectiveCamera
					{
						Position = _savedPosition,
						LookDirection = _savedLookDirection,
						UpDirection = _savedUpDirection,
						FieldOfView = _savedPerspectiveFOV > 0 ? _savedPerspectiveFOV : 45.0
					};
				}

				viewport.Camera = restoredCam;
				_hasSavedCamera = false; // Zurücksetzen für den nächsten Mess-Zyklus
			}
		}

	} // Ende der Klasse MainWindow
} // Ende des Namespaces