# Drittlizenzen / Third-Party Notices

Dieses Repository enthält ausschließlich eigenen Quellcode. Alle Fremdkomponenten
werden zur Build- bzw. Laufzeit bezogen und sind hier mit ihren Lizenzen aufgeführt.

## NuGet-Pakete

Diese Pakete werden bei der Wiederherstellung geladen und sind **nicht** Teil dieses
Repositories (siehe `CamCast.csproj`).

| Komponente | Version | Lizenz | Herkunft |
|---|---|---|---|
| OpenCvSharp4 | 4.13.0.20261107 | Apache-2.0 | <https://github.com/shimat/opencvsharp> |
| OpenCvSharp4.runtime.win | 4.13.0.20261107 | Apache-2.0 (OpenCV) | <https://opencv.org> |
| NAudio | 2.2.1 | MIT | <https://github.com/naudio/NAudio> |
| Microsoft.Windows.SDK.NET / WinRT.Runtime | (transitiv) | MIT | <https://github.com/microsoft/CsWinRT> |

> **Hinweis zu `OpenCvSharp4.runtime.win`:** Das Paket enthält neben
> `OpenCvSharpExtern.dll` auch `opencv_videoio_ffmpeg4130_64.dll`
> (OpenCV-Videoio-Plugin auf Basis von FFmpeg). OpenCV selbst steht unter
> Apache-2.0; die enthaltenen FFmpeg-Binärdateien unterliegen je nach Build der
> LGPL bzw. GPL. Vor einer **Weitergabe der Binärdateien** ist das zu prüfen
> (Attribution, ggf. Lizenztext-Beigabe). Für die interne Nutzung ist das
> unkritisch.

## NDI®

* „NDI" ist eine **eingetragene Marke von Vizrt NDI AB**; bei der ersten Nennung mit
  ® kennzeichnen, ebenso an allen Stellen, an denen NDI verwendet/ausgewählt wird.
* Diese Anwendung lädt die **NDI-Runtime** (`Processing.NDI.Lib.x64.dll`) zur
  **Laufzeit** aus der Installation des Anwenders. Die DLLs werden **nicht**
  mitgeliefert und **nicht** in den Systempfad installiert.
* Die nativen Strukturen in `Interop/NdiTypes.cs` sind aus den Header-Dateien des
  NDI SDK abgeleitet; diese Header stehen unter der MIT-Lizenz des NDI SDK.
* Für Nutzung und Auslieferung gelten die Lizenzbedingungen von Vizrt NDI AB:
  <https://ndi.video>. Vor einer **kommerziellen Auslieferung** unbedingt prüfen
  und im Zweifel Kontakt aufnehmen. (Die Lizenz wurde 2025 geändert.)

## Windows und .NET

* Vorausgesetzt werden Windows 10/11 (x64) und die .NET Desktop Runtime 8; beides
  wird nicht mitgeliefert.
* Die .NET-Runtime steht unter der MIT-Lizenz
  (<https://github.com/dotnet/runtime>).
