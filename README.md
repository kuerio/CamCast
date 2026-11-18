# CamCast

Sendet eine Webcam (plus optional Mikrofon- oder Systemaudio) unter Windows als
NDI®-Quelle ins Netzwerk. Gedacht für den Dauerbetrieb auf einem Rechner, der als
Kamera-/Stream-Zuspieler dient.

> NDI® ist eine eingetragene Marke von Vizrt NDI AB. Runtime und Tools:
> <https://ndi.video/tools/>

---

## Funktionsumfang

* Webcam-Auswahl, Auflösung (bis 4K) und Bildrate
* Die Auflösungsliste wird **direkt beim Gerät abgefragt** – es werden nur die
  Formate angeboten, die die Kamera wirklich unterstützt (Rückfall: Standardliste)
* Mikrofon **oder** Systemaudio (WASAPI-Loopback) als NDI-Audio
* Frei wählbarer NDI-Quellname und optionale NDI-Gruppe
* Live-Vorschau des tatsächlich gesendeten Bildes (nur zur Kontrolle)
* Statusanzeige: Zustand, Ausgabeauflösung, gemessene Bildrate, Anzahl Empfänger,
  Videobilder, Audiopuffer, Lesefehler, Aussteuerungsanzeige
* System-Tray mit Start/Stopp/Beenden
* Autostart mit Windows, „beim Start sofort streamen", Minimieren in den Infobereich
* Einstellungen werden unter `%APPDATA%\CamCast\settings.json` gespeichert
* Protokolldatei unter `%APPDATA%\CamCast\camcast.log`

## Voraussetzungen

| Komponente | Version | Hinweis |
|---|---|---|
| Windows | 10 / 11, 64 Bit | |
| .NET Desktop Runtime | 8.0 | <https://dotnet.microsoft.com/download/dotnet/8.0> |
| NDI Runtime | 5 oder 6 | <https://ndi.video/tools/> – kostenlos |

Die NDI-Runtime wird zur Laufzeit gesucht (Umgebungsvariablen `NDI_RUNTIME_DIR_V6` /
`NDI_RUNTIME_DIR_V5`, Registry-Eintrag der Runtime, übliche Installationspfade,
zuletzt der eigene Programmordner). Es ist **kein** NDI-SDK zum Bauen nötig.

## Bauen

```powershell
# Empfohlen: nutzt das Skript, das eine bekannte Umgebungsvariablen-Falle umgeht
.\build.ps1

# Ausgabe (nur Framework-abhängig, klein):
.\build.ps1 -Publish      # -> .\publish\
```

Direkt mit der CLI:

```powershell
dotnet build .\CamCast\CamCast.csproj -c Release
```

> **Achtung auf dieser Maschine:** Die Umgebungsvariable `MSBuildSDKsPath` zeigt auf
> `C:\Program Files\dotnet\sdk\8.0.101`, dieses SDK ist aber nicht installiert. Das
> lässt *jeden* `dotnet build` mit „Das SDK Microsoft.NET.Sdk wurde nicht gefunden"
> scheitern. `build.ps1` entfernt die Variable automatisch. Dauerhaft beheben:

```powershell
[Environment]::SetEnvironmentVariable('MSBuildSDKsPath', $null, 'User')
```

## Starten

```powershell
.\publish\CamCast.exe
```

Kommandozeilenoptionen:

| Option | Wirkung |
|---|---|
| `--minimized` | Startet direkt im Infobereich (für den Autostart) |
| `--stream` | Startet die NDI-Ausgabe sofort |
| `--list-sources` | Listet alle NDI-Quellen im Netz auf und beendet sich |
| `--probe [Name]` | Verbindet sich als Empfänger und prüft, ob Video/Audio ankommt |

Beispiel-Diagnose:

```powershell
CamCast.exe --list-sources
CamCast.exe --probe "VP2020 (Webcam)"
```

## Typischer Ablauf

1. Kamera, Auflösung, Bildrate und ggf. Audioquelle wählen.
2. Quellnamen vergeben – er erscheint im Netzwerk als `RECHNERNAME (Quellname)`.
3. **Stream starten**.
4. Auf dem Zielrechner z. B. *NDI Studio Monitor* oder OBS (NDI Source) öffnen und
   die Quelle auswählen.

## Netzwerk

* NDI nutzt mDNS/Discovery über UDP 5353 und den Stream über dynamische TCP-Ports
  (ab 5960). Die Windows-Firewall muss die Anwendung (oder diese Ports) zulassen –
  beim ersten Start kommt normalerweise eine entsprechende Abfrage.
* Richtwert Bandbreite: 1080p30 ca. 60–90 Mbit/s, 1080p60 ca. 100–150 Mbit/s.
  Gigabit-Ethernet ist für Produktivbetrieb empfohlen.
* NDI-Gruppen (`TxtGroup`) eignen sich, um mehrere Streams logisch zu trennen.

## Bekannte Einschränkungen

