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
using HelixToolkit.Wpf;					// Falls du noch Helix-Typen nutzt

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

		bool isViewFlat = false;            // wurde der Viewport flach auf X/Y-Ebene gelegt? (Button "Lay flat 3D Viewport" in der Debug-Box)

		// Wir nutzen Visual3D als Basisklasse, da LinesVisual3D, QuadVisual3D und MeshElement3D alle davon erben.
		private Dictionary<System.Windows.Media.Media3D.Visual3D, List<int>> _lineMapping = new Dictionary<System.Windows.Media.Media3D.Visual3D, List<int>>();

		private Point3D? _firstMeasurePoint = null; // Speichert den ersten Klick für die Messung
		private double _lastX, _lastY;              // Für den "Send to Manual"-Button

		private HelixToolkit.Wpf.SphereVisual3D _clickMarker;
		private SphereVisual3D _measureMarker;

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

		public MainWindow()
		{
			AppDomain.CurrentDomain.UnhandledException += UnhandledException;
			InitializeComponent();

			var cam = viewport?.Camera as System.Windows.Media.Media3D.ProjectionCamera;
			if (cam != null)
			{
				string data = $"Pos: {cam.Position}\nDir: {cam.LookDirection}\nUp: {cam.UpDirection}";

				// 1. In das Debug-Fenster schreiben
				System.Diagnostics.Trace.WriteLine("!!! KAMERA DEBUG !!!\n" + data);

				// 2. Ein Fenster aufpoppen lassen (nur zum Testen!)
				// System.Windows.MessageBox.Show(data, "Kamera beim Start");
			}

			_clickMarker = new HelixToolkit.Wpf.SphereVisual3D { Radius = 0.5, Fill = System.Windows.Media.Brushes.Red };
			viewport.Children.Add(_clickMarker);
			_measureMarker = new HelixToolkit.Wpf.SphereVisual3D { Radius = startRadius, Fill = System.Windows.Media.Brushes.Blue };
			viewport.Children.Add(_measureMarker);
			// WICHTIG: Am Anfang unsichtbar machen, damit er nicht bei (0,0,0) im Modell schwebt
			_measureMarker.Content = null;


			// global variable for JoystickService, <deHarry, 2026-02-06>
			_joystick = new OpenCNCPilot.Communication.JoystickService(
				Properties.Settings.Default.JoystickPort,
				Properties.Settings.Default.JoystickBaudrate,
				machine, this
			);

			Properties.Settings.Default.PropertyChanged += (s, e) => {
				if (e.PropertyName == "MarkerSize")
				{
					double newRadius = GetDynamicMarkerSize() / 2.0;

					if (_clickMarker != null) _clickMarker.Radius = newRadius;
					if (_measureMarker != null) _measureMarker.Radius = newRadius;
				}
			};

			// automatically  open/close joystick port, <deHarry, 2026-02-06>
			machine.ConnectionStateChanged += () =>
			{
				if (machine.Connected)
				{
					_joystick.Open();
				}
				else
				{
					_joystick.Close();
				}
			};
			// automatically  open/close joystick port, <deHarry, 2026-02-06>

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

				// Optional: Wenn beim ESC-Drücken auch die Koordinaten-Anzeige 
				// geleert werden soll, nimm diese Zeile dazu:
				if (TxtPickedCoords != null) TxtPickedCoords.Text = "X: 0.000 Y: 0.000";

				// 3. Modus-Buttons optisch zurücksetzen (falls gewünscht)
				// Wenn ESC auch den Modus komplett beenden soll:
				// BtnMeasure.IsChecked = false;
				// BtnMeasure.ClearValue(Control.BackgroundProperty);

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
			// Punkt 3: Automatisch Lay-Flat ausführen
			// Wir rufen die vorhandene OCP-Funktion auf, die die Kamera flach stellt
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
			// Wir nutzen hier Environment.NewLine, damit es im Clipboard und in der Box sauber getrennt ist
			string gcode = cmdZ + Environment.NewLine + cmdXY;

			if (TextBoxManual != null)
			{
				// In die OCP Manual-Box schreiben
				TextBoxManual.Text = gcode;
				TextBoxManual.Focus();
			}
			else
			{
				// Falls die Box nicht da ist: Ab ins Clipboard (dein else-Zweig)
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


		private void HandleGCodeMapping(RayMeshGeometry3DHitTestResult meshHit)
		{
			var hitVisual = meshHit.VisualHit;
			if (hitVisual == null) return;

			// 1. Das Ziel-Visual im Mapping finden (Baum-Suche für Gruppen/Modelle)
			Visual3D target = null;
			DependencyObject parent = hitVisual;
			while (parent != null)
			{
				if (parent is Visual3D v3d && _lineMapping.ContainsKey(v3d))
				{
					target = v3d;
					break;
				}
				parent = VisualTreeHelper.GetParent(parent);
			}

			if (target != null)
			{
				// 2. Index-Berechnung (Lines = 2 Punkte, Quads = 4 Punkte)
				int divisor = (target is HelixToolkit.Wpf.QuadVisual3D) ? 4 : 2;
				int segmentIndex = meshHit.VertexIndex1 / divisor;

				var indices = _lineMapping[target];

				if (segmentIndex >= 0 && segmentIndex < indices.Count)
				{
					// 3. 1-basierte Zeilennummer aus dem Mapping holen
					int lineNumber = indices[segmentIndex];

					// 4. Daten-Extraktion aus der geladenen Datei
					if (lineNumber > 0 && lineNumber <= machine.File.Count)
					{
						string gcodeLine = machine.File[lineNumber - 1];

						// Koordinaten aus dem Text parsen
						double? x = GetCoord(gcodeLine, 'X');
						double? y = GetCoord(gcodeLine, 'Y');

						// Globale Variablen für "Send to Manual" aktualisieren
						if (x.HasValue) _lastX = x.Value;
						if (y.HasValue) _lastY = y.Value;

						// --- DIE VISUELLE NADEL ---
						if (_clickMarker != null)
						{
							// Wir setzen die Nadel auf die exakten G-Code-Koordinaten
							// Z nehmen wir vom Klick-Punkt, damit sie nicht "im Boden" versinkt
							_clickMarker.Center = new Point3D(_lastX, _lastY, meshHit.PointHit.Z + 0.2);
						}

						System.Diagnostics.Debug.WriteLine($"KLICK: Seg-{segmentIndex} -> Zeile {lineNumber}: {gcodeLine} (Nadel bei X{_lastX} Y{_lastY})");

						// 5. UI-Synchronisation (Scrollen und Markieren)
						SelectLineInUI(lineNumber - 1);
					}
				}
			}
			else
			{
				System.Diagnostics.Debug.WriteLine("Kein Mapping für dieses Objekt gefunden.");
			}
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
					//ListViewFile.ScrollIntoView(ListViewFile.Items[lineNumber]);
					// Wir zwingen das Scrollen in die nächste UI-Verarbeitungsschleife
					Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ContextIdle, new Action(() =>
					{
						ListViewFile.ScrollIntoView(ListViewFile.Items[lineNumber]);

						// Falls OCP die Selektion visuell "verschluckt", erzwingen wir ein Update
						var container = ListViewFile.ItemContainerGenerator.ContainerFromIndex(lineNumber) as FrameworkElement;
						container?.BringIntoView();
					}));

					// 3. Optional: Den Fokus setzen, damit die Zeile farblich hervorgehoben wird
					ListViewFile.Focus();
				}
			}
		}


		private void viewport_MouseDown(object sender, MouseButtonEventArgs e)
		{
			// PINZETTE: Nur weitermachen, wenn einer der Modi wirklich aktiv ist
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

			// 1. Dein bewährter Magnet-Loop
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
						hitFound = true; // <-- Jetzt wissen wir: Wir haben G-Code getroffen
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
				// Manueller Ersatz für FindRay, falls HelixToolkit zickt:
				var viewport3D = viewport.Viewport;
				var hitResult = VisualTreeHelper.HitTest(viewport3D, mousePos) as RayMeshGeometry3DHitTestResult;
				if (hitResult != null)
					pointToUse = hitResult.PointHit;
				else
					pointToUse = new Point3D(0, 0, 0); // Letzter Ausweg
			}

			// 3. Die Mess-Logik mit DEINEM BtnMeasure
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

		// Diese Hilfsfunktion gehört direkt unter die MouseDown-Routine
		private bool IsVisualInMapping(DependencyObject visual)
		{
			DependencyObject current = visual;
			while (current != null)
			{
				if (current is Visual3D v3d && _lineMapping.ContainsKey(v3d))
					return true;

				current = VisualTreeHelper.GetParent(current);
			}
			return false;
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

			// Wir aktualisieren unsere Historie, damit der nächste Klick  auf diesen Werten aufbauen kann.
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
			_clickMarker.Center = new Point3D(finalX, finalY, Math.Max(hitPoint.Z, 0) + 1.0);			

			// 4. UI-Synchronisation
			SelectLineInUI(lineNumber - 1);
		}

		// Hilfsfunktion, um X/Y aus einem G-Code String zu fischen (sehr performant)
		private bool TryParseGCodeCoords(string line, out double x, out double y)
		{
			x = 0; y = 0;
			if (!line.Contains("X") && !line.Contains("Y")) return false;

			try
			{
				var parts = line.Split(' ');
				foreach (var p in parts)
				{
					if (p.StartsWith("X")) double.TryParse(p.Substring(1), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out x);
					if (p.StartsWith("Y")) double.TryParse(p.Substring(1), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out y);
				}
				return true;
			}
			catch { return false; }
		}

		// ----- ButtonLayFlatViewport ---------------------------------------------------
		private void ButtonLayFlatViewport_Click(object sender, RoutedEventArgs e)
		{
			if (viewport == null || viewport.Camera == null) return;

			// 1. Blickrichtung, Orientierung sicher bestimmen
			double targetUpX = viewport.Camera.LookDirection.X;
			double targetUpY = viewport.Camera.LookDirection.Y;

			// Wenn wir schon flach sind, nehmen wir das aktuelle Up
			if (Math.Abs(targetUpX) < 0.01 && Math.Abs(targetUpY) < 0.01)
			{
				targetUpX = viewport.Camera.UpDirection.X;
				targetUpY = viewport.Camera.UpDirection.Y;
			}

			Vector3D newUp = new Vector3D(targetUpX, targetUpY, 0);

			// AUCH HIER: Normalisieren! 
			if (newUp.Length > 0)
				newUp.Normalize();
			else
				newUp = new Vector3D(0, 1, 0);

			// Blickrichtung auf einen Standardwert (Länge egal, Hauptsache Richtung stimmt)
			viewport.Camera.LookDirection = new Vector3D(0, 0, -1);
			viewport.Camera.UpDirection = newUp;

				// Wir erhöhen den Faktor etwas: Referenzwert 50mm statt 100mm
				// Damit werden Marker bei großen Platinen noch deutlicher.
				double factor = Math.Max(1.0, diagonal / 50.0);
				double finalSize = baseSize * Math.Min(factor, 15.0);

				// Debug-Ausgabe (erscheint im "Output" Fenster von Visual Studio)
				System.Diagnostics.Debug.WriteLine($"Marker-Check: Diag={diagonal:F1}, Factor={factor:F1}, Result={finalSize:F1}");

				return finalSize;
			}

			return baseSize;
		}

		//----- ButtonLayFlatViewport ---------------------------------------------------
		private void ButtonLayFlatViewport_Click(object sender, RoutedEventArgs e)
        {
            if (viewport == null || viewport.Camera == null) return;

            // --- NEU: ORIENTIERUNG BEIBEHALTEN ---
            // Wir schauen, in welche Richtung die Kamera horizontal blickt (X und Y).
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

            // 1. Blickrichtung & Oben-Ausrichtung (DYNAMISCH)
            // Blick immer senkrecht runter (Z = -100)
            viewport.Camera.LookDirection = new System.Windows.Media.Media3D.Vector3D(0, 0, -100);
            // Oben ist jetzt das, was vorher "Vorne" war
            viewport.Camera.UpDirection = new System.Windows.Media.Media3D.Vector3D(targetUpX, targetUpY, 0);
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

		// In MainWindow.xaml.cs einfügen
		public void ApplyGlobalViewportStandard()
		{

			if (viewport == null) return;

			// Wir bleiben beim bewährten ContextIdle
			this.Dispatcher.InvokeAsync(() =>
			{
				// 1. Kamera-Konflikt lösen (EXAKT DEIN CODE)
				if (viewport.Camera is System.Windows.Media.Media3D.ProjectionCamera cam)
				{
					// Nur eingreifen, wenn Look und Up fast parallel sind (Z-Z-Bug)
					if (Math.Abs(cam.UpDirection.Z) > 0.9 && Math.Abs(cam.LookDirection.X) < 0.1)
					{
						cam.UpDirection = new System.Windows.Media.Media3D.Vector3D(0, 1, 0);
					}
				}

				// 2. Bounding Box berechnen (EXAKT DEIN CODE)
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

				// 3. Gezielter Zoom (EXAKT DEIN CODE)
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
					// NEU: Falls kein G-Code da ist (Start), zeige den Tisch-Ursprung
					// Damit verhinderst du, dass man beim Start ins Leere schaut.
					viewport.ZoomExtents(new System.Windows.Media.Media3D.Rect3D(-10, -10, 0, 120, 120, 1), 0);
				}
			}, System.Windows.Threading.DispatcherPriority.ContextIdle);
		} // Ende ApplyGlobalViewportStandard

		private void SyncMachineWithLayers()
		{
			// 1. G-Code zusammenbauen
			string totalGCode = GetCombinedGCode();
			if (string.IsNullOrWhiteSpace(totalGCode)) return;

			// 2. Rotation vorbereiten
			int rotationAngle = Properties.Settings.Default.GCodeRotation;
			int steps = (rotationAngle / 90) % 4;

			string[] finalLines;

			//if (steps > 0)
			//{
			//	// Wir nutzen den "Clumsy"-Weg, weil er der EINZIGE ist, den dein GCodeFile-Typ frisst
			//	string tempPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ocp_rotate_temp.nc");

			//	try
			//	{
			//		System.IO.File.WriteAllText(tempPath, totalGCode);

			//		// Das hier akzeptiert dein Compiler (haben wir oben gesehen)
			//		var tempFile = GCodeFile.Load(tempPath);

			//		for (int i = 0; i < steps; i++)
			//		{
			//			tempFile.RotateCW();
			//		}

			//		finalLines = tempFile.GetGCode().ToArray();
			//	}
			//	finally
			//	{
			//		// Sofort löschen, damit kein Müll bleibt
			//		if (System.IO.File.Exists(tempPath)) System.IO.File.Delete(tempPath);
			//	}
			//}
			//else
			//{
			//	finalLines = totalGCode.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
			//}

			if (steps > 0)
			{
				string tempPath1 = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ocp_rotate_in.nc");
				string tempPath2 = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ocp_rotate_out.nc");

				System.IO.File.WriteAllText(tempPath1, totalGCode);

				var tempFile = GCodeFile.Load(tempPath1);

				for (int i = 0; i < steps; i++)
				{
					tempFile.RotateCW();
				}

				// Da 'Commands' nicht existiert, nutzen wir Martins 'Save'-Methode.
				// Diese Methode MUSS die rotierten Koordinaten in Text umwandeln.
				tempFile.Save(tempPath2);

				// Jetzt lesen wir die fertig rotierten Zeilen einfach wieder ein
				finalLines = System.IO.File.ReadAllLines(tempPath2);

				// Aufräumen
				if (System.IO.File.Exists(tempPath1)) System.IO.File.Delete(tempPath1);
				if (System.IO.File.Exists(tempPath2)) System.IO.File.Delete(tempPath2);
			}
			else
			{
				finalLines = totalGCode.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
			}

			// 3. Der Maschine übergeben
			machine.SetFile(finalLines);

			// 4. UI-Längenanzeige
			RunFileLength.Text = finalLines.Length.ToString();

			// A. Den internen GCode-Status der Maschine aktualisieren
			// Das zwingt die Listbox oft dazu, ihren Index neu aufzubauen
			machine.GetType().GetMethod("OnFileUpdated")?.Invoke(machine, null);

			// B. Den Viewport (3D) zum Neuzeichnen zwingen
			// Wir rufen deine vorhandene Zoom-Funktion auf, die meistens auch ein Redraw auslöst
			ApplyGlobalViewportStandard();

			// 5. Memory-Leak-Bremse für PCB (30k+ Lines)
			if (finalLines.Length > 20000)
			{
				GC.Collect(1);
			}
		}

	} // Ende MainWindow
} // Ende Namespace