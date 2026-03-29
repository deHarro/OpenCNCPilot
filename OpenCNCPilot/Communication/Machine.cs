using OpenCNCPilot.GCode;
using OpenCNCPilot.GCode.GCodeCommands;
using OpenCNCPilot.Util;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.IO.Ports;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Text;			// Behebt Fehler CS0246 (StringBuilder) und CS0103 (Encoding)

namespace OpenCNCPilot.Communication
{
	enum ConnectionType
	{
		Serial,
		Ethernet
	}

	class Machine
	{
		public enum OperatingMode
		{
			Manual,
			SendFile,
			Probe,
			Disconnected,
			SendMacro
		}

		public event Action<Vector3, bool> ProbeFinished;
		public event Action<string> NonFatalException;
		public event Action<string> Info;
		public event Action<string> LineReceived;
		public event Action<string> StatusReceived;
		public event Action<string> LineSent;
		public event Action ConnectionStateChanged;
		public event Action PositionUpdateReceived;
		public event Action StatusChanged;
		public event Action DistanceModeChanged;
		public event Action UnitChanged;
		public event Action PlaneChanged;
		public event Action BufferStateChanged;
		public event Action PinStateChanged;
		public event Action OperatingModeChanged;
		public event Action FileChanged;
		public event Action FilePositionChanged;
		public event Action OverrideChanged;

		public Vector3 MachinePosition { get; private set; } = new Vector3();   //No events here, the parser triggers a single event for both
		public Vector3 WorkOffset { get; private set; } = new Vector3();
		public Vector3 WorkPosition { get { return MachinePosition - WorkOffset; } }

		public Vector3 LastProbePosMachine { get; private set; }
		public Vector3 LastProbePosWork { get; private set; }

		public int FeedOverride { get; private set; } = 100;
		public int RapidOverride { get; private set; } = 100;
		public int SpindleOverride { get; private set; } = 100;

		public bool PinStateProbe { get; private set; } = false;
		public bool PinStateLimitX { get; private set; } = false;
		public bool PinStateLimitY { get; private set; } = false;
		public bool PinStateLimitZ { get; private set; } = false;

		public double FeedRateRealtime { get; private set; } = 0;
		public double SpindleSpeedRealtime { get; private set; } = 0;

		public double CurrentTLO { get; private set; } = 0;

		private Calculator _calculator;
		public Calculator Calculator { get { return _calculator; } }

		private ReadOnlyCollection<bool> _pauselines = new ReadOnlyCollection<bool>(new bool[0]);
		public ReadOnlyCollection<bool> PauseLines
		{
			get { return _pauselines; }
			private set { _pauselines = value; }
		}

		private ReadOnlyCollection<string> _file = new ReadOnlyCollection<string>(new string[0]);
		public ReadOnlyCollection<string> File
		{
			get { return _file; }
			private set
			{
				_file = value;
				FilePosition = 0;

				RaiseEvent(FileChanged);
			}
		}

		private int _filePosition = 0;
		public int FilePosition
		{
			get { return _filePosition; }
			private set
			{
				_filePosition = value;
			}
		}

		private OperatingMode _mode = OperatingMode.Disconnected;
		public OperatingMode Mode
		{
			get { return _mode; }
			private set
			{
				if (_mode == value)
					return;

				_mode = value;
				RaiseEvent(OperatingModeChanged);
			}
		}

		#region Status
		private string _status = "Disconnected";
		public string Status
		{
			get { return _status; }
			private set
			{
				if (_status == value)
					return;
				_status = value;

				RaiseEvent(StatusChanged);
			}
		}

		private ParseDistanceMode _distanceMode = ParseDistanceMode.Absolute;
		public ParseDistanceMode DistanceMode
		{
			get { return _distanceMode; }
			private set
			{
				if (_distanceMode == value)
					return;
				_distanceMode = value;

				RaiseEvent(DistanceModeChanged);
			}
		}

		private ParseUnit _unit = ParseUnit.Metric;
		public ParseUnit Unit
		{
			get { return _unit; }
			private set
			{
				if (_unit == value)
					return;
				_unit = value;

				RaiseEvent(UnitChanged);
			}
		}

		private ArcPlane _plane = ArcPlane.XY;
		public ArcPlane Plane
		{
			get { return _plane; }
			private set
			{
				if (_plane == value)
					return;
				_plane = value;

				RaiseEvent(PlaneChanged);
			}
		}

