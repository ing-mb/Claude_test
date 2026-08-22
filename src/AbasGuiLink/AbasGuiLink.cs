using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Abas.GuiLink
{
    /// <summary>
    /// Fehler bei der Kommunikation mit der abas-GUI.
    /// </summary>
    public class AbasGuiException : Exception
    {
        public AbasGuiException(string message) : base(message) { }
        public AbasGuiException(string message, Exception inner) : base(message, inner) { }
    }

    /// <summary>
    /// Öffnet Datensätze in einer laufenden abas-ERP-Sitzung.
    ///
    /// Die abas-GUI (abasgui.exe) ist ein DDE-Server. Ihr Dienstname ist die
    /// Sitzungsnummer, die der abas-Server unter der EDP-Variablen
    /// GUIDDESRVNAME liefert. Ein Datensatz wird geöffnet, indem seine
    /// EDP-Referenz — etwa "(2000068,4,0)" — als DDE-Befehl an das Thema
    /// COMMAND geschickt wird.
    ///
    /// Details zum Protokoll: docs/abas-gui-dde-protokoll.md
    /// </summary>
    public sealed class AbasGuiLink : IDisposable
    {
        private const string TopicCommand = "COMMAND";
        private const string TopicSystem = "System";
        private const string ItemClient = "CLIENT";

        private const int CpWinAnsi = 1004;
        private const uint AppcmdClientonly = 0x00000010;
        private const uint XtypExecute = 0x4050;
        private const uint XtypRequest = 0x20B0;
        private const uint CfText = 1;
        private const uint DefaultTimeoutMs = 10000;

        private readonly DdeCallback _callback;
        private uint _instance;
        private bool _disposed;

        /// <summary>
        /// Name des DDE-Dienstes der GUI, also der Wert der EDP-Variablen
        /// GUIDDESRVNAME (z. B. "999").
        /// </summary>
        public string ServiceName { get; }

        /// <param name="ddeServiceName">
        /// Wert der EDP-Variablen GUIDDESRVNAME. Der Name wird pro
        /// GUI-Sitzung neu vergeben und darf deshalb nicht fest hinterlegt,
        /// sondern muss zur Laufzeit abgefragt werden.
        /// </param>
        public AbasGuiLink(string ddeServiceName)
        {
            if (string.IsNullOrWhiteSpace(ddeServiceName))
                throw new ArgumentException("DDE-Dienstname darf nicht leer sein.", nameof(ddeServiceName));

            ServiceName = ddeServiceName;

            _callback = OnDdeEvent;
            uint instance = 0;
            int rc = DdeInitializeA(ref instance, _callback, AppcmdClientonly, 0);
            if (rc != 0 || instance == 0)
                throw new AbasGuiException("DdeInitialize fehlgeschlagen (Code " + rc + ").");

            _instance = instance;
        }

        /// <summary>
        /// Baut eine abas-Referenz aus ihren Bestandteilen.
        /// </summary>
        /// <param name="recordNumber">Satznummer.</param>
        /// <param name="databaseNumber">Datenbanknummer (dnr).</param>
        /// <param name="tableRow">Tabellenzeile; 0 für den Kopfsatz.</param>
        public static string BuildReference(long recordNumber, int databaseNumber, int tableRow = 0)
        {
            return "(" + recordNumber.ToString(System.Globalization.CultureInfo.InvariantCulture)
                 + "," + databaseNumber.ToString(System.Globalization.CultureInfo.InvariantCulture)
                 + "," + tableRow.ToString(System.Globalization.CultureInfo.InvariantCulture) + ")";
        }

        /// <summary>
        /// Öffnet den Datensatz mit der angegebenen Referenz in der GUI.
        /// </summary>
        /// <param name="reference">
        /// EDP-Referenz in der Form "(Satznummer,Datenbanknummer,Tabellenzeile)".
        /// Die Zeichenkette kann unverändert aus der EDP-Schnittstelle
        /// übernommen werden.
        /// </param>
        public void OpenRecord(string reference)
        {
            if (string.IsNullOrWhiteSpace(reference))
                throw new ArgumentException("Referenz darf nicht leer sein.", nameof(reference));

            SendCommand(reference);
        }

        /// <summary>
        /// Öffnet den Datensatz in der GUI.
        /// </summary>
        public void OpenRecord(long recordNumber, int databaseNumber, int tableRow = 0)
        {
            OpenRecord(BuildReference(recordNumber, databaseNumber, tableRow));
        }

        /// <summary>
        /// Schickt einen beliebigen Befehl an das Thema COMMAND.
        /// </summary>
        public void SendCommand(string command)
        {
            EnsureNotDisposed();

            IntPtr conversation = Connect(TopicCommand);
            try
            {
                byte[] payload = Ansi.GetBytes(command + "\0");
                uint ignored;
                IntPtr result = DdeClientTransaction(payload, (uint)payload.Length, conversation,
                    IntPtr.Zero, 0, XtypExecute, DefaultTimeoutMs, out ignored);

                if (result == IntPtr.Zero)
                    throw new AbasGuiException("Die GUI hat den Befehl abgelehnt (" +
                        DescribeLastError() + "). Gesendet: " + command);
            }
            finally
            {
                DdeDisconnect(conversation);
            }
        }

        /// <summary>
        /// Fragt die GUI über das Thema System nach dem Element CLIENT. Die
        /// Antwort ist der Mandantenname, etwa "DEMO".
        /// </summary>
        public string QueryClient()
        {
            EnsureNotDisposed();

            IntPtr conversation = Connect(TopicSystem);
            IntPtr itemHandle = IntPtr.Zero;
            try
            {
                itemHandle = CreateStringHandle(ItemClient);

                uint ignored;
                IntPtr data = DdeClientTransaction(null, 0xFFFFFFFF, conversation, itemHandle,
                    CfText, XtypRequest, DefaultTimeoutMs, out ignored);

                if (data == IntPtr.Zero)
                    throw new AbasGuiException("Die GUI hat auf die Anfrage CLIENT nicht geantwortet (" +
                        DescribeLastError() + ").");

                try
                {
                    uint size = DdeGetData(data, null, 0, 0);
                    byte[] buffer = new byte[size];
                    DdeGetData(data, buffer, size, 0);
                    return Ansi.GetString(buffer).TrimEnd('\0', '\r', '\n');
                }
                finally
                {
                    DdeFreeDataHandle(data);
                }
            }
            finally
            {
                if (itemHandle != IntPtr.Zero) DdeFreeStringHandle(_instance, itemHandle);
                DdeDisconnect(conversation);
            }
        }

        /// <summary>
        /// Prüft, ob die GUI erreichbar ist und zum erwarteten Mandanten gehört.
        /// Entspricht der Lebendprüfung, die auch der EDPViewer vor jedem
        /// Öffnen durchführt.
        /// </summary>
        /// <param name="expectedClient">
        /// Erwarteter Mandantenname, wie ihn die EDP-Variable MANDANT liefert
        /// (z. B. "DEMO"). Bei null wird nur die Erreichbarkeit geprüft.
        /// </param>
        public bool IsAvailable(string expectedClient = null)
        {
            try
            {
                string client = QueryClient();
                if (expectedClient == null) return true;
                return string.Equals(client, expectedClient, StringComparison.OrdinalIgnoreCase);
            }
            catch (AbasGuiException)
            {
                return false;
            }
        }

        private IntPtr Connect(string topic)
        {
            IntPtr serviceHandle = IntPtr.Zero;
            IntPtr topicHandle = IntPtr.Zero;
            try
            {
                serviceHandle = CreateStringHandle(ServiceName);
                topicHandle = CreateStringHandle(topic);

                IntPtr conversation = DdeConnect(_instance, serviceHandle, topicHandle, IntPtr.Zero);
                if (conversation == IntPtr.Zero)
                    throw new AbasGuiException("Keine Verbindung zur abas-GUI '" + ServiceName +
                        "' (Thema " + topic + ", " + DescribeLastError() +
                        "). Läuft die GUI, und stimmt GUIDDESRVNAME noch?");

                return conversation;
            }
            finally
            {
                if (topicHandle != IntPtr.Zero) DdeFreeStringHandle(_instance, topicHandle);
                if (serviceHandle != IntPtr.Zero) DdeFreeStringHandle(_instance, serviceHandle);
            }
        }

        private IntPtr CreateStringHandle(string value)
        {
            IntPtr handle = DdeCreateStringHandleA(_instance, value, CpWinAnsi);
            if (handle == IntPtr.Zero)
                throw new AbasGuiException("DdeCreateStringHandle fehlgeschlagen für '" + value + "'.");
            return handle;
        }

        private string DescribeLastError()
        {
            return "DdeGetLastError 0x" + DdeGetLastError(_instance).ToString("X4");
        }

        private void EnsureNotDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(AbasGuiLink));
        }

        private static IntPtr OnDdeEvent(uint type, uint format, IntPtr conv, IntPtr hsz1,
            IntPtr hsz2, IntPtr data, IntPtr d1, IntPtr d2)
        {
            // Reiner Client: DDEML verlangt einen Rückruf, wir müssen aber
            // nichts davon behandeln.
            return IntPtr.Zero;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            if (_instance != 0)
            {
                DdeUninitialize(_instance);
                _instance = 0;
            }
            GC.SuppressFinalize(this);
        }

        ~AbasGuiLink()
        {
            Dispose();
        }

        // ------------------------------------------------------------------
        // Kodierung
        //
        // Die GUI erwartet ANSI. Wird UTF-16 geschickt, antwortet sie mit
        // "unbekanntes Kommando". Codepage 1252 steht unter .NET Framework
        // immer zur Verfügung; unter .NET (Core) nur, wenn der
        // CodePagesEncodingProvider registriert wurde. Referenzen bestehen
        // ohnehin nur aus Ziffern, Komma und Klammern, deshalb ist ASCII ein
        // unschädlicher Rückfall.
        // ------------------------------------------------------------------

        private static readonly Encoding Ansi = ResolveAnsiEncoding();

        private static Encoding ResolveAnsiEncoding()
        {
            try
            {
                return Encoding.GetEncoding(1252);
            }
            catch (NotSupportedException)
            {
                return Encoding.ASCII;
            }
            catch (ArgumentException)
            {
                return Encoding.ASCII;
            }
        }

        // ------------------------------------------------------------------
        // DDEML — bewusst die ANSI-Varianten, siehe Kommentar oben.
        // ------------------------------------------------------------------

        private delegate IntPtr DdeCallback(uint type, uint format, IntPtr conv, IntPtr hsz1,
            IntPtr hsz2, IntPtr data, IntPtr d1, IntPtr d2);

        [DllImport("user32.dll", CharSet = CharSet.Ansi)]
        private static extern int DdeInitializeA(ref uint pidInst, DdeCallback pfnCallback,
            uint afCmd, uint ulRes);

        [DllImport("user32.dll", CharSet = CharSet.Ansi)]
        private static extern IntPtr DdeCreateStringHandleA(uint idInst, string psz, int iCodePage);

        [DllImport("user32.dll")]
        private static extern bool DdeFreeStringHandle(uint idInst, IntPtr hsz);

        [DllImport("user32.dll")]
        private static extern IntPtr DdeConnect(uint idInst, IntPtr hszService, IntPtr hszTopic,
            IntPtr pCC);

        [DllImport("user32.dll")]
        private static extern bool DdeDisconnect(IntPtr hConv);

        [DllImport("user32.dll")]
        private static extern IntPtr DdeClientTransaction(byte[] pData, uint cbData, IntPtr hConv,
            IntPtr hszItem, uint wFmt, uint wType, uint dwTimeout, out uint pdwResult);

        [DllImport("user32.dll")]
        private static extern uint DdeGetData(IntPtr hData, byte[] pDst, uint cbMax, uint cbOff);

        [DllImport("user32.dll")]
        private static extern bool DdeFreeDataHandle(IntPtr hData);

        [DllImport("user32.dll")]
        private static extern int DdeGetLastError(uint idInst);

        [DllImport("user32.dll")]
        private static extern bool DdeUninitialize(uint idInst);
    }
}
