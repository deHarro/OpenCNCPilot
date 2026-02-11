using OpenCNCPilot.Communication;
using OpenCNCPilot.Util;
using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Management;
using System.Windows;
using System.Windows.Controls;

namespace OpenCNCPilot
{
	/// <summary>
	/// Interaction logic for SettingsWindow.xaml
	/// </summary>
	public partial class SettingsWindow : Window
	{
		public SettingsWindow()
		{

			InitializeComponent();

			ComboBoxSerialPort_DropDownOpened(ComboBoxSerialPort, null);
            ComboBoxSerialPort_DropDownOpened(ComboBoxJoystickPort, null);

            comboBoxConnectionType.ItemsSource = Enum.GetValues(typeof(ConnectionType)).Cast<ConnectionType>();
		}

        private void ComboBoxSerialPort_DropDownOpened(object sender, EventArgs e)
        {
            // Falls die Methode beim Start manuell gerufen wird (sender == null), 
            // nehmen wir standardmäßig die GRBL-Box.
            ComboBox targetBox = (sender as ComboBox) ?? ComboBoxSerialPort;

            targetBox.Items.Clear();

            Dictionary<string, string> ports = new Dictionary<string, string>();

            try
            {
                ManagementObjectSearcher searcher = new ManagementObjectSearcher("root\\CIMV2", "SELECT * FROM Win32_SerialPort");
                foreach (ManagementObject queryObj in searcher.Get())
                {
                    string id = queryObj["DeviceID"] as string;
                    string name = queryObj["Name"] as string;

                    if (id != null && name != null && !ports.ContainsKey(id))
                        ports.Add(id, name);
                }
            }
            catch (ManagementException ex)
            {
                // Nur anzeigen, wenn wirklich ein Fehler passiert
                Console.WriteLine("WMI Error: " + ex.Message);
            }

            // Fallback für Boards, die nicht via WMI gelistet werden
            foreach (string port in SerialPort.GetPortNames())
            {
                if (!ports.ContainsKey(port))
                {
                    ports.Add(port, port);
                }
            }

            foreach (var port in ports)
            {
                // Wir fügen ComboBoxItems hinzu. 
                // Wichtig: Content ist der Name, Tag ist die ID (z.B. "COM3")
                targetBox.Items.Add(new ComboBoxItem() { Content = port.Value, Tag = port.Key });
            }
        }
        private void Window_Closed(object sender, EventArgs e)
		{
			Properties.Settings.Default.Save();
		}

        private void FirmwareType_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
			Util.GrblCodeTranslator.Reload();
        }
    }
}