		private bool _connected = false;
		public bool Connected
		{
			get { return _connected; }
			private set
			{
				if (value == _connected)
					return;

				_connected = value;

				if (!Connected)
					Mode = OperatingMode.Disconnected;

				RaiseEvent(ConnectionStateChanged);
			}
		}

		private int _bufferState;
		public int BufferState
		{
			get { return _bufferState; }
			private set
			{
				if (_bufferState == value)
					return;

				_bufferState = value;

				RaiseEvent(BufferStateChanged);
			}
		}
		#endregion Status

		public bool SyncBuffer { get; set; }

		private Stream Connection;
		private SerialPort serialPort;
		private Thread WorkerThread;

		//ethernet client
		TcpClient ClientEthernet;

		private StreamWriter Log;

		private void RecordLog(string message)
		{
			if (Log != null)
			{
				try
				{
					Log.WriteLine(message);
				}
				catch { throw; }
			}
		}

		public Machine()
		{
			_calculator = new Calculator(this);
		}

		Queue Sent = Queue.Synchronized(new Queue());
		Queue ToSend = Queue.Synchronized(new Queue());
		Queue ToSendPriority = Queue.Synchronized(new Queue()); //contains characters (for soft reset, feed hold etc)
		Queue ToSendMacro = Queue.Synchronized(new Queue());

		private StringBuilder lineBuffer = new StringBuilder();
		private bool SendMacroStatusReceived = false;

		private void Work()
		{
			if (Connection == null) return;

			// Initialisierung der Variablen
			int ControllerBufferSize = Properties.Settings.Default.ControllerBufferSize;
			int StatusPollInterval = Properties.Settings.Default.StatusPollInterval;
			BufferState = 0;

			DateTime LastStatusPoll = DateTime.Now;
			DateTime StartTime = DateTime.Now;
			DateTime LastFilePosUpdate = DateTime.Now;
			bool filePosChanged = false;

			StreamWriter writer = null;
			byte[] readBuffer = new byte[4096];

			try
			{
				writer = new StreamWriter(Connection);

				// Initial-Befehle senden
				writer.Write("\n$G\n$#\n");
				writer.Flush();

				while (Connected && Connection != null)
				{
					// --- 1. PRIORITY SENDEN (Echtzeit-Fix für Overrides) ---
					while (ToSendPriority.Count > 0)
					{
						try
						{
							// Da die Queue 'Synchronized' ist und 'objects' enthält:
							object obj = ToSendPriority.Dequeue();

							if (obj != null)
							{
								// Sicherer Weg: Erst zu char, dann zu byte
								char c = (char)obj;
								byte b = (byte)c;

								if (serialPort != null && serialPort.IsOpen)
								{
									// Wir schreiben das Byte direkt, ohne StreamWriter-Umweg
									serialPort.Write(new byte[] { b }, 0, 1);
								}
							}
						}
						catch (Exception ex)
						{
							System.Diagnostics.Debug.WriteLine("Override-Fehler: " + ex.Message);
						}
					}

					// --- 2. BYTES LESEN (Turbo-Modus ohne Blockierung) ---
					try
					{
						// Wir fragen direkt das SerialPort-Objekt. 
						// Das ist tausendmal schneller als der Umweg über den Stream-Timeout.
						if (serialPort != null && serialPort.IsOpen && serialPort.BytesToRead > 0)
						{
							// Wir lesen nur so viel, wie wirklich im Puffer liegt
							int bytesRead = Connection.Read(readBuffer, 0, readBuffer.Length);

							if (bytesRead > 0)
							{
								string chunk = Encoding.ASCII.GetString(readBuffer, 0, bytesRead);
								lineBuffer.Append(chunk);

								string content = lineBuffer.ToString();
								while (content.Contains("\n") || content.Contains(">"))
								{
									int nlIndex = content.IndexOf('\n');
									int gtIndex = content.IndexOf('>');
									int breakIndex = (nlIndex != -1 && (gtIndex == -1 || nlIndex < gtIndex)) ? nlIndex : gtIndex;

									string readyLine = content.Substring(0, breakIndex + 1).Trim();
									content = content.Substring(breakIndex + 1);

									lineBuffer.Clear();
									lineBuffer.Append(content);

									if (!string.IsNullOrEmpty(readyLine))
										ProcessReceivedLine(readyLine, StartTime);
								}
							}
						}
					}
					catch (Exception ex)
					{
						System.Diagnostics.Debug.WriteLine("CH340 Lese-Glitch: " + ex.Message);
					}

					// --- 3. NORMALES SENDEN (FILE, MACRO, POLL) ---
					HandleSending(writer, ControllerBufferSize, ref filePosChanged);

					// --- 4. STATUS POLL '?' ---
					DateTime Now = DateTime.Now;
					if ((Now - LastStatusPoll).TotalMilliseconds > StatusPollInterval)
					{
						Connection.WriteByte((byte)'?');
						LastStatusPoll = Now;
					}

					// --- 5. GUI UPDATE (FILE POS) ---
					if (filePosChanged && (Now - LastFilePosUpdate).TotalMilliseconds > 500)
					{
						RaiseEvent(FilePositionChanged);
						LastFilePosUpdate = Now;
						filePosChanged = false;
					}

					Thread.Sleep(1); // CPU entlasten
				}
			}
			catch (Exception ex)
			{
				RaiseEvent(ReportError, $"Fataler Fehler in Work-Schleife: {ex.Message}");
				RaiseEvent(() => Disconnect());
			}
		}