* **Capture-Backend:** Standard ist *DirectShow*. *Media Foundation* kann auf
  manchen Systemen (VMs, ältere Treiber) sehr langsam öffnen – hier gemessen:
  ca. 3 s (DirectShow) gegenüber ca. 35 s (Media Foundation) bei 1080p30. Beide
  Backends liefern dasselbe Bild; im Zweifel DirectShow lassen.
* **Kamera-Index:** Die Geräteliste stammt aus der Windows-Geräte-API, angesprochen
  wird die Kamera über ihren Index im jeweiligen Capture-Backend (Media Foundation
  oder DirectShow). Index-Reihenfolge und Anzeigename können in seltenen Fällen
  abweichen. Deshalb gibt es die Live-Vorschau: sie zeigt, welche Kamera wirklich
  gelesen wird. Im Zweifel das Backend umschalten.
* Audio wird im Format des Geräts gesendet (meist 48 kHz) – NDI passt das auf der
  Empfängerseite an. Es findet keine Resampling-Vorkonvertierung statt.
* Das Video wird als BGRA gesendet; NDI komprimiert selbst weiter. Für maximale
  Effizienz wäre UYVY möglich, ist hier aber bewusst nicht implementiert.

## Projektstruktur

```
CamCast/
  App.xaml(.cs)                 Anwendungseinstieg, CLI-Modi
  MainWindow.xaml(.cs)          Oberfläche, Tray, Vorschau, Status
  Interop/
    NdiTypes.cs                 Native Structs (aus dem SDK-Header abgeleitet)
    NdiNative.cs                Dynamisches Laden der NDI-Runtime + Sender/Empfänger
    NdiSender.cs                Kapselt eine Sende-Instanz
  Capture/
    VideoDeviceEnumerator.cs    Kameraliste (Windows-Geräte-API)
    WebcamCapture.cs            OpenCV-Aufnahme, Konvertierung nach BGRA, Skalierung
    AudioDevices.cs             Mikrofon- und Wiedergabegeräte (NAudio)
    AudioCaptureService.cs      WASAPI-Aufnahme (Mikrofon/Loopback)
    AudioFormatConverter.cs     Umwandlung nach planar float
  Services/
    StreamEngine.cs             Verbindet Aufnahme, Audio und NDI-Ausgabe
    AppSettings.cs              Einstellungen, Autostart (Registry)
    AppLog.cs                   Logging in UI und Datei
```

Die NDI-Anbindung lädt `Processing.NDI.Lib.x64.dll` zur Laufzeit
(`NativeLibrary.Load` + `GetProcAddress`). Damit ist zum Bauen kein NDI-SDK und keine
Importbibliothek nötig; es muss lediglich die kostenlose NDI-Runtime installiert sein.

## Abhängigkeiten und Lizenzen

| Komponente | Lizenz |
|---|---|
| OpenCvSharp4 | Apache-2.0 |
| OpenCV (in OpenCvSharp4.runtime.win) | Apache-2.0 |
| NAudio | MIT |
| NDI-SDK-Header (Strukturen in `Interop/NdiTypes.cs`) | MIT (Header-Dateien des SDK) |
| NDI Runtime (`Processing.NDI.Lib.x64.dll`) | proprietär, Vizrt NDI AB |

Der Quellcode dieses Projekts wird von der NDI-Runtime **nicht** mitgeliefert; er lädt
sie zur Laufzeit aus der Installation des Anwenders.

### Wichtig für kommerzielle Nutzung

Die NDI-SDK-Lizenz wurde 2025 geändert und ist nicht mehr uneingeschränkt
gebührenfrei. Vor einer **Auslieferung als kommerzielles Produkt** unbedingt die
Lizenzbedingungen im SDK-Wurzelverzeichnis („NDI License Agreement") prüfen und im
Zweifel direkt Kontakt zu Vizrt NDI AB aufnehmen. Für die **interne Nutzung** im
eigenen Haus (der klassische Fall „Rechner im Studio/Meetingraum" wie hier) ist das
unkritisch – es wird lediglich die frei verfügbare NDI-Runtime benutzt.

Werden NDI-Dateien mit ausgeliefert, verlangt die Lizenz unter anderem:

* Link auf <https://ndi.video> an allen Stellen, an denen NDI verwendet/ausgewählt
  wird, sowie auf der Produktseite und in der Dokumentation
* Hinweis „NDI® is a registered trademark of Vizrt NDI AB" (bei erster Nennung)
* „NDI" darf **nicht** ohne Rückfrage im Produktnamen verwendet werden (deshalb
  heißt diese Anwendung „CamCast")
* NDI-DLLs im eigenen Programmordner mitliefern, **nicht** in den Systempfad
  installieren

Mögliche Stolperfalle bei der H.264/H.265/AAC-Nutzung: deren Lizenzierung ist
eigenverantwortlich. CamCast nutzt diese Codecs nicht direkt.

## Markenhinweis

NDI® ist eine eingetragene Marke von Vizrt NDI AB.
