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
using System.ComponentModel;

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

		System.Windows.Media.Media3D.Point3D? lastClickPoint = null;
		bool isViewFlat = false;			// wurde der Viewport flach auf X/Y-Ebene gelegt? (Button "Lay flat 3D Viewport" in der Debug-Box)

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

			// global variable for JoystickService, <deHarry, 2026-02-06>
			_joystick = new OpenCNCPilot.Communication.JoystickService(
				Properties.Settings.Default.JoystickPort,
				Properties.Settings.Default.JoystickBaudrate,
				machine, this
			);

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
			if (e.Key == System.Windows.Input.Key.Escape)
			{
				// 1. Letzten Punkt löschen
				lastClickPoint = null;

				// 2. Anzeige in der Debug-Box zurücksetzen
				if (txtViewportDist != null)
				{
					txtViewportDist.Text = "Messung zurückgesetzt";
				}

				// Optional: Falls du ein rotes Kreuz oder eine Markierung hättest, 
				// könnte man sie hier auch ausblenden.
			}
		}

		private void ButtonResetViewport_Click(object sender, RoutedEventArgs e)
		{
			viewport.Camera.Position = new System.Windows.Media.Media3D.Point3D(50, -150, 250);
			viewport.Camera.LookDirection = new System.Windows.Media.Media3D.Vector3D(-50, 150, -250);
			viewport.Camera.UpDirection = new System.Windows.Media.Media3D.Vector3D(0, 0, 1);
		}

		private void viewport_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
		{
			// Sicherheitsabfrage: Nur messen, wenn wir flach draufschauen
			if (!isViewFlat)
			{
				txtViewportCoords.Text = "Nur in 'Lay Flat'-Ansicht möglich";
				txtViewportDist.Text = "";
				return;
			}

			var mousePos = e.GetPosition(viewport);
			var hitPoint = viewport.FindNearestPoint(mousePos);

			if (hitPoint.HasValue)
			{
				double x = hitPoint.Value.X;
				double y = hitPoint.Value.Y;

				// 1. Koordinaten in die Debug-Box schreiben
				txtViewportCoords.Text = string.Format("X: {0:F2} | Y: {1:F2}", x, y);

				// 2. Abstand berechnen, falls ein Punkt davor existiert
				if (lastClickPoint.HasValue)
				{
					double dx = x - lastClickPoint.Value.X;
					double dy = y - lastClickPoint.Value.Y;
					double dist = Math.Sqrt(dx * dx + dy * dy);

					// Anzeige der Gesamtdistanz und der Einzelachsen (Betrag genommen für Abstände)
					txtViewportDist.Text = string.Format("Abstand: {0:F3} mm (dX: {1:F2} | dY: {2:F2})",
														  dist, Math.Abs(dx), Math.Abs(dy));
				}
				else
				{
					txtViewportDist.Text = "Abstand: Startpunkt gesetzt";
				}

				// Punkt für die nächste Messung merken
				lastClickPoint = hitPoint;

				// Die G-Code Suche rufen wir hier nur auf, wenn wir sie 
				// sicher performant programmiert haben. Vorerst auslassen:
				// JumpToNearestGCodeLine(hitPoint.Value);
			}
		}

		private void JumpToNearestGCodeLine(System.Windows.Media.Media3D.Point3D clickPoint)
		{
			int globalIndex = 0;
			int bestGlobalIndex = -1;
			double minDistance = 0.5; // 0.5mm Toleranz für Platinen-Präzision

			foreach (var layer in AllLayers)
			{
				if (!layer.IsActive)
				{
					// Auch wenn der Layer inaktiv ist, müssen wir seine Zeilen mitzählen,
					// damit der Index in der Hauptliste am Ende stimmt!
					globalIndex += layer.Content.Length;
					continue;
				}

				for (int i = 0; i < layer.Content.Length; i++)
				{
					string line = layer.Content[i];

					// Wir suchen im Text der Zeile nach X und Y Werten
					// Das ist viel schneller als die UI-Elemente zu fragen!
					if (TryParseGCodeCoords(line, out double x, out double y))
					{
						double dx = clickPoint.X - x;
						double dy = clickPoint.Y - y;
						double d = Math.Sqrt(dx * dx + dy * dy);

						if (d < minDistance)
						{
							minDistance = d;
							bestGlobalIndex = globalIndex + i;
						}
					}
				}
				globalIndex += layer.Content.Length;
			}

			if (bestGlobalIndex != -1)
			{
				ListViewFile.SelectedIndex = bestGlobalIndex;
				ListViewFile.ScrollIntoView(ListViewFile.SelectedItem);
			}
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

		/*
				private void JumpToNearestGCodeLine(System.Windows.Media.Media3D.Point3D clickPoint)
				{
					int bestIndex = -1;
					double minDistance = 1.0; // 1mm Toleranz

					// Wir gehen durch alle Einträge, die gerade in deiner ListViewFile angezeigt werden
					for (int i = 0; i < ListViewFile.Items.Count; i++)
					{
						// OCP speichert hier oft Objekte vom Typ 'GCodeLine'
						var line = ListViewFile.Items[i];

						// Jetzt kommt der "Hack": Wir versuchen die Koordinaten aus dem Objekt zu lesen
						// Da ich die genaue Klasse nicht kenne, nutzen wir eine vorsichtige Prüfung:
						dynamic cmd = line;
						try
						{
							double dx = clickPoint.X - (double)cmd.X;
							double dy = clickPoint.Y - (double)cmd.Y;
							double d = Math.Sqrt(dx * dx + dy * dy);

							if (d < minDistance)
							{
								minDistance = d;
								bestIndex = i;
							}
						}
						catch { // Falls das Objekt kein X/Y hat, ignorieren wir es
						}
					}

					if (bestIndex != -1)
					{
						ListViewFile.SelectedIndex = bestIndex;
						ListViewFile.ScrollIntoView(ListViewFile.SelectedItem);
					}
				}
		*/

		// ----- ButtonLayFlatViewport ---------------------------------------------------
		private void ButtonLayFlatViewport_Click(object sender, RoutedEventArgs e)
		{
			// 1. Blickrichtung (Dein funktionierender Code)
			viewport.Camera.Position = new System.Windows.Media.Media3D.Point3D(0, 0, viewport.Camera.Position.Z);
			viewport.Camera.LookDirection = new System.Windows.Media.Media3D.Vector3D(0, 1, viewport.Camera.LookDirection.Z);

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
				viewport.ZoomExtents(paddedBounds, 500);
			}
			else
			{
				// Fallback: Wenn wir nichts spezifisches finden, nimm alles
				viewport.ZoomExtents();
			}

			isViewFlat = true;
			txtViewportCoords.Foreground = System.Windows.Media.Brushes.DarkGreen;
		}

		// Wird automatisch aufgerufen, sobald du die Kamera mit der Maus drehst/bewegst
		private void viewport_CameraChanged(object sender, RoutedEventArgs e)
		{
			if (txtViewportCoords == null || viewport.Camera == null) return;

			var look = viewport.Camera.LookDirection;

			// Normalerweise ist die Ansicht flach, wenn X fast 0 ist.
			// Wir erlauben jetzt eine massive Abweichung. 
			// Erst wenn die Kamera wirklich spürbar gekippt wird, schalten wir ab.
			bool stillFlat = Math.Abs(look.X) < 0.2;

			if (stillFlat)
			{
				isViewFlat = true;
				txtViewportCoords.Foreground = System.Windows.Media.Brushes.DarkGreen;
				// WICHTIG: Hier keinen Text überschreiben, sonst löscht jeder Zoom die Zahlen!
			}
			else
			{
				isViewFlat = false;
				txtViewportCoords.Foreground = System.Windows.Media.Brushes.Gray;
				txtViewportCoords.Text = "X: --- | Y: --- (3D-Modus)";
				txtViewportDist.Text = "Abstand: ---";
			}
		}

		// ----- ButtonRotateOrigin ---------------------------------------------------
		private void ButtonRotateOrigin_Click(object sender, RoutedEventArgs e)                     // deHarro, 2024-09-11, Origin im Viewport passend zur Fräse ausrichten
		{
			{   // do the rotating CCW
				if (viewport.Camera.LookDirection.X == 0 && viewport.Camera.LookDirection.Y == 1 /*&& viewport.Camera.LookDirection.Z == 1*/)       // (0, 0, z) default
				{
					viewport.Camera.LookDirection = new System.Windows.Media.Media3D.Vector3D(1, 0, viewport.Camera.LookDirection.Z);
				}
				else if (viewport.Camera.LookDirection.X == 1 && viewport.Camera.LookDirection.Y == 0 /*&& viewport.Camera.LookDirection.Z == 1*/)  // (1, 0, z) my default 
				{
					viewport.Camera.LookDirection = new System.Windows.Media.Media3D.Vector3D(0, -1, viewport.Camera.LookDirection.Z);
				}
				else if (viewport.Camera.LookDirection.X == 0 && viewport.Camera.LookDirection.Y == -1 /*&& viewport.Camera.LookDirection.Z == 1*/) // (0, -1, z) other
				{
					viewport.Camera.LookDirection = new System.Windows.Media.Media3D.Vector3D(-1, 0, viewport.Camera.LookDirection.Z);
				}
				else if (viewport.Camera.LookDirection.X == -1 && viewport.Camera.LookDirection.Y == 0 /*&& viewport.Camera.LookDirection.Z == 1*/) // (-1, 0, z) other       {
				{
					viewport.Camera.LookDirection = new System.Windows.Media.Media3D.Vector3D(0, 1, viewport.Camera.LookDirection.Z);
				}
			}   // \do the rotating CCW

			// temporarily save current viewport look direction and camera position
			System.Windows.Media.Media3D.Vector3D LookDirSave = new System.Windows.Media.Media3D.Vector3D(viewport.Camera.LookDirection.X, viewport.Camera.LookDirection.Y, viewport.Camera.LookDirection.Z);
			System.Windows.Media.Media3D.Vector3D CamPosSave = new System.Windows.Media.Media3D.Vector3D(viewport.Camera.Position.X, viewport.Camera.Position.Y, viewport.Camera.Position.Z);

			// get currently stored viewport settings
			string[] scoords = Properties.Settings.Default.ViewPortPos.Split(';');
			try
			{
				IEnumerable<double> settingsCoords = scoords.Select(s => double.Parse(s));

				viewport.Camera.Position = new Vector3(settingsCoords.Take(3).ToArray()).ToPoint3D();
				viewport.Camera.LookDirection = new Vector3(settingsCoords.Skip(3).Take(3).ToArray()).ToVector3D();  // deHarro, 2024-09-08, nur 3 Werte für Vektor
			}
			catch
			{
				ButtonResetViewport_Click(null, null);
			}
			List<double> coords = new List<double>();
			coords.AddRange(new Vector3(viewport.Camera.Position).Array);                           // with getting from above position is retained
			coords.AddRange(new Vector3(viewport.Camera.LookDirection).Array);                      // with getting from above lookdir is retained
			coords.AddRange(new Vector3(viewport.Camera.UpDirection).Array);                        // store new UpDirection

			Properties.Settings.Default.ViewPortPos = string.Join(";", coords.Select(d => d.ToString()));

			// now restore temporarily saved viewport look direction and camera position
			viewport.Camera.LookDirection = new System.Windows.Media.Media3D.Vector3D(LookDirSave.X, LookDirSave.Y, LookDirSave.Z);
			viewport.Camera.Position = new System.Windows.Media.Media3D.Point3D(CamPosSave.X, CamPosSave.Y, CamPosSave.Z);
		}

		// ----- ButtonRestoreViewport ---------------------------------------------------
		private void ButtonRestoreViewport_Click(object sender, RoutedEventArgs e)
		{
			string[] scoords = Properties.Settings.Default.ViewPortPos.Split(';');

			try
			{
				IEnumerable<double> coords = scoords.Select(s => double.Parse(s));

				viewport.Camera.Position = new Vector3(coords.Take(3).ToArray()).ToPoint3D();
				viewport.Camera.LookDirection = new Vector3(coords.Skip(3).Take(3).ToArray()).ToVector3D();  // deHarro, 2024-09-08, nur 3 Werte für Vektor
			}
			catch
			{
				ButtonResetViewport_Click(null, null);
			}
		}

		// ----- ButtonSaveViewport ---------------------------------------------------
		private void ButtonSaveViewport_Click(object sender, RoutedEventArgs e)
		{
			List<double> coords = new List<double>();

			coords.AddRange(new Vector3(viewport.Camera.Position).Array);
			coords.AddRange(new Vector3(viewport.Camera.LookDirection).Array);

			Properties.Settings.Default.ViewPortPos = string.Join(";", coords.Select(d => d.ToString()));
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
	}
}