		// HILFSMETHODE: Verarbeitet eine fertig zusammengesetzte Zeile (Parser)
		private void ProcessReceivedLine(string line, DateTime StartTime)
		{
			RecordLog("< " + line);

			if (line == "ok")
			{
				RaiseEvent(LineReceived, line);
				if (Sent.Count != 0)
					BufferState -= ((string)Sent.Dequeue()).Length + 1;
				else
					BufferState = 0;
			}
			else if (line.StartsWith("error:"))
			{
				if (Sent.Count != 0)
				{
					string errorline = (string)Sent.Dequeue();
					RaiseEvent(ReportError, $"{line}: {errorline}");
					BufferState -= errorline.Length + 1;
				}
				else
				{
					if ((DateTime.Now - StartTime).TotalMilliseconds > 200)
						RaiseEvent(ReportError, $"Received <{line}> without anything in the Sent Buffer");
					BufferState = 0;
				}
				Mode = OperatingMode.Manual;
			}
			else if (line.StartsWith("<"))
			{
				RaiseEvent(ParseStatus, line);
				SendMacroStatusReceived = true;
			}
			else if (line.StartsWith("[PRB:"))
			{
				RaiseEvent(ParseProbe, line);
				RaiseEvent(LineReceived, line);
			}
			else if (line.StartsWith("["))
			{
				RaiseEvent(UpdateStatus, line);
				RaiseEvent(LineReceived, line);
			}
			else if (line.StartsWith("ALARM"))
			{
				RaiseEvent(ReportError, line);
				Mode = OperatingMode.Manual;
				ToSend.Clear();
				ToSendMacro.Clear();
			}
			else if (line.StartsWith("grbl"))
			{
				RaiseEvent(LineReceived, line);
				RaiseEvent(ParseStartup, line);
			}
			else if (line.Length > 0)
			{
				RaiseEvent(LineReceived, line);
			}
		}

