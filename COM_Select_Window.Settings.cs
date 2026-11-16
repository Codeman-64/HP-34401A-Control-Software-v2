using System;
using System.IO;
using System.Text.Json;
using System.Windows.Controls;

namespace HP_34401A
{
    /// <summary>
    /// Saved serial port settings for the COM Select window.
    /// Stored in %LocalAppData%\HP 34401A\COM_Settings.json
    /// </summary>
    public class COM_Settings
    {
        public string PortName { get; set; } = "COM1";
        public string BaudRate { get; set; } = "9600";
        public string DataBits { get; set; } = "8";
        public string Parity { get; set; } = "None";
        public string StopBits { get; set; } = "2";
        public string FlowControl { get; set; } = "None";
        public string WriteTimeout { get; set; } = "3000";
        public string ReadTimeout { get; set; } = "9000";
        public string RtsEnable { get; set; } = "True";
        public string DtrEnable { get; set; } = "True";
        public string LogDirectory { get; set; } = "";
    }

    public partial class COM_Select_Window
    {
        private static readonly string COM_Settings_File = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "HP 34401A", "COM_Settings.json");

        private void Load_COM_Settings()
        {
            try
            {
                if (!File.Exists(COM_Settings_File))
                {
                    return;
                }

                COM_Settings s = JsonSerializer.Deserialize<COM_Settings>(File.ReadAllText(COM_Settings_File));
                if (s == null)
                {
                    return;
                }

                COM_Port.Text = s.PortName;

                //Older versions allowed frames the 34401A can't use (e.g. 8N1, 19200 baud, Mark parity).
                //Map them onto what the meter supports: 300-9600 baud, 8N2 / 7E2 / 7O2.
                bool migrated = false;
                if (!Select_ComboBox_Item(COM_Bits, s.BaudRate))
                {
                    Select_ComboBox_Item(COM_Bits, "9600");
                    migrated = true;
                }
                if (!Select_ComboBox_Item(COM_Parity, s.Parity))
                {
                    Select_ComboBox_Item(COM_Parity, "None");
                    migrated = true;
                }
                if (s.StopBits != "2")
                {
                    migrated = true;
                }
                if (migrated)
                {
                    insert_Log("Saved COM settings were updated to a frame the 34401A supports (2 stop bits; None = 8 data bits, Even/Odd = 7). Check they match the meter's I/O menu.", Warning_Code);
                }
                Select_ComboBox_Item(COM_Flow, s.FlowControl);
                COM_write_timeout.Text = s.WriteTimeout;
                COM_read_timeout.Text = s.ReadTimeout;
                Select_ComboBox_Item(COM_rtsEnable, s.RtsEnable);
                Select_ComboBox_Item(COM_dtrEnable, s.DtrEnable);

                if (!string.IsNullOrWhiteSpace(s.LogDirectory))
                {
                    folder_Directory = s.LogDirectory;
                    insert_Log("Saved log data directory: " + folder_Directory, Config_Code);
                }

                insert_Log("Loaded saved COM settings (" + s.PortName + ", " + s.BaudRate + " baud).", Config_Code);
            }
            catch (Exception Ex)
            {
                insert_Log("Could not load saved COM settings: " + Ex.Message, Warning_Code);
            }
        }

        private void Save_COM_Settings()
        {
            try
            {
                COM_Settings s = new COM_Settings
                {
                    PortName = COM_Port.Text.ToUpper().Trim(),
                    BaudRate = Get_ComboBox_Text(COM_Bits),
                    DataBits = COM_DataBits_Value.ToString(),
                    Parity = Get_ComboBox_Text(COM_Parity),
                    StopBits = "2",
                    FlowControl = Get_ComboBox_Text(COM_Flow),
                    WriteTimeout = COM_write_timeout.Text.Trim(),
                    ReadTimeout = COM_read_timeout.Text.Trim(),
                    RtsEnable = Get_ComboBox_Text(COM_rtsEnable),
                    DtrEnable = Get_ComboBox_Text(COM_dtrEnable),
                    LogDirectory = folder_Directory ?? ""
                };

                Directory.CreateDirectory(Path.GetDirectoryName(COM_Settings_File));
                File.WriteAllText(COM_Settings_File, JsonSerializer.Serialize(s, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception Ex)
            {
                insert_Log("Could not save COM settings: " + Ex.Message, Warning_Code);
            }
        }

        private static string Get_ComboBox_Text(ComboBox box)
        {
            return (box.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";
        }

        private static bool Select_ComboBox_Item(ComboBox box, string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }
            foreach (object item in box.Items)
            {
                if (item is ComboBoxItem cbi && cbi.Content?.ToString() == value)
                {
                    box.SelectedItem = cbi;
                    return true;
                }
            }
            return false;
        }
    }
}
