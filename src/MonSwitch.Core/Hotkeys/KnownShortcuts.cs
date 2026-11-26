namespace MonSwitch.Core.Hotkeys;

public enum ShortcutSource
{
    Windows,
    Application,
    Driver,
}

public sealed record KnownShortcut(HotkeyModifiers Modifiers, uint Key, string Description, ShortcutSource Source);

/// <summary>
/// Bekannte Tastenkombinationen von Windows 11, gängigen Programmen und Grafiktreibern.
/// Viele davon werden über Tastatur-Hooks verarbeitet und sind deshalb per RegisterHotKey nicht erkennbar.
/// </summary>
public static class KnownShortcuts
{
    private const HotkeyModifiers W = HotkeyModifiers.Win;
    private const HotkeyModifiers C = HotkeyModifiers.Control;
    private const HotkeyModifiers A = HotkeyModifiers.Alt;
    private const HotkeyModifiers S = HotkeyModifiers.Shift;
    private const HotkeyModifiers None = HotkeyModifiers.None;

    private const uint Tab = 0x09, Return = 0x0D, Esc = 0x1B, Space = 0x20, End = 0x23, Home = 0x24,
        Left = 0x25, Up = 0x26, Right = 0x27, Down = 0x28, PrintScreen = 0x2C, Insert = 0x2D, Delete = 0x2E,
        Back = 0x08, Pause = 0x13, Add = 0x6B, Subtract = 0x6D, OemPlus = 0xBB, OemComma = 0xBC, OemMinus = 0xBD, OemPeriod = 0xBE;

    private static uint F(int n) => (uint)(0x6F + n);

    public static IReadOnlyList<KnownShortcut> All { get; } = Build();

    public static IEnumerable<KnownShortcut> Find(Hotkey hotkey)
    {
        foreach (var s in All)
            if (s.Modifiers == hotkey.Modifiers && s.Key == hotkey.Key)
                yield return s;
    }