		// HILFSMETHODE: Übernimmt das Senden von G-Code
		private void HandleSending(StreamWriter writer, int ControllerBufferSize, ref bool filePosChanged)
		{
			if (Mode == OperatingMode.SendFile)
			{
				if (File.Count > FilePosition && (File[FilePosition].Length + 1) < (ControllerBufferSize - BufferState))
				{
					string send_line = File[FilePosition].Replace(" ", "");
					writer.Write(send_line + "\n");
					writer.Flush();

					RecordLog("> " + send_line);
					RaiseEvent(UpdateStatus, send_line);
					RaiseEvent(LineSent, send_line);
					BufferState += send_line.Length + 1;
					Sent.Enqueue(send_line);

					if (PauseLines[FilePosition] && Properties.Settings.Default.PauseFileOnHold)
						Mode = OperatingMode.Manual;

					if (++FilePosition >= File.Count)
						Mode = OperatingMode.Manual;

					filePosChanged = true;
				}
			}
			else if (Mode == OperatingMode.SendMacro)
			{
				if (Status == "Idle" && BufferState == 0 && SendMacroStatusReceived)
				{
					SendMacroStatusReceived = false;
					string send_line = (string)ToSendMacro.Dequeue();
					send_line = Calculator.Evaluate(send_line, out bool success);

					if (success)
					{
						send_line = send_line.Replace(" ", "");
						writer.Write(send_line + "\n");
						writer.Flush();
						RecordLog("> " + send_line);
						RaiseEvent(UpdateStatus, send_line);
						RaiseEvent(LineSent, send_line);
						BufferState += send_line.Length + 1;
						Sent.Enqueue(send_line);
					}
					if (ToSendMacro.Count == 0) Mode = OperatingMode.Manual;
				}
			}
			else if (ToSend.Count > 0 && (((string)ToSend.Peek()).Length + 1) < (ControllerBufferSize - BufferState))
			{
				string send_line = ((string)ToSend.Dequeue()).Replace(" ", "");
				writer.Write(send_line + "\n");
				writer.Flush();
				RecordLog("> " + send_line);
				RaiseEvent(UpdateStatus, send_line);
				RaiseEvent(LineSent, send_line);
				BufferState += send_line.Length + 1;
				Sent.Enqueue(send_line);
			}
		}
		public void Connect()
		{
			if (Connected)
				throw new Exception("Can't Connect: Already Connected");

			switch (Properties.Settings.Default.ConnectionType)
			{
				case ConnectionType.Serial:
					string portName = Properties.Settings.Default.SerialPortName;
					int baudRate = Properties.Settings.Default.SerialPortBaud;

					if (string.IsNullOrWhiteSpace(portName))
						throw new Exception("Kein COM-Port ausgewählt.");

					serialPort = new SerialPort(portName, baudRate);

					// Grundkonfiguration (CH340-freundlich)
					serialPort.DtrEnable = true; // Direkt auf true, wie beim Joystick
					serialPort.RtsEnable = true;
					serialPort.Handshake = Handshake.None;

					try
					{
						serialPort.Open();

						// Dem Arduino Zeit zum Booten geben (Reset durch DTR)
						System.Threading.Thread.Sleep(100);

						// Puffer einmalig leeren, damit keine alten Reste stören
						serialPort.DiscardInBuffer();
						serialPort.DiscardOutBuffer();

						// Den Soft-Reset schicken wir trotzdem – sicher ist sicher
						serialPort.Write("\x18");

						Connection = serialPort.BaseStream;
						Connected = true;

						Console.WriteLine($"GRBL-Port {portName} erfolgreich geöffnet.");
					}
					catch (Exception ex)
					{
						if (serialPort != null && serialPort.IsOpen) serialPort.Close();
						throw new Exception($"Fehler beim Öffnen von {portName}: {ex.Message}");
					}
					break;

				case ConnectionType.Ethernet:
					try
					{

						RaiseEvent(Info, "Connecting to " + Properties.Settings.Default.EthernetIP + ":" + Properties.Settings.Default.EthernetPort);
						ClientEthernet = new TcpClient(Properties.Settings.Default.EthernetIP, Properties.Settings.Default.EthernetPort);
						Connected = true;
						RaiseEvent(Info, "Successful Connection");
						Connection = ClientEthernet.GetStream();
					}
					catch (ArgumentNullException)
					{
						MessageBox.Show("Invalid address or port");
					}
					catch (SocketException)
					{
						MessageBox.Show("Connection failure");
					}

					break;
				default:
					throw new Exception("Invalid Connection Type");
			}

			if (!Connected)
			{
				return;
			}

			if (Properties.Settings.Default.LogTraffic)
			{
				try
				{
					Log = new StreamWriter(Constants.LogFile);
				}
				catch (Exception e)
				{
					NonFatalException("could not open logfile: " + e.Message);
				}
			}



			ToSend.Clear();
			ToSendPriority.Clear();
			Sent.Clear();
			ToSendMacro.Clear();

			Mode = OperatingMode.Manual;

			if (PositionUpdateReceived != null)
				PositionUpdateReceived.Invoke();

			WorkerThread = new Thread(Work);
			WorkerThread.Priority = ThreadPriority.AboveNormal;
			WorkerThread.Start();
		}

