using OpenCNCPilot.Communication;
using OpenCNCPilot.GCode;
using System;
using System.Windows;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Win32;
using System.IO;
using System.Text;
using OpenCNCPilot.Properties;
using System.Windows.Controls;

namespace OpenCNCPilot
{
	partial class MainWindow
	{
		
		private string _currentFileName = "";

		public string CurrentFileName
		{
			get => _currentFileName;
			set
			{
				_currentFileName = value;
				GetBindingExpression(Window.TitleProperty).UpdateTarget();
			}
		}

		private void ButtonAddLayer_Click(object sender, RoutedEventArgs e)
		{
			if (machine.Mode == Machine.OperatingMode.SendFile) return;

			// Wir erstellen den Dialog jedes Mal komplett NEU, 
			// um sicherzugehen, dass keine alten Daten drin hängen!
			OpenFileDialog localDialog = new OpenFileDialog() { Filter = "G-Code files (*.nc;*.gcode)|*.nc;*.gcode|All files (*.*)|*.*" };

			if (localDialog.ShowDialog() == true)
			{
				try
				{
					// 1. Daten in VÖLLIG NEUE lokale Variablen laden
					string pfad = localDialog.FileName;
					string dateiName = System.IO.Path.GetFileName(pfad);
					string[] inhalt = System.IO.File.ReadAllLines(pfad);

					// 2. Ein NEUES GCodeLayer Objekt erzeugen
					GCodeLayer layer = new GCodeLayer();

					// 3. Wichtig: Wir erzwingen eine echte Kopie des Strings
					layer.Name = new string(dateiName.ToCharArray());
					layer.Filename = pfad;
					layer.Content = inhalt;
					layer.IsActive = true;

					// 4. In die Liste werfen
					AllLayers.Add(layer);

					// 5. Anzeige aktualisieren
					UpdateLayerDisplay();
					LayerCheckBox_Click(null, null);

					System.Diagnostics.Debug.WriteLine("Hinzugefügt: " + layer.Name);
				}
				catch (Exception ex)
				{
					MessageBox.Show("Fehler: " + ex.Message);
				}
			}
		}

		private void OpenFileDialogGCode_FileOk(object sender, System.ComponentModel.CancelEventArgs e)
		{
			if (machine.Mode == Machine.OperatingMode.SendFile)
				return;

			openFileDialogGCode.InitialDirectory = System.IO.Path.GetDirectoryName(openFileDialogGCode.FileName);

			try
			{
				string[] newLines = System.IO.File.ReadAllLines(openFileDialogGCode.FileName);

				// 1. Liste leeren für Neustart
				AllLayers.Clear();

				// 2. Fehler CS1503 (Zeile 79) lösen: Paket erstellen
				AllLayers.Add(new GCodeLayer
				{
					Name = System.IO.Path.GetFileName(openFileDialogGCode.FileName),
                    Filename = openFileDialogGCode.FileName,
                    Content = newLines,
					IsActive = true
				});

				// 3. UI updaten
				UpdateLayerDisplay();

				// 4. Fehler CS1503 (Zeile 85) lösen: Wir rufen einfach unsere neue Sammel-Logik auf
				LayerCheckBox_Click(null, null);

				CurrentFileName = System.IO.Path.GetFileName(openFileDialogGCode.FileName);
			}
			catch (Exception ex)
			{
				MessageBox.Show("Fehler beim Öffnen: " + ex.Message);
			}

			HeightMapApplied = false;
		}

		/*		private void OpenFileDialogGCode_FileOk(object sender, System.ComponentModel.CancelEventArgs e)
				{
					if (machine.Mode == Machine.OperatingMode.SendFile)
						return;

					CurrentFileName = "";
					ToolPath = GCodeFile.Empty;

					openFileDialogGCode.InitialDirectory = System.IO.Path.GetDirectoryName(openFileDialogGCode.FileName);

					try
					{
						machine.SetFile(System.IO.File.ReadAllLines(openFileDialogGCode.FileName));
						CurrentFileName = System.IO.Path.GetFileName(openFileDialogGCode.FileName);
					}
					catch (Exception ex)
					{
						MessageBox.Show(ex.Message);
					}

					HeightMapApplied = false;
				}
		*/
		private void SaveFileDialogGCode_FileOk(object sender, System.ComponentModel.CancelEventArgs e)
		{
			if (machine.Mode == Machine.OperatingMode.SendFile)
				return;

			try
			{
				ToolPath.Save(saveFileDialogGCode.FileName);
			}
			catch (Exception ex)
			{
				MessageBox.Show(ex.Message);
			}
		}

		private void ButtonOpen_Click(object sender, RoutedEventArgs e)
		{
			if (machine.Mode == Machine.OperatingMode.SendFile)
				return;

			// prevent exception when the drive letter doesn't exist
			if (!System.IO.Directory.Exists(openFileDialogGCode.InitialDirectory))
			{
				openFileDialogGCode.InitialDirectory = "";
			}
			openFileDialogGCode.ShowDialog();
		}

