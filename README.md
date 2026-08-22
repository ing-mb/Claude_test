# abas-GUI-Anbindung über DDE

Datensätze aus einer eigenen Anwendung heraus in einer **bereits geöffneten**
abas-ERP-Sitzung anzeigen — so, wie es der ABAS-EDPViewer beim Klick auf einen
Datensatz tut.

## Wie es funktioniert

Die abas-GUI (`abasgui.exe`) ist ein DDE-Server. Ein Datensatz wird geöffnet,
indem man ihr die EDP-Referenz als Befehl schickt:

```
DDE-Dienst : <GUIDDESRVNAME>      z. B. "999"
DDE-Thema  : COMMAND
Befehl     : (2000068,4,0)        ANSI, nullterminiert
```

Der Dienstname wird pro GUI-Sitzung vergeben und über die EDP-Variable
`GUIDDESRVNAME` abgefragt. Vor dem Öffnen prüft der EDPViewer über das Thema
`System` das Element `CLIENT` — die Antwort muss der Mandantenname sein.

Die vollständige Beschreibung samt Fallstricken steht in
[`docs/abas-gui-dde-protokoll.md`](docs/abas-gui-dde-protokoll.md).

## Inhalt

| Pfad | Zweck |
|---|---|
| `src/AbasGuiLink/AbasGuiLink.cs` | C#-Klasse für den produktiven Einsatz |
| `src/AbasGuiLink/Beispiel.cs` | Verwendungsbeispiele |
| `scripts/Open-AbasRecord.ps1` | PowerShell-Skript zum schnellen Ausprobieren |
| `docs/abas-gui-dde-protokoll.md` | Protokollbeschreibung |
| `docs/umsetzung-in-bestehendem-projekt.md` | Übergabe-Anleitung für die Umsetzung in einem anderen Projekt, inklusive offener Punkte |

## Schnellstart

PowerShell — läuft in `powershell.exe` wie in `pwsh`, ohne Administratorrechte:

```powershell
# GUI über den Mandantennamen suchen
.\scripts\Open-AbasRecord.ps1 -Client DEMO -Reference '(2000068,4,0)'

# oder mit bekanntem Dienstnamen
.\scripts\Open-AbasRecord.ps1 -DdeName 999 -Reference '(2000068,4,0)'

# laufende GUIs auflisten
.\scripts\Open-AbasRecord.ps1 -ListGuis
```

C# — mit bekanntem Dienstnamen aus der EDP-Verbindung:

```csharp
using (var gui = new AbasGuiLink(ddeName))
{
    if (gui.IsAvailable(mandant))
        gui.OpenRecord(recordNumber: 2000068, databaseNumber: 4);
}
```

C# — ohne EDP-Verbindung, die GUI wird über den Mandanten gesucht:

```csharp
using (var gui = AbasGuiLink.Connect("DEMO"))
{
    gui.OpenRecord("(2000068,4,0)");
}
```

## Voraussetzungen

* abas ERP läuft in derselben Windows-Sitzung wie die aufrufende Anwendung
* keine Administratorrechte, keine Installation, keine zusätzliche Bibliothek
* `GUIDDESRVNAME` und `MANDANT` aus der bestehenden EDP-Verbindung — im
  EDP-Protokoll `SHO|0|GUIDDESRVNAME|0` und `SHO|0|MANDANT|0`.
  Alternativ `AbasGuiLink.Connect(mandant)`, dann wird gar keine
  EDP-Verbindung gebraucht.

Den Dienstnamen nicht fest hinterlegen: Er ändert sich mit jedem Start der GUI.