		public void Disconnect()
		{
			if (Log != null)
				Log.Close();
			Log = null;

			Connected = false;

			WorkerThread.Join();
			switch (Properties.Settings.Default.ConnectionType)
			{
				case ConnectionType.Serial:
					try
					{
						Connection.Close();
					}
					catch { }
					Connection.Dispose();
					Connection = null;
					break;
				case ConnectionType.Ethernet:
					if (Connection != null)
					{
						Connection.Close();
						ClientEthernet.Close();
					}
					Connection = null;
					break;
				default:
					throw new Exception("Invalid Connection Type");
			}
			Mode = OperatingMode.Disconnected;

			MachinePosition = new Vector3();
			WorkOffset = new Vector3();
			FeedRateRealtime = 0;
			CurrentTLO = 0;

			if (PositionUpdateReceived != null)
				PositionUpdateReceived.Invoke();

			Status = "Disconnected";
			DistanceMode = ParseDistanceMode.Absolute;
			Unit = ParseUnit.Metric;
			Plane = ArcPlane.XY;
			BufferState = 0;

			FeedOverride = 100;
			RapidOverride = 100;
			SpindleOverride = 100;

			if (OverrideChanged != null)
				OverrideChanged.Invoke();

			PinStateLimitX = false;
			PinStateLimitY = false;
			PinStateLimitZ = false;
			PinStateProbe = false;

			if (PinStateChanged != null)
				PinStateChanged.Invoke();

			ToSend.Clear();
			ToSendPriority.Clear();
			Sent.Clear();
			ToSendMacro.Clear();
		}

		public void SendLine(string line)
		{
			if (!Connected)
			{
				RaiseEvent(Info, "Not Connected");
				return;
			}

			if (Mode != OperatingMode.Manual && Mode != OperatingMode.Probe)
			{
				RaiseEvent(Info, "Not in Manual Mode");
				return;
			}

			ToSend.Enqueue(line);
		}

		public void SoftReset()
		{
			if (!Connected)
			{
				RaiseEvent(Info, "Not Connected");
				return;
			}

			Mode = OperatingMode.Manual;

			ToSend.Clear();
			ToSendPriority.Clear();
			Sent.Clear();
			ToSendMacro.Clear();
			ToSendPriority.Enqueue((char)0x18);

			BufferState = 0;

			FeedOverride = 100;
			RapidOverride = 100;
			SpindleOverride = 100;

			if (OverrideChanged != null)
				OverrideChanged.Invoke();

			SendLine("$G");
			SendLine("$#");
		}

		public void SendMacroLines(params string[] lines)
		{
			if (Mode != OperatingMode.Manual)
			{
				RaiseEvent(Info, "Not in Manual Mode");
				return;
			}

			foreach (string line in lines)
				ToSendMacro.Enqueue(line.Trim());

			Mode = OperatingMode.SendMacro;
		}

		//probably shouldn't expose this, but adding overrides would be much more effort otherwise
		public void SendControl(byte controlchar)
		{
			if (!Connected)
			{
				RaiseEvent(Info, "Not Connected");
				return;
			}

			ToSendPriority.Enqueue((char)controlchar);
		}

		public void FeedHold()
		{
			if (!Connected)
			{
				RaiseEvent(Info, "Not Connected");
				return;
			}

			ToSendPriority.Enqueue('!');
		}

		public void CycleStart()
		{
			if (!Connected)
			{
				RaiseEvent(Info, "Not Connected");
				return;
			}

			ToSendPriority.Enqueue('~');
		}

		public void JogCancel()
		{
			if (!Connected)
			{
				RaiseEvent(Info, "Not Connected");
				return;
			}

			ToSendPriority.Enqueue((char)0x85);
		}

		public void SetFile(IList<string> file)
		{
			if (Mode == OperatingMode.SendFile)
			{
				RaiseEvent(Info, "Can't change file while active");
				return;
			}

			bool[] pauselines = new bool[file.Count];

			for (int line = 0; line < file.Count; line++)
			{
				var matches = GCodeParser.GCodeSplitter.Matches(file[line]);

				foreach (Match m in matches)
				{
					if (m.Groups[1].Value == "M")
					{
						int code = int.MinValue;

						if (int.TryParse(m.Groups[2].Value, out code))
						{
							if (code == 0 || code == 1 || code == 2 || code == 30 || code == 6)
								pauselines[line] = true;
						}
					}
				}
			}

			File = new ReadOnlyCollection<string>(file);
			PauseLines = new ReadOnlyCollection<bool>(pauselines);

			FilePosition = 0;

			RaiseEvent(FilePositionChanged);
		}

