# MonSwitch

Schaltet die **Eingänge von Monitoren per selbst definierter Tastenkombination** um – z. B. mit
<kbd>Strg</kbd> + <kbd>Alt</kbd> + <kbd>1</kbd> vom PC auf den Laptop an HDMI 1. Die Steuerung erfolgt über
**DDC/CI** (VESA MCCS, VCP-Code `0x60`), also direkt über das Monitorkabel – ohne Zusatzhardware.

MonSwitch läuft unauffällig im Hintergrund, startet automatisch mit Windows und wird über ein Symbol im
Infobereich der Taskleiste (Systray) konfiguriert.

## Funktionen

- **Globale Tastenkombinationen** pro Aktion, frei definierbar (inkl. Windows-Taste und F13–F24)
- **Zwei Modi:** auf einen festen Eingang schalten oder zwischen zwei Eingängen hin- und herschalten
- **Mehrere Monitore gleichzeitig** mit einer Tastenkombination umschalten (z. B. beide Monitore auf den Laptop)
- **Automatische Erkennung** der Monitore, des aktuellen Eingangs und der unterstützten Eingänge (Capabilities)
- **LG-Monitore**, die den Standardbefehl ignorieren, werden über den LG-spezifischen Befehl umgeschaltet (AMD-Grafik)
- **Konfliktprüfung** beim Festlegen einer Tastenkombination – bei einem Konflikt erscheint ein Hinweis,
  übernommen wird die Kombination nur nach ausdrücklicher erneuter Bestätigung (siehe unten)
- **Tray-Menü:** Einstellungen, „Jetzt umschalten“, Tastenkombinationen pausieren, Autostart, Beenden
- Dunkler und heller Modus, hohe DPI-Werte (Per-Monitor V2)

## Warum kein Windows-Dienst?

Ein echter Windows-Dienst läuft isoliert in *Session 0*. Von dort aus kann er weder ein Symbol im Infobereich
anzeigen noch globale Tastenkombinationen der angemeldeten Benutzersitzung empfangen noch per DDC/CI auf deren
Monitore zugreifen. MonSwitch verhält sich deshalb **wie ein Dienst**, ist technisch aber ein schlanker
Hintergrundprozess, der bei der Anmeldung automatisch startet (`HKCU\…\Run`, keine Administratorrechte nötig).

## Ressourcenschonung

| Maßnahme | Wirkung |
|---|---|
| Zwei Prozesse: `MonSwitch.exe` (Hintergrund) und `MonSwitch.Settings.exe` (Oberfläche) | Der Hintergrundprozess lädt nie ein UI-Framework; die Oberfläche läuft nur, solange sie geöffnet ist |
| Reines Win32 im Hintergrundprozess (unsichtbares Fenster, `GetMessage`-Schleife) | Im Leerlauf **0 % CPU** – keine Timer, kein Polling |
| Monitor-Handles nur während eines Umschaltvorgangs | Keine dauerhaft belegten Geräteressourcen |
| EcoQoS („Effizienzmodus“), niedrige Speicherpriorität | Windows plant den Prozess sparsam ein; Prozesspriorität bleibt normal, damit Hotkeys auch unter Last sofort reagieren |
| Workstation-GC ohne Hintergrund-Thread, kein ICU, ReadyToRun | Weniger Threads, weniger Speicher, kein JIT beim Start |
| Working-Set-Trimming nach Start und nach jeder Aktion | Speicher wird an Windows zurückgegeben |

Gemessen unter Windows 11 (x64, .NET 10): **≈ 2 MB Working Set** und **0 ms CPU-Zeit** in 10 s Leerlauf.

## Voraussetzungen

- Windows 10 oder 11 (x64)
- [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) (x64)
- Monitor mit **DDC/CI** – meist im Monitormenü aktivierbar (oft unter „Einstellungen“, „System“ oder „Sonstiges“)

## Installation

1. Unter [Releases](../../releases) das ZIP-Archiv herunterladen.
2. In einen dauerhaften Ordner entpacken, z. B. `%LOCALAPPDATA%\Programs\MonSwitch`.
3. `MonSwitch.exe` starten. Beim ersten Start wird der Autostart eingerichtet und die Einstellungen öffnen sich.

## Bedienung

- **Linksklick** auf das Symbol im Infobereich öffnet die Einstellungen, **Rechtsklick** das Menü.
- Erneutes Starten von `MonSwitch.exe` öffnet ebenfalls die Einstellungen.
- **Hinzufügen…** → in das Feld *Tastenkombination* klicken und die Kombination drücken. Kombinationen mit der
  Windows-Taste fängt Windows teilweise selbst ab – dafür gibt es zusätzlich die Kontrollkästchen
  *Strg / Alt / Umschalt / Win*.
- Monitore und Eingänge wählen. Eingänge, die der Monitor nicht meldet, können als Code eingegeben werden
  (z. B. `0x1B` für USB-C bei vielen Dell-Monitoren).
- **Jetzt testen** schaltet sofort um.
- Solange die Einstellungen geöffnet sind, sind die Tastenkombinationen deaktiviert (damit die Konfliktprüfung
  zuverlässig arbeitet); beim Schließen werden sie wieder aktiv.

### Konfliktprüfung

Beim Festlegen einer Tastenkombination prüft MonSwitch:

| Prüfung | Beispiel |
|---|---|
| Doppelt in MonSwitch | blockiert – kann nicht übernommen werden |
| Von einem **laufenden Programm** (oder Windows) global registriert – Test über `RegisterHotKey` | Win + E |
| **Verknüpfungen** mit Tastenkombination im Startmenü und auf dem Desktop | Strg + Alt + M startet „Programm X“ |
| **PowerToys**-Tastenkombinationen und Keyboard-Manager-Zuordnungen | Win + Umschalt + C (Color Picker) |
| Bekannte **Windows-11-Tastenkombinationen** | Win + Umschalt + Pfeil links |
| Gängige **Programm-Tastenkombinationen** | Strg + C, Alt + F4, F5 |
| **Grafiktreiber-Overlays** (Intel, NVIDIA, AMD) | Alt + Z, Alt + R |
| **Zeicheneingabe** auf dem aktuellen Tastaturlayout | Strg + Alt + Q = AltGr + Q = „@“ |

Wird ein möglicher Konflikt gefunden, zeigt MonSwitch einen Hinweis mit allen Fundstellen. Die Tastenkombination
wird **nur übernommen, wenn dies erneut mit „Trotzdem übernehmen“ bestätigt wird**; die Standardauswahl ist
„Andere Kombination wählen“.

> Programme, die Tastatureingaben über einen globalen Hook abfangen (z. B. AutoHotkey-Skripte), lassen sich nicht
> in jedem Fall erkennen.

## Hinweise und Fehlerbehebung

- **„DDC/CI nicht verfügbar“:** DDC/CI im Monitormenü aktivieren. Dockingstationen, KVM-Switches und
  DisplayLink-Adapter reichen DDC/CI häufig nicht durch.
- **Zurückschalten:** Nach dem Umschalten auf ein anderes Gerät zeigt der Monitor dieses an. Ob der Monitor
  danach noch Befehle von diesem PC annimmt, hängt vom Modell ab – am zuverlässigsten ist MonSwitch (oder ein
  vergleichbares Tool) auf jedem beteiligten Gerät.
- **LG-Monitore:** Viele LG-Modelle (u. a. UltraGear) bestätigen den Standardbefehl (VCP `0x60`), ignorieren ihn
  aber und schalten nur über das LG-Register `0xF4` mit der DDC/CI-Quelladresse `0x50` um. Dieses Paket kann
  Windows selbst nicht senden; MonSwitch schickt es deshalb über den Grafiktreiber – derzeit mit
  **AMD-Grafikkarten** (ADL). Bei LG-Monitoren geschieht das automatisch; im Editor lässt sich der Befehl je Monitor
  auf „Standard“ oder „LG-Befehl“ festlegen. Codes: DisplayPort 1 = `0xD0`, DisplayPort 2 = `0xD1`,
  HDMI 1 = `0x90`, HDMI 2 = `0x91`, USB-C = `0xD2` (Standardeingänge werden automatisch umgerechnet,
  abweichende LG-Codes können direkt eingegeben werden).
  Solche Monitore melden den aktuellen Eingang nicht korrekt. Beim Hin- und Herschalten richtet sich MonSwitch
  daher nach dem zuletzt selbst gesetzten Eingang (`%LOCALAPPDATA%\MonSwitch\lastinput.txt`). Wurde der Eingang
  zwischendurch am Monitor selbst umgestellt, ist ggf. ein zweiter Tastendruck nötig.
- **Herstellerspezifische Codes:** Manche Monitore (u. a. einige Samsung-Modelle) verwenden andere Werte als der
  Standard. Den passenden Code ggf. im Editor direkt eingeben.
- **Konfiguration:** `%APPDATA%\MonSwitch\config.json`
- **Protokoll (nur Fehler):** `%LOCALAPPDATA%\MonSwitch\monswitch.log`
- **Beenden per Kommandozeile:** `MonSwitch.exe --exit`

## Entwicklung

```powershell
dotnet build MonSwitch.slnx          # Debug-Build
dotnet test  MonSwitch.slnx          # Unit-Tests
./build.ps1                          # Release nach artifacts\MonSwitch
./build.ps1 -Zip                     # zusätzlich ZIP-Archiv
./build.ps1 -Aot                     # Hintergrundprozess als Native AOT (benötigt VS Build Tools mit C++)
```

| Projekt | Inhalt |
|---|---|
| `src/MonSwitch.Core` | DDC/CI, Monitorerkennung, Konfiguration, Konfliktprüfung, Autostart, IPC (AOT-kompatibel) |
| `src/MonSwitch` | Hintergrundprozess: Tray-Symbol, Hotkeys, Umschalten (reines Win32) |
| `src/MonSwitch.Settings` | Einstellungsoberfläche (WinForms) |
| `tests/MonSwitch.Core.Tests` | Unit-Tests (xUnit) |

Zum Testen ohne Einfluss auf die eigene Konfiguration kann der Konfigurationsordner über die Umgebungsvariable
`MONSWITCH_CONFIG_DIR` umgelenkt werden.

### Branches und Releases

- **`DEV`** – Entwicklung. Jeder Push wird von GitHub Actions gebaut und getestet.
- **`main`** – Releases. Ein Push bzw. Merge auf `main` erzeugt automatisch ein GitHub-Release mit der Version aus
  `Directory.Build.props` (z. B. `v1.0.0`). Für ein neues Release vorher die Version erhöhen.
