<#
.SYNOPSIS
    Oeffnet einen Datensatz in der laufenden abas-ERP-Sitzung.

.DESCRIPTION
    Schickt die abas-Referenz per DDE an die GUI. Laeuft in Windows PowerShell
    5.1 und in PowerShell 7 (pwsh), benoetigt keine Administratorrechte.

    Der DDE-Dienstname ist die EDP-Variable GUIDDESRVNAME. Er wird pro
    GUI-Sitzung neu vergeben; nach einem Neustart der GUI ist er ein anderer.
    Auslesen laesst er sich aus dem EDP-Protokoll des EDPViewers
    (Zeile "SHO|0|GUIDDESRVNAME|0") oder ueber die eigene EDP-Verbindung.

    Protokollbeschreibung: docs/abas-gui-dde-protokoll.md

.PARAMETER DdeName
    Wert von GUIDDESRVNAME, z. B. "999".

.PARAMETER Reference
    Referenz in der Form "(Satznummer,Datenbanknummer,Tabellenzeile)".
    Mehrere Referenzen sind erlaubt.

.PARAMETER Client
    Erwarteter Mandantenname (EDP-Variable MANDANT, z. B. "DEMO"). Ist er
    angegeben, wird vorab geprueft, ob die GUI zu diesem Mandanten gehoert.

.EXAMPLE
    .\Open-AbasRecord.ps1 -DdeName 999 -Reference '(2000068,4,0)'

.EXAMPLE
    .\Open-AbasRecord.ps1 -DdeName 999 -Client DEMO -Reference '(2000021,4,0)','(2000022,4,0)'
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $DdeName,

    [Parameter(Mandatory = $true)]
    [string[]] $Reference,

    [string] $Client
)

if (-not ('Abas.GuiLink.AbasDde' -as [type])) {
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Abas.GuiLink {
public static class AbasDde {
    delegate IntPtr DdeCallback(uint t, uint f, IntPtr c, IntPtr a, IntPtr b,
                                IntPtr d, IntPtr e, IntPtr g);

    [DllImport("user32.dll", CharSet = CharSet.Ansi)]
    static extern int DdeInitializeA(ref uint pidInst, DdeCallback cb, uint afCmd, uint ulRes);
    [DllImport("user32.dll", CharSet = CharSet.Ansi)]
    static extern IntPtr DdeCreateStringHandleA(uint inst, string psz, int codePage);
    [DllImport("user32.dll")] static extern bool DdeFreeStringHandle(uint inst, IntPtr hsz);
    [DllImport("user32.dll")] static extern IntPtr DdeConnect(uint inst, IntPtr svc, IntPtr top, IntPtr cc);
    [DllImport("user32.dll")] static extern bool DdeDisconnect(IntPtr conv);
    [DllImport("user32.dll")]
    static extern IntPtr DdeClientTransaction(byte[] pData, uint cbData, IntPtr conv,
        IntPtr hszItem, uint wFmt, uint wType, uint timeout, out uint result);
    [DllImport("user32.dll")]
    static extern uint DdeGetData(IntPtr hData, byte[] dst, uint cbMax, uint off);
    [DllImport("user32.dll")] static extern bool DdeFreeDataHandle(IntPtr hData);
    [DllImport("user32.dll")] static extern int DdeGetLastError(uint inst);
    [DllImport("user32.dll")] static extern bool DdeUninitialize(uint inst);

    const int  CP_WINANSI        = 1004;
    const uint APPCMD_CLIENTONLY = 0x10;
    const uint XTYP_EXECUTE      = 0x4050;
    const uint XTYP_REQUEST      = 0x20B0;
    const uint CF_TEXT           = 1;

    static DdeCallback keep;
    static IntPtr Cb(uint t, uint f, IntPtr c, IntPtr a, IntPtr b,
                     IntPtr d, IntPtr e, IntPtr g) { return IntPtr.Zero; }

    static uint Init() {
        uint inst = 0;
        keep = new DdeCallback(Cb);
        DdeInitializeA(ref inst, keep, APPCMD_CLIENTONLY, 0);
        return inst;
    }

    /// <summary>Schickt einen Befehl an das Thema COMMAND.</summary>
    public static string Execute(string service, string command) {
        uint inst = Init();
        if (inst == 0) return "FEHLER: DdeInitialize";
        IntPtr hs = DdeCreateStringHandleA(inst, service,   CP_WINANSI);
        IntPtr ht = DdeCreateStringHandleA(inst, "COMMAND", CP_WINANSI);
        IntPtr cv = DdeConnect(inst, hs, ht, IntPtr.Zero);
        string res;
        if (cv == IntPtr.Zero) {
            res = "KEINE VERBINDUNG (0x" + DdeGetLastError(inst).ToString("X4") + ")";
        } else {
            byte[] data = Encoding.ASCII.GetBytes(command + "\0");
            uint ignored;
            IntPtr t = DdeClientTransaction(data, (uint)data.Length, cv, IntPtr.Zero, 0,
                                            XTYP_EXECUTE, 10000, out ignored);
            res = (t == IntPtr.Zero)
                ? "ABGELEHNT (0x" + DdeGetLastError(inst).ToString("X4") + ")"
                : "OK";
            DdeDisconnect(cv);
        }
        DdeFreeStringHandle(inst, hs); DdeFreeStringHandle(inst, ht);
        DdeUninitialize(inst);
        return res;
    }

    /// <summary>Liest das Element CLIENT vom Thema System (Lebendpruefung).</summary>
    public static string QueryClient(string service) {
        uint inst = Init();
        if (inst == 0) return null;
        IntPtr hs = DdeCreateStringHandleA(inst, service,  CP_WINANSI);
        IntPtr ht = DdeCreateStringHandleA(inst, "System", CP_WINANSI);
        IntPtr hi = DdeCreateStringHandleA(inst, "CLIENT", CP_WINANSI);
        IntPtr cv = DdeConnect(inst, hs, ht, IntPtr.Zero);
        string res = null;
        if (cv != IntPtr.Zero) {
            uint ignored;
            IntPtr h = DdeClientTransaction(null, 0xFFFFFFFF, cv, hi, CF_TEXT,
                                            XTYP_REQUEST, 10000, out ignored);
            if (h != IntPtr.Zero) {
                uint n = DdeGetData(h, null, 0, 0);
                byte[] buf = new byte[n];
                DdeGetData(h, buf, n, 0);
                DdeFreeDataHandle(h);
                res = Encoding.ASCII.GetString(buf).Trim('\0', '\r', '\n');
            }
            DdeDisconnect(cv);
        }
        DdeFreeStringHandle(inst, hs); DdeFreeStringHandle(inst, ht);
        DdeFreeStringHandle(inst, hi); DdeUninitialize(inst);
        return res;
    }
}}
'@
}

if ($PSBoundParameters.ContainsKey('Client')) {
    $gemeldet = [Abas.GuiLink.AbasDde]::QueryClient($DdeName)
    if ($null -eq $gemeldet) {
        Write-Error "Keine abas-GUI unter dem DDE-Namen '$DdeName' erreichbar. Laeuft die GUI?"
        return
    }
    if ($gemeldet -ne $Client) {
        Write-Error "Die GUI '$DdeName' gehoert zum Mandanten '$gemeldet', erwartet war '$Client'."
        return
    }
    Write-Verbose "GUI '$DdeName' bestaetigt Mandant '$gemeldet'."
}

foreach ($ref in $Reference) {
    $ergebnis = [Abas.GuiLink.AbasDde]::Execute($DdeName, $ref)
    [pscustomobject]@{
        Referenz = $ref
        Ergebnis = $ergebnis
    }
}