		public void ClearFile()
		{
			if (Mode == OperatingMode.SendFile)
			{
				RaiseEvent(Info, "Can't change file while active");
				return;
			}

			File = new ReadOnlyCollection<string>(new string[0]);
			FilePosition = 0;
			RaiseEvent(FilePositionChanged);
		}

		public void FileStart()
		{
			if (!Connected)
			{
				RaiseEvent(Info, "Not Connected");
				return;
			}

			if (Mode != OperatingMode.Manual)
			{
				RaiseEvent(Info, "Not in Manual Mode");
				return;
			}

			Mode = OperatingMode.SendFile;
		}

		public void FilePause()
		{
			if (!Connected)
			{
				RaiseEvent(Info, "Not Connected");
				return;
			}

			if (Mode != OperatingMode.SendFile)
			{
				RaiseEvent(Info, "Not in SendFile Mode");
				return;
			}

			Mode = OperatingMode.Manual;
		}

		public void ProbeStart()
		{
			if (!Connected)
			{
				RaiseEvent(Info, "Not Connected");
				return;
			}

			if (Mode != OperatingMode.Manual)
			{
				RaiseEvent(Info, "Can't start probing while running!");
				return;
			}

			Mode = OperatingMode.Probe;
		}

		public void ProbeStop()
		{
			if (!Connected)
			{
				RaiseEvent(Info, "Not Connected");
				return;
			}

			if (Mode != OperatingMode.Probe)
			{
				RaiseEvent(Info, "Not in Probe mode");
				return;
			}

			Mode = OperatingMode.Manual;
		}

		public void FileGoto(int lineNumber)
		{
			if (Mode == OperatingMode.SendFile)
				return;

			if (lineNumber >= File.Count || lineNumber < 0)
			{
				RaiseEvent(NonFatalException, "Line Number outside of file length");
				return;
			}

			FilePosition = lineNumber;

			RaiseEvent(FilePositionChanged);
		}

		public void ClearQueue()
		{
			if (Mode != OperatingMode.Manual)
			{
				RaiseEvent(Info, "Not in Manual mode");
				return;
			}

			ToSend.Clear();
		}

		private static Regex GCodeSplitter = new Regex(@"([GZ])\s*(\-?\d+\.?\d*)", RegexOptions.Compiled);

		/// <summary>
		/// Updates Status info from each line sent
		/// </summary>
		/// <param name="line"></param>
		private void UpdateStatus(string line)
		{
			if (!Connected)
				return;

			if (line.Contains("$J="))
				return;

			if (line.StartsWith("[TLO:"))
			{
				try
				{
					CurrentTLO = double.Parse(line.Substring(5, line.Length - 6), Constants.DecimalParseFormat);
					RaiseEvent(PositionUpdateReceived);
				}
				catch { RaiseEvent(NonFatalException, "Error while Parsing Status Message"); }
				return;
			}

			try
			{
				//we use a Regex here so G91.1 etc don't get recognized as G91
				MatchCollection mc = GCodeSplitter.Matches(line);
				for (int i = 0; i < mc.Count; i++)
				{
					Match m = mc[i];

					if (m.Groups[1].Value != "G")
						continue;

					double code = double.Parse(m.Groups[2].Value, Constants.DecimalParseFormat);

					if (code == 17)
						Plane = ArcPlane.XY;
					if (code == 18)
						Plane = ArcPlane.YZ;
					if (code == 19)
						Plane = ArcPlane.ZX;

					if (code == 20)
						Unit = ParseUnit.Imperial;
					if (code == 21)
						Unit = ParseUnit.Metric;

					if (code == 90)
						DistanceMode = ParseDistanceMode.Absolute;
					if (code == 91)
						DistanceMode = ParseDistanceMode.Incremental;

					if (code == 49)
						CurrentTLO = 0;

					if (code == 43.1)
					{
						if (mc.Count > (i + 1))
						{
							if (mc[i + 1].Groups[1].Value == "Z")
							{
								CurrentTLO = double.Parse(mc[i + 1].Groups[2].Value, Constants.DecimalParseFormat);
								RaiseEvent(PositionUpdateReceived);
							}

							i += 1;
						}
					}
				}
			}
			catch { RaiseEvent(NonFatalException, "Error while Parsing Status Message"); }
		}

		private static Regex StatusEx = new Regex(@"(?<=[<|])(\w+):?([^|>]*)?(?=[|>])", RegexOptions.Compiled);

