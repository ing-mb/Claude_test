# Datensatz in der laufenden abas-ERP-GUI öffnen (DDE)

Dokumentation des Mechanismus, mit dem der ABAS-EDPViewer beim Klick auf einen
Datensatz diesen in einer bereits geöffneten abas-Sitzung anzeigt.

Ermittelt durch Analyse von `EDPViewer.exe` (Visual Basic 6, ABAS Software AG,
1999–2011) sowie durch Mitschnitt der Kommunikation mit einem DDE-Server, der
sich unter dem Namen der GUI angemeldet hat.

## Kurzfassung

Die abas-GUI (`abasgui.exe`) meldet sich bei Windows als **DDE-Server** an. Der
Servername ist eine Zahl, die der abas-Server pro Sitzung vergibt. Ein Datensatz
wird geöffnet, indem man dem DDE-Thema `COMMAND` die **abas-Referenz** des
Datensatzes als Befehl schickt — als ANSI-Text, ohne weitere Verpackung.

```
DDE-Dienst : <GUIDDESRVNAME>      z. B. "999"
DDE-Thema  : COMMAND
Befehl     : (2000068,4,0)        ANSI, nullterminiert
```

## Vollständiger Ablauf

| Schritt | Kanal | Anfrage | Antwort |
|---|---|---|---|
| 1 | EDP | `SHO\|0\|MANDANT\|0` | `DEMO` |
| 2 | EDP | `SHO\|0\|GUIDDESRVNAME\|0` | `999` |
| 3 | DDE `999\|System` | `XTYP_REQUEST`, Item `CLIENT`, Format `CF_TEXT` | muss den Mandantennamen liefern (`DEMO`) |
| 4 | DDE `999\|COMMAND` | `XTYP_EXECUTE` mit `(2000068,4,0)` | Datensatz öffnet sich in der GUI |

Schritt 3 ist eine Lebendprüfung. Weicht die Antwort vom erwarteten
Mandantennamen ab, bricht der Aufrufer ab — beim EDPViewer mit der Meldung
*„GUI für Mandanten »DEMO« nicht gefunden. Ist die GUI gestartet?"*

## Aufbau der Referenz

```
(<Satznummer>,<Datenbanknummer>,<Tabellenzeile>)
```

Das ist dieselbe Zeichenkette, die die EDP-Schnittstelle als Objekt-Referenz
liefert — im EDP-Protokoll etwa in `RDP|0|(1956088,4,0)|dnr,gruppe|0|1|1`. Sie
kann unverändert als DDE-Befehl weitergereicht werden; eine Umrechnung ist nicht
nötig.

Die Tabellenzeile ist `0` für den Kopfsatz.

## Fallstricke

**Kodierung.** Der Befehl muss als **ANSI** übertragen werden. Wird er als UTF-16
geschickt, quittiert die GUI mit „unbekanntes Kommando" oder „nicht gefunden".
Praktisch heißt das: DDEML mit `DdeInitializeA` und `CP_WINANSI` (1004)
initialisieren, nicht mit den `…W`-Varianten.

**Keine Verpackung.** Weder `<obj>…`, noch ein Befehlswort, noch XML. Alle drei
Varianten werden abgelehnt:

| Gesendet | Antwort der GUI |
|---|---|
| `<ABASData><edit><obj>…</obj></edit></ABASData>` | unbekanntes Kommando |
| `<hole><obj>%…` | unbekanntes Kommando |
| `<obj>%1956088,4,4` | *…*: nicht gefunden |
| `(2000068,4,0)` | **öffnet den Datensatz** |

**Der Servername ist nicht konstant.** `GUIDDESRVNAME` wird pro Sitzung vergeben
und ändert sich, sobald die GUI neu gestartet wird. Er gehört deshalb zur
Laufzeit über die EDP-Verbindung abgefragt und nicht in eine Konfiguration
eingetragen.

## Weitere beobachtete Befehle

Auf dem Thema `COMMAND` versteht die GUI zusätzlich abas-Tastenbefehle in spitzen
Klammern. `<hole>` und `<ändern>` öffnen die jeweilige Maske; `<edit>` und
`<call>` sind unbekannt. Für das Öffnen eines Datensatzes werden sie nicht
benötigt.

Neben `999` meldet sich eine zweite Gegenstelle namens `ABAS-EKS` an; sie wird
vom EDPViewer ebenfalls über das Thema `System` abgefragt, für das Öffnen eines
Datensatzes aber nicht verwendet.

## Verwendung

Siehe `src/AbasGuiLink/AbasGuiLink.cs` (C#) und `scripts/Open-AbasRecord.ps1`
(PowerShell, zum schnellen Ausprobieren).
