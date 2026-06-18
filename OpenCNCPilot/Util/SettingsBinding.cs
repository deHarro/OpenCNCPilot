using System.Windows.Data;

namespace OpenCNCPilot.Util
{
	public class SettingBindingExtension : Binding
	{
		public SettingBindingExtension()
		{
			Initialize();
		}

		public SettingBindingExtension(string path)
			: base(path)
		{
			Initialize();
		}

        private void Initialize()
        {
            Source = Properties.Settings.Default;
            Mode = BindingMode.TwoWay;

            // 1. Kultur für Komma/Punkt-Wandlung
            ConverterCulture = System.Globalization.CultureInfo.CurrentCulture;

            // 2. Sicherheit gegen Null-Referenzen
            // Falls die Box leer ist oder der Wert nicht konvertierbar, 
            // wird eine "0" als Fallback genommen statt 'null'.
            //FallbackValue = 0.0;
            //TargetNullValue = "0";

            // 3. WICHTIG: Kein globales UpdateSourceTrigger = LostFocus hier!
            // (Wegen des Helix-Toolkit-Fehlers von vorhin)
        }
    }
}