		/// <summary>
		/// Parses a recevied status report (answer to '?')
		/// </summary>
		private void ParseStatus(string line)
		{
			MatchCollection statusMatch = StatusEx.Matches(line);

			if (statusMatch.Count == 0)
			{
				NonFatalException.Invoke(string.Format("Received Bad Status: '{0}'", line));
				return;
			}

			bool posUpdate = false;
			bool overrideUpdate = false;
			bool pinStateUpdate = false;
			bool resetPins = true;

			foreach (Match m in statusMatch)
			{
				if (m.Index == 1)
				{
					Status = m.Groups[1].Value;
					continue;
				}

				if (m.Groups[1].Value == "Ov")
				{
					try
					{
						string[] parts = m.Groups[2].Value.Split(',');
						FeedOverride = int.Parse(parts[0]);
						RapidOverride = int.Parse(parts[1]);
						SpindleOverride = int.Parse(parts[2]);
						overrideUpdate = true;
					}
					catch { NonFatalException.Invoke(string.Format("Received Bad Status: '{0}'", line)); }
				}

				else if (m.Groups[1].Value == "WCO")
				{
					try
					{
						string OffsetString = m.Groups[2].Value;

						if (Properties.Settings.Default.IgnoreAdditionalAxes)
						{
							string[] parts = OffsetString.Split(',');
							if (parts.Length > 3)
							{
								Array.Resize(ref parts, 3);
								OffsetString = string.Join(",", parts);
							}
						}

						WorkOffset = Vector3.Parse(OffsetString);
						posUpdate = true;
					}
					catch { NonFatalException.Invoke(string.Format("Received Bad Status: '{0}'", line)); }
				}

				else if (SyncBuffer && m.Groups[1].Value == "Bf")
				{
					try
					{
						int availableBytes = int.Parse(m.Groups[2].Value.Split(',')[1]);
						int used = Properties.Settings.Default.ControllerBufferSize - availableBytes;

						if (used < 0)
							used = 0;

						BufferState = used;
						RaiseEvent(Info, $"Buffer State Synced ({availableBytes} bytes free)");
					}
					catch { NonFatalException.Invoke(string.Format("Received Bad Status: '{0}'", line)); }
				}

				else if (m.Groups[1].Value == "Pn")
				{
					resetPins = false;

					string states = m.Groups[2].Value;

					bool stateX = states.Contains("X");
					if (stateX != PinStateLimitX)
						pinStateUpdate = true;
					PinStateLimitX = stateX;

					bool stateY = states.Contains("Y");
					if (stateY != PinStateLimitY)
						pinStateUpdate = true;
					PinStateLimitY = stateY;

					bool stateZ = states.Contains("Z");
					if (stateZ != PinStateLimitZ)
						pinStateUpdate = true;
					PinStateLimitZ = stateZ;

					bool stateP = states.Contains("P");
					if (stateP != PinStateProbe)
						pinStateUpdate = true;
					PinStateProbe = stateP;
				}

				else if (m.Groups[1].Value == "F")
				{
					try
					{
						FeedRateRealtime = double.Parse(m.Groups[2].Value, Constants.DecimalParseFormat);
						posUpdate = true;
					}
					catch { NonFatalException.Invoke(string.Format("Received Bad Status: '{0}'", line)); }
				}

				else if (m.Groups[1].Value == "FS")
				{
					try
					{
						string[] parts = m.Groups[2].Value.Split(',');
						FeedRateRealtime = double.Parse(parts[0], Constants.DecimalParseFormat);
						SpindleSpeedRealtime = double.Parse(parts[1], Constants.DecimalParseFormat);
						posUpdate = true;
					}
					catch { NonFatalException.Invoke(string.Format("Received Bad Status: '{0}'", line)); }
				}
			}

			SyncBuffer = false; //only run this immediately after button press

			//run this later to catch work offset changes before parsing position
			Vector3 NewMachinePosition = MachinePosition;

			foreach (Match m in statusMatch)
			{
				if (m.Groups[1].Value == "MPos" || m.Groups[1].Value == "WPos")
				{
					try
					{
						string PositionString = m.Groups[2].Value;

						if (Properties.Settings.Default.IgnoreAdditionalAxes)
						{
							string[] parts = PositionString.Split(',');
							if (parts.Length > 3)
							{
								Array.Resize(ref parts, 3);
								PositionString = string.Join(",", parts);
							}
						}

						NewMachinePosition = Vector3.Parse(PositionString);

						if (m.Groups[1].Value == "WPos")
							NewMachinePosition += WorkOffset;

						if (NewMachinePosition != MachinePosition)
						{
							posUpdate = true;
							MachinePosition = NewMachinePosition;
						}
					}
					catch { NonFatalException.Invoke(string.Format("Received Bad Status: '{0}'", line)); }
				}

			}

			if (posUpdate && Connected && PositionUpdateReceived != null)
				PositionUpdateReceived.Invoke();

			if (overrideUpdate && Connected && OverrideChanged != null)
				OverrideChanged.Invoke();

			if (resetPins)  //no pin state received in status -> all zero
			{
				pinStateUpdate = PinStateLimitX | PinStateLimitY | PinStateLimitZ | PinStateProbe;  //was any pin set before

				PinStateLimitX = false;
				PinStateLimitY = false;
				PinStateLimitZ = false;
				PinStateProbe = false;
			}

			if (pinStateUpdate && Connected && PinStateChanged != null)
				PinStateChanged.Invoke();

			if (Connected && StatusReceived != null)
				StatusReceived.Invoke(line);
		}

