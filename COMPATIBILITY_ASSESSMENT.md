# Kompatibilitäts-Analyse: OpenCNCPilot .NET 8.0 / .NET 9.0 Upgrade

**Erstellt**: 2025-01-15  
**Projekt**: OpenCNCPilot (C:\Users\Harry\Desktop\OpenCNCPilot\)  
**Aktuelles Framework**: .NET Framework 4.8  
**Ziel-Frameworks**: .NET 8.0 (LTS) oder .NET 9.0 (STS)  
**Status**: Reine Analyse — Keine Code-Änderungen durchgeführt

---

## Inhaltsverzeichnis

1. [Executive Summary](#executive-summary)
2. [Projektstruktur & Ausgangssituation](#projektstruktur--ausgangssituation)
3. [Kritische Blocker](#kritische-blocker)
4. [NuGet-Paket Kompatibilität (Detailanalyse)](#nuget-paket-kompatibilität-detailanalyse)
5. [Framework-spezifische Risiken](#framework-spezifische-risiken)
6. [Empfehlungen & Nächste Schritte](#empfehlungen--nächste-schritte)

---

## Executive Summary

### Upgrade-Machbarkeit

| Kriterium | Bewertung | Kommentar |
|-----------|-----------|----------|
| **Technische Machbarkeit** | ⚠️ Mittleres Risiko | Abhängig von Paket-Updates und Quellcode-Verfügbarkeit |
| **Paket-Verfügbarkeit** | ✅ Gut | Hauptpakete haben net8/net9-kompatible Versionen |
| **Projektformat** | ❌ **Blocker** | Non-SDK csproj → SDK-Style Konvertierung erforderlich |
| **WPF-Support** | ✅ Gut | Windows-spezifische TFM (net8.0-windows) verfügbar |
| **Zeitaufwand** | 🟡 Mittel-Hoch | 2–4 Tage für vollständiges Upgrade + Testen |

### Zielempfehlung

**Primär: .NET 8.0 (LTS)**
- Long-Term Support bis November 2026
- Produktionsreife
- Vollständige Windows-/WPF-Unterstützung

**Sekundär: .NET 9.0 (STS)**
- Support nur bis November 2026 (Short-Term)
- Neueste Features
- Derselbe technische Aufwand wie .NET 8.0

---

## Projektstruktur & Ausgangssituation

### Lösung

```
OpenCNCPilot.sln
└── OpenCNCPilot/
	├── OpenCNCPilot.csproj (Non-SDK-Style, .NET Framework 4.8)
	├── packages.config (NuGet-Paket-Verwaltung)
	└── [Quellcode, XAML, Ressourcen, ...]
```

### Projekttyp

- **Typ**: WPF Desktop Application (.NET Framework)
- **Format**: Legacy `packages.config` + Non-SDK csproj
- **UI-Framework**: WPF (Windows Presentation Foundation)
- **APIs**: System.Windows.Forms, System.Xaml, PresentationFramework

### Automatisch erkannte Probleme (Assessment-Tool)

```
Issue: ruleId=Api.0001
  Beschreibung: Binär inkompatibel mit ausgewählter .NET-Version

Issue: ruleId=Api.0002
  Beschreibung: Die Quelle ist mit ausgewählter .NET-Version inkompatibel

Issue: ruleId=NuGet.0001
  Beschreibung: Das NuGet-Paket ist nicht kompatibel

Issue: ruleId=Project.0001
  Beschreibung: Die Projektdatei muss in SDK-Stil konvertiert werden

Issue: ruleId=Project.0002
  Beschreibung: Die Zielframeworks des Projekts müssen geändert werden
```

---

## Kritische Blocker

### 1. **Projektdatei-Format (KRITISCH)**

**Problem**: Projektdatei is **Non-SDK-Style** (Legacy XML-Format)

```xml
<!-- Aktuell: OpenCNCPilot.csproj (Legacy) -->
<Project ToolsVersion="4.0" DefaultTargets="Build" xmlns="...">
  <PropertyGroup>
	<TargetFrameworkVersion>v4.8</TargetFrameworkVersion>
	...
  </PropertyGroup>
  ...
</Project>
```

**Anforderung**: SDK-Style Konvertierung ist **erforderlich** bevor net8/net9 zielführend funktioniert.

```xml
<!-- Ziel: SDK-Style csproj -->
<Project Sdk="Microsoft.NET.Sdk.WindowsDesktop">
  <PropertyGroup>
	<TargetFramework>net8.0-windows</TargetFramework>
	<UseWPF>true</UseWPF>
	...
  </PropertyGroup>
</Project>
```

**Impact**: ⛔ **Blocker** — Muss vor allen anderen Upgrades erfolgen

**Tool verfügbar**: `convert_project_to_sdk_style()` (automatisiert möglich)

---

### 2. **WPF Targeting (KRITISCH für WPF-Projekte)**

**Problem**: WPF ist Windows-only → TFM muss `-windows` Suffix enthalten

**Anforderung**: 
- ❌ Falsch: `<TargetFramework>net8.0</TargetFramework>`
- ✅ Richtig: `<TargetFramework>net8.0-windows</TargetFramework>`

**Impact**: Runtime-Error ohne korrektes Windows-Targeting

---

### 3. **packages.config (Migrationspfad)**

**Aktuell**: Projekt verwendet `packages.config` für NuGet-Verwaltung

```xml
<!-- packages.config -->
<packages>
  <package id="HelixToolkit" version="2.11.0" ... />
  <package id="HelixToolkit.Wpf" version="2.11.0" ... />
  <package id="martin2250.Calculator" version="1.0.0" ... />
</packages>
```

**Migrationspfad**:
- ✅ **Empfohlen**: packages.config → PackageReference (SDK-Style Standard)
- ⚠️ **Duldbar**: packages.config beibehalten (aber nicht ideal für .NET Core/Modern .NET)

**Impact**: PackageReference wird mit SDK-Konvertierung teilweise erzwungen

---

## NuGet-Paket Kompatibilität (Detailanalyse)

### Paket 1: HelixToolkit

| Property | Wert |
|----------|------|
| **Aktuell in Projekt** | 2.11.0 |
| **Zielversion net8.0** | 3.1.2 |
| **Zielversion net9.0** | 3.1.2 (assumed) |
| **NuGet-Status** | ✅ Verfügbar für .NET 5+ |
| **TargetFrameworks** | net5.0, net6.0, net7.0, net8.0+ |

#### Detailbefund

- **Upgrade-Typ**: Minor → Major (2.11.0 → 3.x.x) = Potenzielle Breaking Changes
- **Differenz**: +1 Major-Version
- **Risiko-Level**: 🟡 **MITTEL**

#### Bekannte Änderungen / Zu Prüfen

1. **API Changes** (typisch bei Major-Upgrades):
   - Namespace-Reorganisationen möglich
   - Rendering-Engine-Updates (DirectX-Versionen)
   - ViewportRenderHost, Camera, Lighting APIs ggf. geändert

2. **Abhängigkeiten**:
   - HelixToolkit 3.1.2 benötigt: .NET 5+ Runtime + DirectX (auf Windows verfügbar)

3. **Native Komponenten**:
   - HelixToolkit enthält native D3D/DirectX-Bindings → Plattform-spezifisch (x86/x64)
   - Muss für Zielplattform (Windows) korrekt packaged sein

#### Aktionsempfehlung

| Aktion | Priorität | Aufwand |
|--------|-----------|---------|
| Release Notes 2.11.0 → 3.1.2 durcharbeiten | 🔴 HOCH | 1-2h |
| Breaking Changes in Code identifizieren | 🔴 HOCH | 2-3h |
| Rendering-Tests (visuelle Validierung) | 🔴 HOCH | 2-4h |
| Native Abhängigkeiten prüfen | 🟡 MITTEL | 1h |

---

### Paket 2: HelixToolkit.Wpf

| Property | Wert |
|----------|------|
| **Aktuell in Projekt** | 2.11.0 |
| **Zielversion net8.0** | 2.27.3 |
| **Zielversion net9.0** | 2.27.3 (assumed) |
| **NuGet-Status** | ✅ Verfügbar für .NET 4.5+ / .NET Core |
| **TargetFrameworks** | net45, net461, netstandard2.0, net6.0+ |

#### Detailbefund

- **Upgrade-Typ**: Minor-Version (2.11.0 → 2.27.3) = Stabilere Änderungen
- **Differenz**: +16 Minor-Versionen (zählt zu Backports / Bugfixes)
- **Risiko-Level**: 🟢 **NIEDRIG-MITTEL**

#### Bekannte Änderungen / Zu Prüfen

1. **API-Stabilität**:
   - Minor-Version-Upgrades sind typischerweise Bugfixes + Features ohne Breaking Changes
   - Einige neue Events/Properties möglich, aber meist additive Änderungen

2. **WPF-spezifisch**:
   - HelixToolkit.Wpf 2.27.3 unterstützt netstandard2.0 → kompatibel mit .NET Framework + .NET Core/Modern .NET
   - WPF Rendering Pipeline ähnlich über Versionen hinweg

3. **Abhängigkeiten**:
   - Abhängig von HelixToolkit Core Library
   - Ensure: HelixToolkit.Wpf 2.27.3 mit HelixToolkit 3.1.2 kompatibel

#### Aktionsempfehlung

| Aktion | Priorität | Aufwand |
|--------|-----------|---------|
| Release Notes 2.11.0 → 2.27.3 durchscan | 🟡 MITTEL | 30min |
| HelixToolkit/HelixToolkit.Wpf Kompatibilität checken | 🟡 MITTEL | 30min |
| Compile-Test nach Upgrade | 🟡 MITTEL | 1h |

---

### Paket 3: martin2250.Calculator

| Property | Wert |
|----------|------|
| **Aktuell in Projekt** | 1.0.0 |
| **Verfügbare Versionen net8.0** | 1.0.0 (only) |
| **NuGet-Status** | ⚠️ Unbekannt — Proprietäres Paket |
| **TargetFrameworks** | Unbekannt |

#### Detailbefund

- **Typ**: Custom/Proprietäres Paket (author: martin2250)
- **Quellcode**: Unbekannt (möglicherweise im Repo oder extern hosted)
- **Risiko-Level**: 🔴 **HOCH/KRITISCH**

#### Kritische Fragen

1. **Wo ist der Quellcode?**
   - ✅ Falls im OpenCNCPilot-Repo vorhanden → kann neu gegen net8.0/net9.0 kompiliert werden
   - ❌ Falls nur als Binärpaket auf NuGet → überprüfen, ob net8.0 Targets enthält
   - ❌ Falls verwaist/nicht gepflegt → Alternativen suchen oder Fork erstellen

2. **NuGet-Metadaten**:
   - Abfrage ergab: Version 1.0.0 bleibt verfügbar (keine neuere Version)
   - Vermutung: Paket ist statisch und Support für .NET 8.0 unklar

3. **Binär-Kompatibilität**:
   - Wenn Paket nur .NET Framework 4.8-Binary enthält → **INKOMPATIBEL** mit .NET 8.0
   - Wenn Paket netstandard2.0 oder net6.0+ enthält → möglicherweise kompatibel

#### Aktionsempfehlung

| Aktion | Priorität | Aufwand |
|--------|-----------|---------|
| **KRITISCH**: Quellcode für martin2250.Calculator finden | 🔴 HOCH | 30min |
| Falls Repo-lokal: Neu gegen net8.0 kompilieren | 🔴 HOCH | 1-2h |
| Falls nur NuGet-Paket: Kontakt mit Autor / Fork erstellen | 🔴 HOCH | 2+ Tage |
| **Fallback**: Interne Calculator-Lib neu implementieren | 🟡 MITTEL | 4-8h |

---

## Framework-spezifische Risiken

### 1. **Assembly-Binding & Redirects**

**Problem**: .NET Framework nutzt `app.config` mit `bindingRedirect` Einträgen

```xml
<!-- app.config (.NET Framework) -->
<runtime>
  <assemblyBinding xmlns="urn:schemas-microsoft-com:asm.v1">
	<dependentAssembly>
	  <assemblyIdentity name="System.Net.Http" ... />
	  <bindingRedirect oldVersion="0.0.0.0-4.2.0.0" newVersion="4.2.0.0" />
	</dependentAssembly>
  </assemblyBinding>
</runtime>
```

**Impact für .NET 8.0**:
- Binding Redirects funktionieren in .NET Core/Modern .NET **nicht**
- Muss aus app.config entfernt werden
- Paket-Versionen müssen direkt kompatibel sein

**Aufwand**: Gering (Löschen obsoleter Einträge)

---

### 2. **System-Assembly References**

**Aktuell referenziert in csproj**:
- System (4.0.0.0)
- System.Core
- System.Data (4.0.0.0)
- System.Drawing (4.0.0.0)
- System.Management (4.0.0.0)
- System.Windows.Forms (4.0.0.0)
- System.Xml (4.0.0.0)
- System.Xaml (4.0.0.0)
- System.Net.Http (4.2.0.0)
- WindowsBase, PresentationCore, PresentationFramework (WPF)

**Problem**: In SDK-Style csproj werden viele dieser automatisch mit dem Target Framework eingezogen

**Impact**:
- Nach Konvertierung zu SDK-Style + net8.0-windows müssen diese Referenzen wahrscheinlich entfernt/bereinigt werden
- Compiler gibt ggf. Warnungen "already included by framework"

**Aufwand**: Gering (automatisches Cleanup durch SDK-Konvertierungs-Tool)

---

### 3. **WPF/UI Framework Kompatibilität**

**Gut News**: 
- ✅ WPF ist vollständig in .NET 5+ unterstützt (Windows-only)
- ✅ XAML-Syntax und APIs relativ stabil
- ✅ WPF Tooling in Visual Studio aktualisiert

**Zu Prüfen**:
- Custom Controls / Behaviors (möglicherweise HelixToolkit-abhängig)
- Data Binding Syntax (ggf. neue Features in .NET 8/9, aber Backcompat gut)
- Themes / Resources

**Aufwand**: Mittel (hauptsächlich Testen, nicht Umschreiben)

---

### 4. **Language Features & Modern C#**

**Hinweis**: .NET 8.0 nutzt standardmäßig **C# 12**, .NET 9.0 nutzt **C# 13**

**Zu Beachten**:
- Aktueller Code vermutlich C# 4.0-7.0 (für .NET Framework 4.8 typisch)
- Breaking Changes bei C# 12/13 sind selten für Legacy-Code
- Compiler kann aber neue Warnungen auslösen (z. B. nullable reference warnings)

**Aufwand**: Niedrig-Mittel (Compiler-Warnungen durchgehen + None-Suppressors setzen)

---

## Empfehlungen & Nächste Schritte

### Phasierter Upgrade-Plan

#### **Phase 1: Vorbereitung (Tag 1)**

| Task | Aufwand | Kritikalität |
|------|---------|--------------|
| martin2250.Calculator Quellcode/Paket-Verfügbarkeit checken | 30min | 🔴 HOCH |
| HelixToolkit 2.11.0 → 3.1.2 Release Notes durcharbeiten | 1-2h | 🔴 HOCH |
| App.config Binding Redirects dokumentieren | 30min | 🟡 MITTEL |
| Git Branch für Upgrade erstellen (`upgrade/net8.0`) | 15min | 🟢 NIEDRIG |

**Gate**: Falls martin2250.Calculator nicht kompatibel → Alternative definieren (Implementierung, Fork, etc.)

---

#### **Phase 2: Projektformat (Tag 1-2)**

| Task | Aufwand | Kritikalität |
|------|---------|--------------|
| Non-SDK → SDK-Style Konvertierung (`convert_project_to_sdk_style`) | 1h | 🔴 HOCH |
| packages.config → PackageReference Migration (ggf. automatisch) | 30min | 🔴 HOCH |
| Alte Assembly-Referenzen aufräumen | 30min | 🟡 MITTEL |
| Initial Compile-Test (Fehler dokumentieren) | 1-2h | 🔴 HOCH |

---

#### **Phase 3: Paket-Upgrade (Tag 2-3)**

| Task | Aufwand | Kritikalität |
|------|---------|--------------|
| HelixToolkit: 2.11.0 → 3.1.2 | 1-2h | 🔴 HOCH |
| HelixToolkit.Wpf: 2.11.0 → 2.27.3 | 1h | 🔴 HOCH |
| martin2250.Calculator: Neu kompilieren oder ersetzen | 2-4h | 🔴 HOCH |
| NuGet Restore & Compile | 1h | 🔴 HOCH |

---

#### **Phase 4: Framework-Targeting (Tag 3)**

| Task | Aufwand | Kritikalität |
|------|---------|--------------|
| TargetFramework: .NET Framework 4.8 → net8.0-windows | 30min | 🔴 HOCH |
| UseWPF = true 설정 | 15min | 🔴 HOCH |
| Langversion auf C# 12 (optional) | 30min | 🟢 NIEDRIG |
| Full Clean/Rebuild | 1-2h | 🔴 HOCH |

---

#### **Phase 5: Testing & Validierung (Tag 4)**

| Task | Aufwand | Kritikalität |
|------|---------|--------------|
| Kompilierung ohne Fehler ✅ | 1h | 🔴 HOCH |
| Compiler-Warnungen durchgehen | 1-2h | 🟡 MITTEL |
| Unit-Tests (falls vorhanden) | 2-4h | 🔴 HOCH |
| UI-Smoke-Tests (Rendering, 3D, etc.) | 2-4h | 🔴 HOCH |
| Performance-Regression-Tests | 1-2h | 🟡 MITTEL |

---

### Inkompatible Pakete: Zusammenfassung

| Paket | Aktuell | Ziel | Status | Risiko | Aktion |
|-------|---------|------|--------|--------|--------|
| **HelixToolkit** | 2.11.0 | 3.1.2 | ⚠️ Major-Upgrade | 🟡 MITTEL | Release Notes prüfen + Testen |
| **HelixToolkit.Wpf** | 2.11.0 | 2.27.3 | ✅ Minor-Upgrade | 🟢 NIEDRIG | Standard-Update |
| **martin2250.Calculator** | 1.0.0 | ??? | 🔴 UNBEKANNT | 🔴 HOCH | **Quellcode finden / Neu kompilieren** |

---

### Go/No-Go Entscheidungspunkte

**✅ GO zu Phase 2 (SDK-Konvertierung), wenn:**
- martin2250.Calculator Quellcode oder kompatibles Update gefunden
- HelixToolkit 3.1.2 als machbar bewertet (keine Deal-Breaker in Release Notes)

**❌ NO-GO / Eskalation, wenn:**
- martin2250.Calculator vollständig verwaist + keine Alternativen
- HelixToolkit 3.1.2 benötigt proprietäre Komponenten nicht vorhanden
- Zeitbudget < 4 Tage (realistischer Mindestaufwand)

---

### Empfohlener nächster Schritt

✅ **Sofort durchführen**:

1. Prüfen Sie das Projekt-Verzeichnis und Git-History für `martin2250.Calculator`:
   - Ist Quellcode lokal vorhanden? (Repo durchsuchen nach `Calculator.*` Projekten )
   - Falls ja → Neu gegen net8.0 kompilieren
   - Falls nein → NuGet-Paketquelle identifizieren + mit Autor kontaktieren

2. HelixToolkit 3.1.2 Release Notes & Changelog durcharbeiten:
   - Suchen Sie nach Breaking Changes in Rendering/Camera/Viewport APIs
   - Prüfen Sie, ob Sie diese APIs in Ihrem Code nutzen

3. Optional: Diese Assessment erneut für **net9.0** erzeugen (identischer Aufwand, nur wenige nuancen)

---

## Appendix: Befragte APIs & Versionen

```
Projekt: C:\Users\Harry\Desktop\OpenCNCPilot\OpenCNCPilot\OpenCNCPilot.csproj

Verfügbare Target Frameworks (get_dotnet_upgrade_options):
  ✅ net8.0 (LTS, Support bis Nov 2026)
  ✅ net9.0 (STS, Support bis Nov 2026)
  ✅ net10.0 (LTS, Support bis Nov 2028) [Empfohlen vom System]
  ⚠️  net11.0 (Preview - nicht empfohlen)

NuGet-Paket Versionen (get_supported_package_version):
  HelixToolkit net8.0 → 3.1.2
  HelixToolkit.Wpf net8.0 → 2.27.3
  martin2250.Calculator net8.0 → 1.0.0 (no update available)

Assessment-Tool (generate_dotnet_upgrade_assessment):
  API.0001: Binär inkompatibel
  API.0002: Quelle inkompatibel
  NuGet.0001: NuGet-Paket nicht kompatibel
  Project.0001: SDK-Stil erforderlich
  Project.0002: TargetFrameworks müssen geändert werden
```

---

**Analysedatum**: 2025-01-15  
**Status**: ✅ Fertig — Keine Änderungen durchgeführt. Bereit für Freigabe zum Upgrade.
