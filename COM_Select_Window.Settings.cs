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
        public string StopBits { get; set; } = "1";
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
                Select_ComboBox_Item(COM_Bits, s.BaudRate);
                Select_ComboBox_Item(COM_DataBits, s.DataBits);
                Select_ComboBox_Item(COM_Parity, s.Parity);
                Select_ComboBox_Item(COM_Stop, s.StopBits);
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
                    DataBits = Get_ComboBox_Text(COM_DataBits),
                    Parity = Get_ComboBox_Text(COM_Parity),
                    StopBits = Get_ComboBox_Text(COM_Stop),
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

        private static void Select_ComboBox_Item(ComboBox box, string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return;
            }
            foreach (object item in box.Items)
            {
                if (item is ComboBoxItem cbi && cbi.Content?.ToString() == value)
                {
                    box.SelectedItem = cbi;
                    return;
                }
            }
        }
    }
}
