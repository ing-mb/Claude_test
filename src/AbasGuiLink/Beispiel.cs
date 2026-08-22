using System;

namespace Abas.GuiLink.Beispiele
{
    /// <summary>
    /// Zeigt, wie ein Datensatz aus einer eigenen Anwendung heraus in der
    /// laufenden abas-Sitzung geöffnet wird.
    /// </summary>
    public static class Beispiel
    {
        /// <summary>
        /// Einfachster Fall: Der DDE-Name ist bereits bekannt.
        /// </summary>
        public static void Minimal()
        {
            using (var gui = new AbasGuiLink("999"))
            {
                gui.OpenRecord("(2000068,4,0)");
            }
        }

        /// <summary>
        /// Empfohlener Weg: Den DDE-Namen zur Laufzeit über die bestehende
        /// EDP-Verbindung abfragen. Er ändert sich mit jedem Start der GUI.
        /// </summary>
        /// <param name="edp">
        /// Die eigene EDP-Sitzung. Wie die beiden Variablen gelesen werden,
        /// hängt vom verwendeten Wrapper ab — im EDP-Protokoll entsprechen sie
        /// den Kommandos "SHO|0|MANDANT|0" und "SHO|0|GUIDDESRVNAME|0".
        /// </param>
        public static void MitEdpVerbindung(IEdpVariablen edp)
        {
            string mandant = edp.GetVariable("MANDANT");           // z. B. "DEMO"
            string ddeName = edp.GetVariable("GUIDDESRVNAME");     // z. B. "999"

            using (var gui = new AbasGuiLink(ddeName))
            {
                if (!gui.IsAvailable(mandant))
                {
                    Console.WriteLine("Für den Mandanten " + mandant +
                                      " ist keine abas-GUI gestartet.");
                    return;
                }

                gui.OpenRecord(recordNumber: 2000068, databaseNumber: 4);
            }
        }

        /// <summary>
        /// Mehrere Datensätze nacheinander öffnen — die Verbindung wird
        /// dabei nur einmal aufgebaut.
        /// </summary>
        public static void MehrereDatensaetze(string ddeName, string[] referenzen)
        {
            using (var gui = new AbasGuiLink(ddeName))
            {
                foreach (string referenz in referenzen)
                {
                    try
                    {
                        gui.OpenRecord(referenz);
                    }
                    catch (AbasGuiException ex)
                    {
                        Console.WriteLine(referenz + " konnte nicht geöffnet werden: " + ex.Message);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Schmale Abstraktion über die vorhandene EDP-Anbindung, damit das
    /// Beispiel unabhängig vom konkreten ActiveX-Wrapper bleibt.
    /// </summary>
    public interface IEdpVariablen
    {
        string GetVariable(string name);
    }
}
