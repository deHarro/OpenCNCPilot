﻿//------------------------------------------------------------------------------
// <auto-generated>
//     Este código fue generado por una herramienta.
//     Versión de runtime:4.0.30319.42000
//
//     Los cambios en este archivo podrían causar un comportamiento incorrecto y se perderán si
//     se vuelve a generar el código.
// </auto-generated>
//------------------------------------------------------------------------------

namespace OpenCNCPilot.Properties {
    
    
    [global::System.Runtime.CompilerServices.CompilerGeneratedAttribute()]
    [global::System.CodeDom.Compiler.GeneratedCodeAttribute("Microsoft.VisualStudio.Editors.SettingsDesigner.SettingsSingleFileGenerator", "16.8.1.0")]
    internal sealed partial class Settings : global::System.Configuration.ApplicationSettingsBase {
        
        private static Settings defaultInstance = ((Settings)(global::System.Configuration.ApplicationSettingsBase.Synchronized(new Settings())));
        
        public static Settings Default {
            get {
                return defaultInstance;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("COM3")]
        public string SerialPortName {
            get {
                return ((string)(this["SerialPortName"]));
            }
            set {
                this["SerialPortName"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("115200")]
        public int SerialPortBaud {
            get {
                return ((int)(this["SerialPortBaud"]));
            }
            set {
                this["SerialPortBaud"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("Serial")]
        public global::OpenCNCPilot.Communication.ConnectionType ConnectionType {
            get {
                return ((global::OpenCNCPilot.Communication.ConnectionType)(this["ConnectionType"]));
            }
            set {
                this["ConnectionType"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("120")]
        public int ControllerBufferSize {
            get {
                return ((int)(this["ControllerBufferSize"]));
            }
            set {
                this["ControllerBufferSize"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("100")]
        public int StatusPollInterval {
            get {
                return ((int)(this["StatusPollInterval"]));
            }
            set {
                this["StatusPollInterval"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("1")]
        public double ViewportArcSplit {
            get {
                return ((double)(this["ViewportArcSplit"]));
            }
            set {
                this["ViewportArcSplit"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("True")]
        public bool EnableCodePreview {
            get {
                return ((bool)(this["EnableCodePreview"]));
            }
            set {
                this["EnableCodePreview"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("5")]
        public double ProbeSafeHeight {
            get {
                return ((double)(this["ProbeSafeHeight"]));
            }
            set {
                this["ProbeSafeHeight"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("1")]
        public double ProbeMinimumHeight {
            get {
                return ((double)(this["ProbeMinimumHeight"]));
            }
            set {
                this["ProbeMinimumHeight"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("5")]
        public double ProbeMaxDepth {
            get {
                return ((double)(this["ProbeMaxDepth"]));
            }
            set {
                this["ProbeMaxDepth"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("False")]
        public bool AbortOnProbeFail {
            get {
                return ((bool)(this["AbortOnProbeFail"]));
            }
            set {
                this["AbortOnProbeFail"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("20")]
        public double ProbeFeed {
            get {
                return ((double)(this["ProbeFeed"]));
            }
            set {
                this["ProbeFeed"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("1")]
        public double ArcToLineSegmentLength {
            get {
                return ((double)(this["ArcToLineSegmentLength"]));
            }
            set {
                this["ArcToLineSegmentLength"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("5")]
        public double SplitSegmentLength {
            get {
                return ((double)(this["SplitSegmentLength"]));
            }
            set {
                this["SplitSegmentLength"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("8080")]
        public int WebServerPort {
            get {
                return ((int)(this["WebServerPort"]));
            }
            set {
                this["WebServerPort"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("1000")]
        public double JogFeed {
            get {
                return ((double)(this["JogFeed"]));
            }
            set {
                this["JogFeed"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("10")]
        public double JogDistance {
            get {
                return ((double)(this["JogDistance"]));
            }
            set {
                this["JogDistance"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("False")]
        public bool LogTraffic {
            get {
                return ((bool)(this["LogTraffic"]));
            }
            set {
                this["LogTraffic"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("Probe and set Zero:G38.2Z-10F20\r\nG92Z0;")]
        public string Macros {
            get {
                return ((string)(this["Macros"]));
            }
            set {
                this["Macros"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("True")]
        public bool GCodeIncludeSpindle {
            get {
                return ((bool)(this["GCodeIncludeSpindle"]));
            }
            set {
                this["GCodeIncludeSpindle"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("True")]
        public bool GCodeIncludeDwell {
            get {
                return ((bool)(this["GCodeIncludeDwell"]));
            }
            set {
                this["GCodeIncludeDwell"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("False")]
        public bool GCodeIncludeMEnd {
            get {
                return ((bool)(this["GCodeIncludeMEnd"]));
            }
            set {
                this["GCodeIncludeMEnd"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("250")]
        public double JogFeedCtrl {
            get {
                return ((double)(this["JogFeedCtrl"]));
            }
            set {
                this["JogFeedCtrl"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("1")]
        public double JogDistanceCtrl {
            get {
                return ((double)(this["JogDistanceCtrl"]));
            }
            set {
                this["JogDistanceCtrl"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("True")]
        public bool PauseFileOnHold {
            get {
                return ((bool)(this["PauseFileOnHold"]));
            }
            set {
                this["PauseFileOnHold"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("0.95")]
        public double ProbeXAxisWeight {
            get {
                return ((double)(this["ProbeXAxisWeight"]));
            }
            set {
                this["ProbeXAxisWeight"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("False")]
        public bool ShowStatusLines {
            get {
                return ((bool)(this["ShowStatusLines"]));
            }
            set {
                this["ShowStatusLines"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("10")]
        public double ConsoleFadeTime {
            get {
                return ((double)(this["ConsoleFadeTime"]));
            }
            set {
                this["ConsoleFadeTime"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("0")]
        public double ToolLengthSetterPos {
            get {
                return ((double)(this["ToolLengthSetterPos"]));
            }
            set {
                this["ToolLengthSetterPos"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("10")]
        public int SettingsSendDelay {
            get {
                return ((int)(this["SettingsSendDelay"]));
            }
            set {
                this["SettingsSendDelay"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("True")]
        public bool SettingsUpdateRequired {
            get {
                return ((bool)(this["SettingsUpdateRequired"]));
            }
            set {
                this["SettingsUpdateRequired"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("False")]
        public bool TLSUseActualPos {
            get {
                return ((bool)(this["TLSUseActualPos"]));
            }
            set {
                this["TLSUseActualPos"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("False")]
        public bool ManualUseExpressions {
            get {
                return ((bool)(this["ManualUseExpressions"]));
            }
            set {
                this["ManualUseExpressions"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("False")]
        public bool FileExpanderOpen {
            get {
                return ((bool)(this["FileExpanderOpen"]));
            }
            set {
                this["FileExpanderOpen"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("")]
        public string EditExpanderOpen {
            get {
                return ((string)(this["EditExpanderOpen"]));
            }
            set {
                this["EditExpanderOpen"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("False")]
        public bool OverrideExpanderOpen {
            get {
                return ((bool)(this["OverrideExpanderOpen"]));
            }
            set {
                this["OverrideExpanderOpen"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("False")]
        public bool ProbingExpanderOpen {
            get {
                return ((bool)(this["ProbingExpanderOpen"]));
            }
            set {
                this["ProbingExpanderOpen"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("False")]
        public bool ManualExpanderOpen {
            get {
                return ((bool)(this["ManualExpanderOpen"]));
            }
            set {
                this["ManualExpanderOpen"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("False")]
        public bool ManualProbingExpanderOpen {
            get {
                return ((bool)(this["ManualProbingExpanderOpen"]));
            }
            set {
                this["ManualProbingExpanderOpen"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("False")]
        public bool MacroExpanderOpen {
            get {
                return ((bool)(this["MacroExpanderOpen"]));
            }
            set {
                this["MacroExpanderOpen"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("False")]
        public bool MachineExpanderOpen {
            get {
                return ((bool)(this["MachineExpanderOpen"]));
            }
            set {
                this["MachineExpanderOpen"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("False")]
        public bool AboutExpanderOpen {
            get {
                return ((bool)(this["AboutExpanderOpen"]));
            }
            set {
                this["AboutExpanderOpen"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("False")]
        public bool DebugExpanderOpen {
            get {
                return ((bool)(this["DebugExpanderOpen"]));
            }
            set {
                this["DebugExpanderOpen"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("900")]
        public int WindowWidth {
            get {
                return ((int)(this["WindowWidth"]));
            }
            set {
                this["WindowWidth"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("500")]
        public int WindowHeight {
            get {
                return ((int)(this["WindowHeight"]));
            }
            set {
                this["WindowHeight"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("False")]
        public bool BackupHeightMap {
            get {
                return ((bool)(this["BackupHeightMap"]));
            }
            set {
                this["BackupHeightMap"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("0.8")]
        public float HeightMapOpacity {
            get {
                return ((float)(this["HeightMapOpacity"]));
            }
            set {
                this["HeightMapOpacity"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("True")]
        public bool EnableEscapeSoftReset {
            get {
                return ((bool)(this["EnableEscapeSoftReset"]));
            }
            set {
                this["EnableEscapeSoftReset"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("False")]
        public bool IgnoreAdditionalAxes {
            get {
                return ((bool)(this["IgnoreAdditionalAxes"]));
            }
            set {
                this["IgnoreAdditionalAxes"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("0.1")]
        public float GridThickness {
            get {
                return ((float)(this["GridThickness"]));
            }
            set {
                this["GridThickness"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("False")]
        public bool SerialPortDTR {
            get {
                return ((bool)(this["SerialPortDTR"]));
            }
            set {
                this["SerialPortDTR"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("50;-150;250;-50;150;-250")]
        public string ViewPortPos {
            get {
                return ((string)(this["ViewPortPos"]));
            }
            set {
                this["ViewPortPos"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("192.168.1.101")]
        public string EthernetIP {
            get {
                return ((string)(this["EthernetIP"]));
            }
            set {
                this["EthernetIP"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("34000")]
        public int EthernetPort {
            get {
                return ((int)(this["EthernetPort"]));
            }
            set {
                this["EthernetPort"] = value;
            }
        }
    }
}
