# Umsetzung in einem bestehenden EDP-Projekt

Übergabe-Anleitung. Sie ist bewusst so geschrieben, dass sie ohne den
Entstehungskontext auskommt — sie kann in ein anderes Repository kopiert oder
einer neuen Claude-Code-Sitzung als Auftrag vorgelegt werden.

---

## 1. Auftrag

Aus der eigenen Anwendung heraus einen Datensatz in einer **bereits geöffneten**
abas-ERP-Sitzung anzeigen — genau der Mechanismus, den der ABAS-EDPViewer beim
Klick auf einen Datensatz benutzt.

## 2. Stand der Dinge

Der Mechanismus ist vollständig entschlüsselt und **praktisch verifiziert**:
Datensätze öffnen sich zuverlässig in der laufenden GUI.

Ermittelt wurde er durch Analyse von `EDPViewer.exe` (Visual Basic 6, ABAS
Software AG) und durch Mitschnitt der Kommunikation mit einem DDE-Server, der
sich unter dem Namen der GUI angemeldet hat.

Testumgebung: abas ERP, Mandant `DEMO`, Host `zi-abasrh01`, EDP-Port `6550`,
GUI gestartet als `\\zi-abasrh01\demo\abasgui.exe -server server_1`.
Aufrufer: PowerShell 7 (64 Bit), ohne Administratorrechte.

## 3. Das Protokoll

Die abas-GUI (`abasgui.exe`) ist ein **DDE-Server**. Ein Datensatz wird
geöffnet, indem man seine EDP-Referenz als Befehl an das Thema `COMMAND`
schickt.

| Schritt | Kanal | Anfrage | Antwort |
|---|---|---|---|
| 1 | EDP | `SHO\|0\|MANDANT\|0` | `DEMO` |
| 2 | EDP | `SHO\|0\|GUIDDESRVNAME\|0` | `999` |
| 3 | DDE `999\|System` | `XTYP_REQUEST`, Item `CLIENT`, Format `CF_TEXT` | muss den Mandantennamen liefern |
| 4 | DDE `999\|COMMAND` | `XTYP_EXECUTE` mit `(2000068,4,0)` | Datensatz öffnet sich |

Schritt 3 ist eine Lebendprüfung. Weicht die Antwort vom erwarteten
Mandantennamen ab, bricht der EDPViewer ab mit
*„GUI für Mandanten »DEMO« nicht gefunden. Ist die GUI gestartet?"*

**Aufbau der Referenz:**

```
(<Satznummer>,<Datenbanknummer>,<Tabellenzeile>)
```

Das ist dieselbe Zeichenkette, die die EDP-Schnittstelle als Objekt-Referenz
liefert — im EDP-Protokoll etwa in `RDP|0|(1956088,4,0)|dnr,gruppe|0|1|1`. Sie
wird **unverändert** weitergereicht; eine Umrechnung findet nicht statt. Die
Tabellenzeile ist `0` für den Kopfsatz.

## 4. Was in diesem Projekt zu tun ist

### 4.1 Klasse übernehmen

`src/AbasGuiLink/AbasGuiLink.cs` in das Projekt kopieren. Eine einzelne Datei,
keine Referenz, kein NuGet-Paket, keine COM-Registrierung — sie spricht die
Windows-DDE-Schnittstelle (DDEML in `user32.dll`) direkt an.

Öffentliche Schnittstelle:

```csharp
new AbasGuiLink(ddeServiceName)          // Verbindung mit bekanntem Dienstnamen
AbasGuiLink.Connect(mandant)             // Dienstnamen suchen und verbinden
AbasGuiLink.Discover(mandant)            // nur den Dienstnamen ermitteln
AbasGuiLink.FindGuis(mandant = null)     // alle laufenden GUIs auflisten

gui.OpenRecord("(2000068,4,0)")          // Datensatz öffnen
gui.OpenRecord(2000068, 4)               // dito, aus Bestandteilen
gui.QueryClient()                        // Mandantennamen der GUI lesen
gui.IsAvailable(mandant)                 // Lebendprüfung
gui.SendCommand(text)                    // beliebiger Befehl an COMMAND
```

### 4.2 Die zwei EDP-Variablen lesen — **die einzige offene Stelle**