		private void ButtonSave_Click(object sender, RoutedEventArgs e)
		{
			if (machine.Mode == Machine.OperatingMode.SendFile)
				return;

			saveFileDialogGCode.ShowDialog();
		}

		private void ButtonClear_Click(object sender, RoutedEventArgs e)
		{
			if (machine.Mode == Machine.OperatingMode.SendFile)
				return;

			// 1. Unsere neue Layer-Liste komplett leeren
			AllLayers.Clear();

			// 2. Die Anzeige der Namen aktualisieren (damit sie verschwinden)
			UpdateLayerDisplay();

			// 3. Den G-Code in der Maschine löschen (wie im Original)
			machine.SetFile(new string[0]);

			// 4. Den Dateinamen-Text oben zurücksetzen
			CurrentFileName = "";

			// Falls vorhanden, Zeilenanzeige auf 0
			if (RunFileLength != null) RunFileLength.Text = "0";
		}

		private void ButtonFileStart_Click(object sender, RoutedEventArgs e)
		{
			machine.FileStart();
		}

		private void ButtonFilePause_Click(object sender, RoutedEventArgs e)
		{
			machine.FilePause();
		}

		private void ButtonFileGoto_Click(object sender, RoutedEventArgs e)
		{
			if (machine.Mode == Machine.OperatingMode.SendFile)
				return;

			EnterNumberWindow enw = new EnterNumberWindow(machine.FilePosition + 1);
			enw.Title = "Enter new line number";
			enw.Owner = this;
			enw.User_Ok += Enw_User_Ok_Goto;
			enw.Show();
		}

		private void Enw_User_Ok_Goto(double value)
		{
			if (machine.Mode == Machine.OperatingMode.SendFile)
				return;

			machine.FileGoto((int)value - 1);
		}

		// Diese Methode berechnet den G-Code neu, wenn ein Haken gesetzt/entfernt wird
		private void LayerCheckBox_Click(object sender, RoutedEventArgs e)
		{
			// 1. G-Code über unsere neue Logik zusammenbauen (ohne M02, mit Z10)
			string totalGCode = GetCombinedGCode();

			// 2. In Zeilen zerlegen
			string[] lines = totalGCode.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);

			// 3. Der Maschine geben (Das aktualisiert bei dir ALLES)
			if (lines.Length > 0)
			{
				machine.SetFile(lines); // Das ist der Befehl, der bei dir funktioniert!

				// UI-Längenanzeige
				RunFileLength.Text = lines.Length.ToString();
			}
			else
			{
				machine.SetFile(new string[0]);
				RunFileLength.Text = "0";
			}
		}

		// Diese Methode blendet das Layer-Fenster ein oder aus
		private void UpdateLayerDisplay()
		{
			// 1. Status berechnen (Wer ist oben, wer unten?)
			for (int i = 0; i < AllLayers.Count; i++)
			{
				var layer = AllLayers[i] as GCodeLayer;
				if (layer != null)
				{
					layer.IsNotFirst = (i > 0);						// Erster bekommt false
					layer.IsNotLast = (i < AllLayers.Count - 1);	// Letzter bekommt false
				}
			}

			// 2. UI-Panel Sichtbarkeit
			LayerPanel.Visibility = (AllLayers.Count > 1) ? Visibility.Visible : Visibility.Collapsed;
			ButtonFileAdd.IsEnabled = AllLayers.Count > 0;
			ButtonReloadAll.IsEnabled = AllLayers.Count > 0;

			// 3. Liste neu binden (erzwingt das Neuzeichnen der Buttons)
			ListViewLayers.ItemsSource = null;
			ListViewLayers.ItemsSource = AllLayers;
		}

		private void ButtonReloadAll_Click(object sender, RoutedEventArgs e)
		{
			try
			{
				foreach (var layer in AllLayers)
				{
					if (!string.IsNullOrEmpty(layer.Filename) && System.IO.File.Exists(layer.Filename))
					{
						// Die Datei wird einfach neu eingelesen und der Content überschrieben
						layer.Content = System.IO.File.ReadAllLines(layer.Filename);
					}
				}

				// 1. Die Anzeige aktualisieren (damit eventuelle Zeilenänderungen sichtbar werden)
				UpdateLayerDisplay();

				// 2. Den kombinierten Code neu berechnen und an die Maschine/Viewer senden
				LayerCheckBox_Click(null, null);

				// Optional: Ein kurzer Hinweis, dass es geklappt hat
				// MessageBox.Show("Alle Layer wurden neu geladen!");
			}
			catch (Exception ex)
			{
				MessageBox.Show("Fehler beim Neuladen: " + ex.Message);
			}
		}