		private static Regex ProbeEx = new Regex(@"\[PRB:(?'Pos'\-?[0-9\.]*(?:,\-?[0-9\.]*)+):(?'Success'0|1)\]", RegexOptions.Compiled);

		/// <summary>
		/// Parses a recevied probe report
		/// </summary>
		private void ParseProbe(string line)
		{
			if (ProbeFinished == null)
				return;

			Match probeMatch = ProbeEx.Match(line);

			Group pos = probeMatch.Groups["Pos"];
			Group success = probeMatch.Groups["Success"];

			if (!probeMatch.Success || !(pos.Success & success.Success))
			{
				NonFatalException.Invoke($"Received Bad Probe: '{line}'");
				return;
			}

			string PositionString = pos.Value;

			if (Properties.Settings.Default.IgnoreAdditionalAxes)
			{
				string[] parts = PositionString.Split(',');
				if (parts.Length > 3)
				{
					Array.Resize(ref parts, 3);
					PositionString = string.Join(",", parts);
				}
			}

			Vector3 ProbePos = Vector3.Parse(PositionString);
			LastProbePosMachine = ProbePos;

			ProbePos -= WorkOffset;
			ProbePos.X += Properties.Settings.Default.ProbeOffsetX;
			ProbePos.Y += Properties.Settings.Default.ProbeOffsetY;
			LastProbePosWork = ProbePos;

			bool ProbeSuccess = success.Value == "1";

			ProbeFinished.Invoke(ProbePos, ProbeSuccess);
		}

		private static Regex StartupRegex = new Regex("grbl v([0-9])\\.([0-9])([a-z])");
		private void ParseStartup(string line)
		{
			Match m = StartupRegex.Match(line);

			int major, minor;
			char rev;

			if (!m.Success ||
				!int.TryParse(m.Groups[1].Value, out major) ||
				!int.TryParse(m.Groups[2].Value, out minor) ||
				!char.TryParse(m.Groups[3].Value, out rev))
			{
				RaiseEvent(Info, "Could not parse startup message.");
				return;
			}

			Version v = new Version(major, minor, (int)rev);
			if (v < Constants.MinimumGrblVersion)
			{
				ReportError("Outdated version of grbl detected!");
				ReportError($"Please upgrade to at least grbl v{Constants.MinimumGrblVersion.Major}.{Constants.MinimumGrblVersion.Minor}{(char)Constants.MinimumGrblVersion.Build}");
			}

		}

		/// <summary>
		/// Reports error. This is there to offload the ExpandError function from the "Real-Time" worker thread to the application thread
		/// also used for alarms
		/// </summary>
		private void ReportError(string error)
		{
			if (NonFatalException != null)
				NonFatalException.Invoke(GrblCodeTranslator.ExpandError(error));
		}

		private void RaiseEvent(Action<string> action, string param)
		{
			if (action == null)
				return;

			Application.Current.Dispatcher.BeginInvoke(action, param);
		}

		private void RaiseEvent(Action action)
		{
			if (action == null)
				return;

			Application.Current.Dispatcher.BeginInvoke(action);
		}
	}
}