Auf Protokollebene sind es zwei Aufrufe des EDP-Kommandos `SHO`
(„Variable anzeigen"):

```
SHO|0|MANDANT|0            ->  DEMO
SHO|0|GUIDDESRVNAME|0      ->  999
```

Wie diese über den vorhandenen ActiveX-Wrapper gelesen werden, ist projektweit
sicher schon gelöst — es ist derselbe Aufruf, mit dem auch `ServerEDPVersion`
oder `AktBenutzer` gelesen werden. Typische Methodennamen sind `Show`,
`GetVariable`, `ShowVar` oder ein generisches `SendCommand`/`Execute` für
Rohkommandos.

**Vorgehen:** Im bestehenden Code die Stelle suchen, an der die EDP-Verbindung
aufgebaut wird und eine Variable gelesen wird. Suchbegriffe:

```
EDPSession      buildEDPConnection      CreateObject("EDP
EDPActiveX      edpapi                  MANDANT
GUIDDESRVNAME   ServerEDPVersion        AktBenutzer
```

Dann die beiden Werte holen und einsetzen:

```csharp
string mandant = edp.GetVariable("MANDANT");          // Methodenname anpassen
string ddeName = edp.GetVariable("GUIDDESRVNAME");    // Methodenname anpassen

using (var gui = new AbasGuiLink(ddeName))
{
    if (gui.IsAvailable(mandant))
        gui.OpenRecord(referenz);
}
```

**Falls sich der Aufruf nicht finden lässt:** Es gibt einen Weg ohne EDP.
`AbasGuiLink.Connect(mandant)` sucht die GUI selbst — die Dienstnamen sind
fortlaufende Zahlen, und jede GUI beantwortet die Anfrage `CLIENT` mit ihrem
Mandanten. Ein Vorfilter über die globale Atomtabelle von Windows
(`GlobalFindAtom`) hält das billig; der Durchlauf dauert Millisekunden. Der
Mandantenname wird dafür trotzdem gebraucht, ist aber meist ohnehin bekannt.

### 4.3 Die Referenz des Datensatzes besorgen

Die Objekt-Referenz stammt aus dem EDP-Ergebnis der eigenen Selektion — dort,
wo heute schon über Datensätze iteriert wird. Wichtig ist nur, dass die
Zeichenkette die Form `(id,dnr,zeile)` hat.

Liegen die Bestandteile einzeln vor, hilft:

```csharp
string referenz = AbasGuiLink.BuildReference(recordNumber: 2000068, databaseNumber: 4);
```

Die Gruppennummer wird **nicht** gebraucht. Der EDPViewer liest sie zwar
(`RDP|0|(...)|dnr,gruppe|...`), verwendet sie im DDE-Befehl aber nicht.

### 4.4 Abnahme

1. `AbasGuiLink.FindGuis()` liefert die laufende GUI mit dem richtigen Mandanten
2. `OpenRecord` mit einer frischen Referenz öffnet den Datensatz in der Sitzung
3. Bei geschlossener GUI wirft der Aufruf eine `AbasGuiException` mit
   verständlichem Text statt stillschweigend nichts zu tun

Zum Gegenprüfen ohne Anwendung:

```powershell
.\scripts\Open-AbasRecord.ps1 -ListGuis
.\scripts\Open-AbasRecord.ps1 -Client DEMO -Reference '(2000068,4,0)'
```

## 5. Offene Punkte

| Punkt | Stand |
|---|---|
| Methodenname im EDP-ActiveX für `SHO` | **nicht ermittelt.** Wrapper lag nicht vor. Siehe 4.2 |
| Kompilierfähigkeit von `AbasGuiLink.cs` | **nicht geprüft.** In der Entwicklungsumgebung stand kein C#-Compiler zur Verfügung. Der Code wurde sorgfältig geprüft, aber nie übersetzt |
| Verhalten bei mehreren gleichzeitig geöffneten Mandanten | nicht getestet. `FindGuis()` ist dafür ausgelegt, wurde aber nur mit einer GUI erprobt |
| Dienst `ABAS-EKS` | existiert als zweite DDE-Gegenstelle und wird vom EDPViewer über `System` abgefragt. Für das Öffnen eines Datensatzes ohne Bedeutung, Zweck ungeklärt |
| Befehle `<hole>` und `<ändern>` | funktionieren auf dem Thema `COMMAND` und öffnen die jeweilige Maske. Nicht weiter untersucht, für diesen Zweck nicht nötig |

## 6. Fallstricke

**ANSI, nicht UTF-16.** Wird der Befehl als Unicode geschickt, antwortet die GUI
mit „unbekanntes Kommando" oder „nicht gefunden". In DDEML heißt das:
`DdeInitializeA` und `CP_WINANSI` (1004) verwenden, nicht die `…W`-Varianten.
Das war der Grund, warum die ersten Versuche scheiterten, obwohl der Befehl
inhaltlich stimmte.

**Keine Verpackung.** Der Befehl besteht nur aus der Referenz. Alles andere wird
abgelehnt:

| Gesendet | Antwort der GUI |
|---|---|
| `<ABASData><edit><obj>…</obj></edit></ABASData>` | unbekanntes Kommando |
| `<hole><obj>%…` | unbekanntes Kommando |
| `<obj>%1956088,4,4` | *…*: nicht gefunden |
| `(2000068,4,0)` | **öffnet den Datensatz** |

Das `<ABASData>`-XML ist das Export- und Drag-Format des EDPViewers, nicht das
GUI-Protokoll. Diese Verwechslung hat mehrere Anläufe gekostet.

**Der Dienstname ist nicht konstant.** `GUIDDESRVNAME` wird pro GUI-Sitzung neu
vergeben. Er darf nicht in eine Konfiguration eingetragen werden, sondern gehört
zur Laufzeit ermittelt.

**Gleiche Windows-Sitzung.** DDE funktioniert nur zwischen Prozessen desselben
Benutzers auf demselben Desktop. Ein Dienst oder eine andere RDP-Sitzung
erreicht die GUI nicht. Über Bitness hinweg funktioniert es dagegen problemlos —
getestet wurde 64-Bit-Aufrufer gegen die abas-GUI.

## 7. Anhang: Diagnose, falls etwas nicht passt

Alle folgenden Werkzeuge laufen ohne Administratorrechte in `pwsh`.

**Läuft überhaupt eine GUI, und wie heißt sie?**

```powershell
.\scripts\Open-AbasRecord.ps1 -ListGuis
```

**Was schickt der EDPViewer tatsächlich?** Im EDPViewer die Protokollierung
einschalten (Menü „EDP-Einstellungen"), auf einen Datensatz klicken, dann
`C:\Temp\edp.log` ansehen. Dort stehen die `SHO`-Kommandos und die Referenz des
angeklickten Satzes.

**Den DDE-Verkehr mitlesen.** Wenn der eigene Aufruf abgelehnt wird, der
EDPViewer aber funktioniert: abas ERP schließen, einen eigenen DDE-Server unter
demselben Namen anmelden, der auf `CLIENT` mit dem Mandantennamen antwortet, und
den `WM_DDE_EXECUTE`-Inhalt protokollieren. Genau so wurde der Befehl
ursprünglich gefunden. Der Aufbau eines solchen Horchers ist in der
Entstehungsgeschichte dieses Repositories dokumentiert; die Kernpunkte:

* Fenster mit eigener `WndProc` anlegen (reine Win32-Aufrufe genügen, kein
  WinForms — das erspart Verweisprobleme in PowerShell 7)
* auf `WM_DDE_INITIATE` (0x03E0) mit `WM_DDE_ACK` antworten
* auf `WM_DDE_REQUEST` (0x03E6) für Item `CLIENT` den Mandantennamen als
  `CF_TEXT` in einer `DDEDATA`-Struktur zurückgeben
* `WM_DDE_EXECUTE` (0x03E8) auswerten: `lParam` ist das Speicherhandle, falls
  `GlobalSize` 0 liefert, vorher `UnpackDDElParam`

## 8. Verweise

* `README.md` — Überblick und Schnellstart
* `docs/abas-gui-dde-protokoll.md` — Protokollbeschreibung
* `src/AbasGuiLink/AbasGuiLink.cs` — die Klasse
* `src/AbasGuiLink/Beispiel.cs` — Verwendungsbeispiele
* `scripts/Open-AbasRecord.ps1` — Skript zum Gegenprüfen