    private static List<KnownShortcut> Build()
    {
        var list = new List<KnownShortcut>(256);
        void Win(HotkeyModifiers m, uint k, string d) => list.Add(new(m, k, d, ShortcutSource.Windows));
        void App(HotkeyModifiers m, uint k, string d) => list.Add(new(m, k, d, ShortcutSource.Application));
        void Drv(HotkeyModifiers m, uint k, string d) => list.Add(new(m, k, d, ShortcutSource.Driver));

        // --- Windows-Taste + Buchstabe ---
        Win(W, 'A', "Schnelleinstellungen öffnen");
        Win(W, 'B', "Fokus auf den Infobereich der Taskleiste");
        Win(W, 'C', "Copilot öffnen");
        Win(W, 'D', "Desktop anzeigen/ausblenden");
        Win(W, 'E', "Datei-Explorer öffnen");
        Win(W, 'F', "Feedback-Hub öffnen");
        Win(W, 'G', "Xbox Game Bar öffnen");
        Win(W, 'H', "Spracheingabe (Diktat) starten");
        Win(W, 'I', "Einstellungen öffnen");
        Win(W, 'J', "Von Windows reserviert");
        Win(W, 'K', "Übertragen (Cast) öffnen");
        Win(W, 'L', "Computer sperren");
        Win(W, 'M', "Alle Fenster minimieren");
        Win(W, 'N', "Benachrichtigungszentrale und Kalender öffnen");
        Win(W, 'O', "Bildschirmausrichtung sperren");
        Win(W, 'P', "Projektionsmodus (Anzeigemodus) wählen");
        Win(W, 'Q', "Suche öffnen");
        Win(W, 'R', "Ausführen-Dialog öffnen");
        Win(W, 'S', "Suche öffnen");
        Win(W, 'T', "Apps in der Taskleiste durchlaufen");
        Win(W, 'U', "Einstellungen für Barrierefreiheit öffnen");
        Win(W, 'V', "Zwischenablageverlauf öffnen");
        Win(W, 'W', "Widgets öffnen");
        Win(W, 'X', "Schnellzugriffsmenü (Rechtsklick auf Start) öffnen");
        Win(W, 'Y', "Eingabe zwischen Mixed Reality und Desktop umschalten");
        Win(W, 'Z', "Snap-Layouts öffnen");

        // --- Windows-Taste + Ziffer (Taskleisten-Apps) ---
        for (uint d = '0'; d <= '9'; d++)
        {
            int pos = d == '0' ? 10 : (int)(d - '0');
            Win(W, d, $"Taskleisten-App an Position {pos} starten bzw. dorthin wechseln");
            Win(W | S, d, $"Neue Instanz der Taskleisten-App an Position {pos} starten");
            Win(W | C, d, $"Zum zuletzt aktiven Fenster der Taskleisten-App an Position {pos} wechseln");
            Win(W | A, d, $"Sprungliste der Taskleisten-App an Position {pos} öffnen");
            Win(W | C | S, d, $"Taskleisten-App an Position {pos} als Administrator starten");
        }

        // --- Fenster, Desktops, Monitore ---
        Win(W, Tab, "Aktive Ansicht (Task View) öffnen");
        Win(W, Left, "Fenster links andocken");
        Win(W, Right, "Fenster rechts andocken");
        Win(W, Up, "Fenster maximieren");
        Win(W, Down, "Fenster wiederherstellen/minimieren");
        Win(W | S, Left, "Fenster auf den linken Monitor verschieben");
        Win(W | S, Right, "Fenster auf den rechten Monitor verschieben");
        Win(W | S, Up, "Fenster vertikal strecken");
        Win(W | S, Down, "Vertikal gestrecktes Fenster wiederherstellen");
        Win(W | A, Up, "Fenster in der oberen Bildschirmhälfte andocken");
        Win(W | A, Down, "Fenster in der unteren Bildschirmhälfte andocken");
        Win(W, Home, "Alle Fenster außer dem aktiven minimieren");
        Win(W | S, 'M', "Minimierte Fenster wiederherstellen");
        Win(W | C, 'D', "Neuen virtuellen Desktop erstellen");
        Win(W | C, F(4), "Aktuellen virtuellen Desktop schließen");
        Win(W | C, Left, "Zum linken virtuellen Desktop wechseln");
        Win(W | C, Right, "Zum rechten virtuellen Desktop wechseln");
        Win(W, OemComma, "Desktop kurz anzeigen");

        // --- System, Eingabe, Barrierefreiheit ---
        Win(W, Pause, "Systeminformationen öffnen");
        Win(W, PrintScreen, "Screenshot erstellen und speichern");
        Win(W, Space, "Eingabesprache/Tastaturlayout wechseln");
        Win(W | C, Space, "Zur vorherigen Eingabesprache wechseln");
        Win(W, OemPeriod, "Emoji-Panel öffnen");
        Win(W, OemPlus, "Bildschirmlupe: vergrößern");
        Win(W, Add, "Bildschirmlupe: vergrößern");
        Win(W, OemMinus, "Bildschirmlupe: verkleinern");
        Win(W, Subtract, "Bildschirmlupe: verkleinern");
        Win(W, Esc, "Bildschirmlupe beenden");
        Win(W | C, Return, "Sprachausgabe ein-/ausschalten");
        Win(W | C, 'C', "Farbfilter ein-/ausschalten");
        Win(W | C, 'F', "Computer im Netzwerk suchen");
        Win(W | C, 'N', "Einstellungen der Sprachausgabe öffnen");
        Win(W | C, 'O', "Bildschirmtastatur öffnen");
        Win(W | C, 'Q', "Remotehilfe (Schnellhilfe) öffnen");
        Win(W | C, 'S', "Windows-Spracherkennung");
        Win(W | C, 'V', "Lautstärkemixer/Soundausgabe öffnen");
        Win(W | C | S, 'B', "Grafiktreiber neu starten");
        Win(W | S, 'S', "Bildschirmausschnitt aufnehmen (Snipping Tool)");
        Win(W | S, 'R', "Bildschirmvideo aufnehmen (Snipping Tool)");
        Win(W | A, 'B', "HDR ein-/ausschalten");
        Win(W | A, 'D', "Datum und Uhrzeit in der Taskleiste anzeigen");
        Win(W | A, 'G', "Die letzten Sekunden aufzeichnen (Game Bar)");
        Win(W | A, 'R', "Bildschirmaufnahme starten/beenden (Game Bar)");
        Win(W | A, 'K', "Mikrofon in Anrufen stummschalten");
        Win(W | A, PrintScreen, "Screenshot des Spiels (Game Bar)");

        // --- Klassische Systemkombinationen ---
        Win(C | A, Delete, "Sicherheitsoptionen (Sperren, Abmelden, Task-Manager)");
        Win(C, Esc, "Startmenü öffnen");
        Win(C | S, Esc, "Task-Manager öffnen");
        Win(A, Tab, "Zwischen Fenstern wechseln");
        Win(A | S, Tab, "Zwischen Fenstern rückwärts wechseln");
        Win(C | A, Tab, "Fensterübersicht geöffnet halten");
        Win(A, Esc, "Fenster in Öffnungsreihenfolge durchlaufen");
        Win(A, F(4), "Aktives Fenster bzw. Programm schließen");
        Win(A, Space, "Fenstermenü öffnen");
        Win(A, Return, "Eigenschaften anzeigen bzw. Vollbild umschalten");
        Win(A, PrintScreen, "Screenshot des aktiven Fensters");
        Win(None, PrintScreen, "Bildschirmausschnitt aufnehmen (Snipping Tool)");
        Win(S, F(10), "Kontextmenü öffnen");
        Win(None, F(12), "Von Windows für Debugger reserviert");

        // --- Funktionstasten in Programmen ---
        App(None, F(1), "Hilfe");
        App(None, F(2), "Umbenennen (Explorer) bzw. Bearbeiten");
        App(None, F(3), "Suchen bzw. Weitersuchen");
        App(None, F(4), "Adressleiste (Explorer)");
        App(None, F(5), "Aktualisieren");
        App(None, F(6), "Fensterbereiche durchlaufen / Adressleiste (Browser)");
        App(None, F(7), "Rechtschreibprüfung (Office) bzw. Caret-Browsing");
        App(None, F(10), "Menüleiste aktivieren");
        App(None, F(11), "Vollbild umschalten");

        // --- Strg-Kombinationen in nahezu allen Programmen ---
        App(C, 'A', "Alles markieren");
        App(C, 'B', "Fett / Lesezeichen");
        App(C, 'C', "Kopieren");
        App(C, 'D', "Lesezeichen hinzufügen / Löschen");
        App(C, 'E', "Suchen (Browser/Explorer)");
        App(C, 'F', "Suchen");
        App(C, 'G', "Weitersuchen / Gehe zu");
        App(C, 'H', "Ersetzen / Verlauf");
        App(C, 'I', "Kursiv");
        App(C, 'J', "Downloads (Browser) / Blocksatz");
        App(C, 'K', "Link einfügen");
        App(C, 'L', "Adressleiste (Browser)");
        App(C, 'N', "Neu (Dokument/Fenster)");
        App(C, 'O', "Öffnen");
        App(C, 'P', "Drucken");
        App(C, 'Q', "Programm beenden");
        App(C, 'R', "Aktualisieren");
        App(C, 'S', "Speichern");
        App(C, 'T', "Neuer Tab");
        App(C, 'U', "Unterstreichen / Seitenquelltext");
        App(C, 'V', "Einfügen");
        App(C, 'W', "Tab bzw. Fenster schließen");
        App(C, 'X', "Ausschneiden");
        App(C, 'Y', "Wiederholen");
        App(C, 'Z', "Rückgängig");
        App(C, Tab, "Nächster Tab");
        App(C | S, Tab, "Vorheriger Tab");
        App(C, F(4), "Dokument bzw. Tab schließen");
        App(C | S, 'N', "Neuer Ordner (Explorer) / privates Fenster (Browser)");
        App(C | S, 'T', "Geschlossenen Tab wiederherstellen");
        App(C | S, 'V', "Unformatiert einfügen");
        App(C | S, 'Z', "Wiederholen");
        App(C | S, 'S', "Speichern unter");
        App(C | S, 'P', "Privates Fenster / Befehlspalette");
        App(C | S, Delete, "Browserdaten löschen");
        App(C, OemPlus, "Vergrößern (Zoom)");
        App(C, Add, "Vergrößern (Zoom)");
        App(C, OemMinus, "Verkleinern (Zoom)");
        App(C, Subtract, "Verkleinern (Zoom)");
        App(C, '0', "Zoom zurücksetzen");
        for (uint d = '1'; d <= '9'; d++)
            App(C, d, $"Zu Tab {d - '0'} wechseln (Browser) / Formatvorlage");
        App(C, Home, "Zum Dokumentanfang");
        App(C, End, "Zum Dokumentende");
        App(C, Left, "Wortweise nach links");
        App(C, Right, "Wortweise nach rechts");
        App(C, Up, "Absatzweise nach oben");
        App(C, Down, "Absatzweise nach unten");
        App(C, Back, "Wort links löschen");
        App(C, Delete, "Wort rechts löschen");
        App(C, Return, "Senden / Seitenumbruch");
        App(C, Space, "Formatierung entfernen / Eingabemethode");
        App(C, Insert, "Kopieren");
        App(S, Insert, "Einfügen");
        App(S, Delete, "Ausschneiden bzw. endgültig löschen");
        App(C | S, Left, "Wortweise markieren");
        App(C | S, Right, "Wortweise markieren");
        App(C | S, Home, "Bis zum Dokumentanfang markieren");
        App(C | S, End, "Bis zum Dokumentende markieren");
        App(S, Left, "Text markieren");
        App(S, Right, "Text markieren");
        App(S, Up, "Text markieren");
        App(S, Down, "Text markieren");
        App(S, Home, "Bis zum Zeilenanfang markieren");
        App(S, End, "Bis zum Zeilenende markieren");
        App(S, Tab, "Rückwärts navigieren");
        App(A, Left, "Zurück (Browser/Explorer)");
        App(A, Right, "Vorwärts (Browser/Explorer)");
        App(A, Up, "Übergeordneter Ordner (Explorer)");
        App(A, Home, "Startseite (Browser)");

        // --- Grafiktreiber / Overlays ---
        foreach (uint arrow in new[] { Left, Up, Right, Down })
            Drv(C | A, arrow, "Bildschirm drehen (Grafiktreiber-Hotkeys, z. B. Intel)");
        Drv(C | A, F(12), "Intel-Grafiksteuerung öffnen (falls installiert)");
        Drv(A, 'Z', "NVIDIA-Overlay öffnen (falls installiert)");
        Drv(A, F(1), "NVIDIA: Screenshot (falls installiert)");
        Drv(A, F(9), "NVIDIA: Aufnahme starten/beenden (falls installiert)");
        Drv(A, F(10), "NVIDIA: Instant Replay speichern (falls installiert)");
        Drv(A, 'R', "AMD Software: Overlay öffnen (falls installiert)");
        Drv(C | S, 'O', "AMD Software: Leistungsmetriken ein/aus (falls installiert)");
        Drv(C | S, 'E', "AMD Software: Aufnahme starten/beenden (falls installiert)");
        Drv(C | S, 'I', "AMD Software: Screenshot (falls installiert)");

        return list;
    }
}
