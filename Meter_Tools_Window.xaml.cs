using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace HP_34401A
{
    /// <summary>
    /// Meter math (null, dB, dBm, limit test) and burst capture. Commands go through the main window's command queue.
    /// </summary>
    public partial class Meter_Tools_Window : Window
    {
        private readonly MainWindow Main;

        public Meter_Tools_Window(MainWindow main)
        {
            InitializeComponent();
            Main = main;
        }

        private static string Num(double v)
        {
            return v.ToString("R", CultureInfo.InvariantCulture);
        }

        //Empty text = no value (returns true, value null). Invalid text = false.
        private static bool Parse_Optional(string text, out double? value)
        {
            value = null;
            text = text.Trim();
            if (text.Length == 0)
            {
                return true;
            }
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
            {
                value = v;
                return true;
            }
            return false;
        }

        private void Math_Apply_Click(object sender, RoutedEventArgs e)
        {
            Math_Message.Text = "";
            if (!Main.Can_Send_Commands)
            {
                Math_Message.Text = "Not connected, or Sampling Only mode is on.";
                return;
            }

            int function = Main.Current_Measurement; //0 DCV, 1 DCI, 2 ACV, 3 ACI, 4 2W, 5 4W, 6 FREQ, 7 PER, 8 DIODE, 9 CONT
            List<string> commands = new List<string>();
            Meter_Math mode;
            double low = 0, high = 0;
            string status;

            if (Math_Off.IsChecked == true)
            {
                mode = Meter_Math.Off;
                commands.Add("CALCulate:STATe OFF");
                status = "Math: Off";
            }
            else
            {
                if (function == 8 || function == 9)
                {
                    Math_Message.Text = "Math isn't available for diode and continuity.";
                    return;
                }

                if (Math_Null.IsChecked == true)
                {
                    if (!Parse_Optional(Null_Offset.Text, out double? offset))
                    {
                        Math_Message.Text = "Offset must be a number (use '.' as the decimal point).";
                        return;
                    }
                    mode = Meter_Math.Null;
                    commands.Add("CALCulate:FUNCtion NULL");
                    commands.Add("CALCulate:STATe ON");
                    if (offset.HasValue)
                    {
                        commands.Add("CALCulate:NULL:OFFSet " + Num(offset.Value));
                    }
                    status = "Math: Null" + (offset.HasValue ? " (offset " + Num(offset.Value) + " " + Main.Current_Unit + ")" : " (offset = first reading)");
                }
                else if (Math_dB.IsChecked == true || Math_dBm.IsChecked == true)
                {
                    if (function != 0 && function != 2)
                    {
                        Math_Message.Text = "dB and dBm only work with DC V or AC V.";
                        return;
                    }
                    if (Math_dB.IsChecked == true)
                    {
                        if (!Parse_Optional(DB_Ref.Text, out double? reference))
                        {
                            Math_Message.Text = "Reference must be a number in dBm.";
                            return;
                        }
                        if (reference.HasValue && (reference.Value < -200 || reference.Value > 200))
                        {
                            Math_Message.Text = "The dB reference must be between -200 and +200 dBm.";
                            return;
                        }
                        mode = Meter_Math.dB;
                        commands.Add("CALCulate:FUNCtion DB");
                        commands.Add("CALCulate:STATe ON");
                        if (reference.HasValue)
                        {
                            commands.Add("CALCulate:DB:REFerence " + Num(reference.Value));
                        }
                        status = "Math: dB" + (reference.HasValue ? " (ref " + Num(reference.Value) + " dBm)" : " (ref = first reading)");
                    }
                    else
                    {
                        string ohms = (DBM_Ref.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "600";
                        mode = Meter_Math.dBm;
                        commands.Add("CALCulate:FUNCtion DBM");
                        commands.Add("CALCulate:STATe ON");
                        commands.Add("CALCulate:DBM:REFerence " + ohms);
                        status = "Math: dBm (" + ohms + " Ω)";
                    }
                }
                else if (Math_Limit.IsChecked == true)
                {
                    if (!double.TryParse(Limit_Low.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out low) ||
                        !double.TryParse(Limit_High.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out high))
                    {
                        Math_Message.Text = "Enter both limits as numbers (e.g. 4.95 and 5.05, or 1e3).";
                        return;
                    }
                    if (low >= high)
                    {
                        Math_Message.Text = "The lower limit must be below the upper limit.";
                        return;
                    }
                    mode = Meter_Math.Limit;
                    commands.Add("CALCulate:FUNCtion LIMit");
                    commands.Add("CALCulate:STATe ON");
                    commands.Add("CALCulate:LIMit:LOWer " + Num(low));
                    commands.Add("CALCulate:LIMit:UPPer " + Num(high));
                    status = "Math: Limit " + Num(low) + " to " + Num(high) + " " + Main.Current_Unit;
                }
                else
                {
                    return;
                }
            }

            Main.Queue_Meter_Commands(commands);
            Main.Set_Math_Mode(mode, low, high);
            Math_Status.Text = status;
        }

        //Called by the main window when a function change turned the meter's math off.
        internal void Math_Was_Reset()
        {
            Math_Off.IsChecked = true;
            Math_Status.Text = "Math: Off (measurement function changed)";
        }

        private void Burst_Start_Click(object sender, RoutedEventArgs e)
        {
            if (!Main.Can_Send_Commands)
            {
                Burst_Result.Text = "Not connected, or Sampling Only mode is on.";
                return;
            }
            if (!int.TryParse(Burst_Count.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int count) ||
                count < 2 || count > MainWindow.Burst_Max)
            {
                Burst_Result.Text = "Enter a number of readings from 2 to " + MainWindow.Burst_Max + ".";
                return;
            }
            Burst_Start.IsEnabled = false;
            Burst_Result.Text = "Running burst of " + count + " readings...";
            Main.Queue_Meter_Commands(new[] { "BURST " + count.ToString(CultureInfo.InvariantCulture) });
        }

        //Called by the main window (UI thread) when the burst is done.
        internal void Burst_Finished(string result)
        {
            Burst_Start.IsEnabled = true;
            Burst_Result.Text = result;
        }
    }
}