		private string GetCombinedGCode()
		{
			StringBuilder sb = new StringBuilder();
			var activeLayers = AllLayers.Where(l => l.IsActive).ToList();

			for (int i = 0; i < activeLayers.Count; i++)
			{
				var layer = activeLayers[i];
				string content = string.Join(Environment.NewLine, layer.Content);

				// Werkzeugwechsel (T1, T2... und M06) - Globaler Filter
				if (!Settings.Default.GCodeIncludeToolChange)
				{
					var lines = content.Split(new[] { Environment.NewLine, "\n" }, StringSplitOptions.None);
					for (int j = 0; j < lines.Length; j++)
					{
						string trimmed = lines[j].Trim();

						// NUR bearbeiten, wenn die Zeile NICHT bereits ein Kommentar ist
						if (!string.IsNullOrEmpty(trimmed) && !trimmed.StartsWith("("))
						{
							string upper = trimmed.ToUpper();
							if (upper.Contains("M06") || System.Text.RegularExpressions.Regex.IsMatch(upper, @"T\d+"))
							{
								// WICHTIG: Semikolons entfernen, da sie OCP innerhalb von Klammern verwirren
								string safeLine = trimmed.Replace(";", "-");

								// Jetzt sicher einklammern
								lines[j] = "(" + safeLine + " - suppressed)";
							}
						}
					}
					content = string.Join(Environment.NewLine, lines);
				}

				// Filterung für Zwischen-Layer (NICHT der letzte)
				if (i < activeLayers.Count - 1)
				{
					// Program Ende (M2/M30) - Martins Checkbox nutzen
					if (!Settings.Default.GCodeIncludeMEnd) // Falls die Checkbox NICHT angehakt ist -> entfernen
					{
						content = content.Replace("M02", "(M02 bypassed)").Replace("m02", "(m02 bypassed)")
										 .Replace("M30", "(M30 bypassed)").Replace("m30", "(m30 bypassed)");
					}

					// Spindel Ein/Aus (M3/M5) - Unsere neue Checkbox
					if (!Settings.Default.GCodeIncludeSpindleOnOff)
					{
						content = content.Replace("M03", "(M03 bypassed)").Replace("M3", "(M3 bypassed)")
										 .Replace("M05", "(M05 bypassed)").Replace("M5", "(M5 bypassed)");
					}

					content += Environment.NewLine + "G0 Z10 (Sicherheits-Hoehe fuer Layerwechsel)" + Environment.NewLine;
				}

				// Zero-Length Moves filtern (Das kann für ALLE Layer gelten)
				if (Settings.Default.GCodeFilterZeroMoves)
				{
					content = FilterZeroLengthMoves(content);
				}

				sb.AppendLine($"(--- Start Layer: {layer.Name} ---)");
				sb.AppendLine(content);
				sb.AppendLine();
			}

			return sb.ToString();
		}

		// Hilfsfunktion um den Code sauber zu halten
		private string FilterZeroLengthMoves(string input)
		{
			string[] lines = input.Split(new[] { Environment.NewLine }, StringSplitOptions.None);
			StringBuilder clean = new StringBuilder();
			string lastPos = "";

			foreach (var line in lines)
			{
				string current = line.Trim();
				// Erkennt Zeilen die exakt gleich sind (oft bei pcb-gcode)
				if (current.StartsWith("G1") || current.StartsWith("G0"))
				{
					if (current == lastPos) continue;
					lastPos = current;
				}
				clean.AppendLine(line);
			}
			return clean.ToString();
		}

		private void ButtonMoveLayerUp_Click(object sender, RoutedEventArgs e)
		{
			var layer = (sender as Button).DataContext as GCodeLayer;
			if (layer != null)
			{
				int index = AllLayers.IndexOf(layer);
				if (index > 0)
				{
					AllLayers.Move(index, index - 1);
					UpdateLayerDisplay(); // Buttons aktualisieren
					LayerCheckBox_Click(null, null); // G-Code neu berechnen
				}
			}
		}

		private void ButtonMoveLayerDown_Click(object sender, RoutedEventArgs e)
		{
			var layer = (sender as Button).DataContext as GCodeLayer;
			if (layer != null)
			{
				int index = AllLayers.IndexOf(layer);
				if (index >= 0)
				{
					AllLayers.Move(index, index + 1);
					UpdateLayerDisplay(); // Buttons aktualisieren
					LayerCheckBox_Click(null, null); // G-Code neu berechnen
				}
			}
		}

		// Hilfsmethode, um den Code nicht doppelt schreiben zu müssen (wie in deinem CheckBox_Click)
		private void UpdateMachineFile()
		{
			string totalGCode = GetCombinedGCode();
			string[] lines = totalGCode.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);

			if (lines.Length > 0)
			{
				machine.SetFile(lines);
				RunFileLength.Text = lines.Length.ToString();
			}
			else
			{
				machine.SetFile(new string[0]);
				RunFileLength.Text = "0";
			}
		}
	}
}
