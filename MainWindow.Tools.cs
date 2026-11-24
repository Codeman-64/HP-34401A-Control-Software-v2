using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;

namespace HP_34401A
{
    //Math functions built into the 34401A (CALCulate subsystem)
    public enum Meter_Math { Off, Null, dB, dBm, Limit }

    /// <summary>
    /// Stage 3 additions: meter error-queue checks, built-in math, burst capture and saved session settings.
    /// </summary>
    public partial class MainWindow
    {
        //---------------------------------------------------------------------------------------
        // Error queue
        //---------------------------------------------------------------------------------------

        //Reads SYSTem:ERRor? until the queue is empty and logs every error. Runs on the data thread.
        private void Check_Meter_Errors()
        {
            for (int attempt = 0; attempt < 2; attempt++)
            {
                try
                {
                    Read_Error_Queue();
                    return;
                }
                catch (TimeoutException)
                {
                    insert_Log("No reply from the meter to SYSTem:ERRor? (timed out)." + (attempt == 0 ? " Trying again." : ""), 2);
                    Discard_Late_Reply();
                    //Try once more: errors left in the queue keep the meter's ERROR annunciator lit
                }
            }
        }

        private void Read_Error_Queue()
        {
            for (int i = 0; i < 20; i++)
            {
                Meter_Write("SYSTem:ERRor?");
                string reply = Meter_ReadLine().Trim();
                string codeText = reply.Split(',')[0].Trim();
                if (!int.TryParse(codeText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int code))
                {
                    //Not an error-queue reply: a leftover from an earlier query. Skip it and ask again.
                    insert_Log("Unexpected reply while reading meter errors: " + reply, 2);
                    continue;
                }
                if (code == 0)
                {
                    return;
                }
                insert_Log("Meter error " + reply, 1);
            }
        }

        //---------------------------------------------------------------------------------------
        // Serial traffic (optionally traced to the output log for diagnosing)
        //---------------------------------------------------------------------------------------

        volatile bool Trace_Serial = false;
        readonly Stopwatch Trace_Clock = Stopwatch.StartNew();

        //Duration of the last READ? (ms): roughly how long the meter is busy taking one reading.
        long Last_Read_Ms = 1000;

        //True once the meter's DTR (our DSR) has been seen asserted, i.e. the cable carries the handshake line.
        bool Dsr_Wired = false;

        //The meter's actual math state as last commanded on the data thread (Math_Mode is the window's setting).
        volatile bool Meter_Math_On = false;

        //The meter ignores serial input while it is taking a reading triggered by a setting change.
        //Wait about one reading time (plus margin), and for its ready line if the cable has it.
        private void Wait_Meter_Busy()
        {
            int ms = (int)Math.Min(10000, Math.Max(200, Last_Read_Ms + 150));
            //Watch the meter's ready line meanwhile, to learn whether the cable carries it.
            Stopwatch w = Stopwatch.StartNew();
            int checks = 0, low = 0;
            while (w.ElapsedMilliseconds < ms)
            {
                checks++;
                if (!HP34401A.DsrHolding)
                {
                    low++;
                }
                Thread.Sleep(1);
            }
            if (Trace_Serial)
            {
                insert_Log("   waited " + ms + " ms for the meter to finish its reading (DSR low in " + low + " of " + checks + " checks)", 4);
            }
        }

        private void Meter_Write(string command)
        {
            bool dsr = HP34401A.DsrHolding;
            if (dsr)
            {
                Dsr_Wired = true;
            }
            else if (Dsr_Wired)
            {
                //Meter signals busy on its DTR line: wait (max 10 s) until it is ready for input.
                Stopwatch w = Stopwatch.StartNew();
                while (!HP34401A.DsrHolding && w.ElapsedMilliseconds < 10000 && !isClosing)
                {
                    Thread.Sleep(1);
                }
                if (Trace_Serial)
                {
                    insert_Log("   meter busy (DSR low) for " + w.ElapsedMilliseconds + " ms", 4);
                }
            }
            if (Trace_Serial)
            {
                insert_Log(">> " + command + "   (t=" + Trace_Clock.ElapsedMilliseconds + " ms, DSR=" + (dsr ? 1 : 0) + ")", 4);
            }
            HP34401A.WriteLine(command);
        }

        private string Meter_ReadLine()
        {
            long t0 = Trace_Clock.ElapsedMilliseconds;
            try
            {
                string reply = HP34401A.ReadLine();
                if (Trace_Serial)
                {
                    insert_Log("<< " + reply.Trim() + "   (" + (Trace_Clock.ElapsedMilliseconds - t0) + " ms)", 4);
                }
                return reply;
            }
            catch (TimeoutException)
            {
                if (Trace_Serial)
                {
                    insert_Log("<< TIMEOUT after " + (Trace_Clock.ElapsedMilliseconds - t0) + " ms", 1);
                }
                throw;
            }
        }

        private void LogSerialTraffic_Click(object sender, RoutedEventArgs e)
        {
            Trace_Serial = LogSerialTraffic.IsChecked == true;
            insert_Log(Trace_Serial ? "Serial traffic logging ON (every command and reply is logged)." : "Serial traffic logging OFF.", 3);
        }

        //---------------------------------------------------------------------------------------
        // Built-in math (null, dB, dBm, limit test)
        //---------------------------------------------------------------------------------------

        volatile Meter_Math Math_Mode = Meter_Math.Off;
        double Limit_Low = 0;
        double Limit_High = 0;
        string Last_Limit_State = "";
        Brush Measurement_Type_Normal_Brush;
        Meter_Tools_Window Tools_Window;

        internal int Current_Measurement => Measurement_Selected;

        internal string Current_Unit => MeasurementUnit_String().Item1;

        internal bool Can_Send_Commands => Serial_COM_Info.isConnected && !isSamplingOnly && !isClosing;

        //Queues commands exactly like the front-panel buttons do.
        internal void Queue_Meter_Commands(IEnumerable<string> commands)
        {
            foreach (string c in commands)
            {
                SerialWriteQueue.Add(c);
            }
            lockControls();
            isUserSendCommand = true;
            Speedup_Interval();
        }

        //Called by the tools window after it has queued the math commands. UI thread.
        internal void Set_Math_Mode(Meter_Math mode, double low, double high)
        {
            if (Measurement_Type_Normal_Brush == null)
            {
                Measurement_Type_Normal_Brush = Measurement_Type.Foreground;
            }
            Math_Mode = mode;
            Limit_Low = low;
            Limit_High = high;
            Last_Limit_State = "";
            Measurement_Type.Foreground = Measurement_Type_Normal_Brush;
            Interlocked.Exchange(ref resetMinMaxAvg, 1);
            switch (mode)
            {
                case Meter_Math.Off:
                    insert_Log("Meter math turned off.", 3);
                    break;
                case Meter_Math.Null:
                    insert_Log("Meter math: NULL. Readings are relative to the null offset.", 3);
                    break;
                case Meter_Math.dB:
                    insert_Log("Meter math: dB. Readings are in dB relative to the reference.", 3);
                    break;
                case Meter_Math.dBm:
                    insert_Log("Meter math: dBm. Readings are in dBm into the reference resistance.", 3);
                    break;
                case Meter_Math.Limit:
                    insert_Log("Meter math: limit test " + low.ToString(CultureInfo.InvariantCulture) + " to " + high.ToString(CultureInfo.InvariantCulture) + " " + Current_Unit + ".", 3);
                    break;
            }
        }

        //A CONFigure command turns the meter's math off, so follow it here. Data thread.
        private void Math_Off_By_Function_Change()
        {
            if (Math_Mode == Meter_Math.Off)
            {
                return;
            }
            Math_Mode = Meter_Math.Off;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (Measurement_Type_Normal_Brush != null)
                {
                    Measurement_Type.Foreground = Measurement_Type_Normal_Brush;
                }
                insert_Log("Meter math turned off (measurement function changed).", 2);
                Tools_Window?.Math_Was_Reset();
            }));
        }

        //Measurement_Type_Select() rewrites the unit labels after every command; put the math units back. Data thread.
        private void Apply_Math_Labels()
        {
            if (Math_Mode != Meter_Math.dB && Math_Mode != Meter_Math.dBm)
            {
                return;
            }
            string unit = Math_Mode == Meter_Math.dB ? "dB" : "dBm";
            Dispatcher.Invoke(new Action(() =>
            {
                Measurement_Type.Content = unit;
                MIN_Type.Content = unit;
                MAX_Type.Content = unit;
                AVG_Type.Content = unit;
            }));
        }

        //Called for each reading on the UI thread. Returns true when the reading was displayed here.
        private bool Display_Math_Value(double value)
        {
            switch (Math_Mode)
            {
                case Meter_Math.dB:
                case Meter_Math.dBm:
                    //dB values must not get SI prefixes (0.5 dB is not "500 m")
                    Measurement_Value.Content = value.ToString("0.000", CultureInfo.InvariantCulture);
                    Measurement_Scale.Content = "";
                    return true;
                case Meter_Math.Limit:
                    string state = value < Limit_Low ? "LO" : value > Limit_High ? "HI" : "PASS";
                    if (state != Last_Limit_State)
                    {
                        if (state == "PASS")
                        {
                            Measurement_Type.Content = Current_Unit;
                            Measurement_Type.Foreground = Measurement_Type_Normal_Brush;
                            if (Last_Limit_State != "")
                            {
                                insert_Log("Limit test: back within limits.", 0);
                            }
                        }
                        else
                        {
                            Measurement_Type.Content = state;
                            Measurement_Type.Foreground = Brushes.Red;
                            insert_Log("Limit test FAIL (" + state + "): " + value.ToString(CultureInfo.InvariantCulture) + " " + Current_Unit, 1);
                        }
                        Last_Limit_State = state;
                    }
                    return false;
                default:
                    return false;
            }
        }

        private void MeterTools_Click(object sender, RoutedEventArgs e)
        {
            if (Tools_Window == null)
            {
                Tools_Window = new Meter_Tools_Window(this);
                Tools_Window.Owner = this;
                Tools_Window.Closed += (a, b) => Tools_Window = null;
                Tools_Window.Show();
            }
            else
            {
                Tools_Window.Activate();
            }
        }

        //---------------------------------------------------------------------------------------
        // Burst capture (readings stored in the meter, then fetched in one transfer)
        //---------------------------------------------------------------------------------------

        internal const int Burst_Max = 512; //size of the 34401A reading memory

        //Runs on the data thread from the command queue ("BURST n").
        private void Run_Burst(int count)
        {
            int oldTimeout = HP34401A.ReadTimeout;
            string oldTrigger = "IMM";
            string result;
            try
            {
                Meter_Write("TRIGger:SOURce?");
                oldTrigger = Meter_ReadLine().Trim();

                Meter_Write("TRIGger:SOURce IMMediate");
                Meter_Write("SAMPle:COUNt " + count);
                Check_Meter_Errors(); //also confirms both settings were processed

                //FETCh? waits until all readings are taken. Allow plenty of time for slow NPLC settings.
                HP34401A.ReadTimeout = 20 * 60 * 1000;
                DateTime start = DateTime.Now;
                Stopwatch sw = Stopwatch.StartNew();
                Meter_Write("INITiate");
                Meter_Write("FETCh?");

                //The first byte arrives when acquisition has finished: that gives the acquisition time.
                while (HP34401A.BytesToRead == 0 && sw.ElapsedMilliseconds < HP34401A.ReadTimeout && !isClosing)
                {
                    Thread.Sleep(2);
                }
                double acquireSeconds = sw.Elapsed.TotalSeconds;
                string all = Meter_ReadLine().Trim();
                double totalSeconds = sw.Elapsed.TotalSeconds;

                string[] readings = all.Split(',').Select(r => r.Trim()).Where(r => r.Length > 0).ToArray();
                double interval = readings.Length > 1 ? acquireSeconds / readings.Length : 0;

                StringBuilder csv = new StringBuilder();
                csv.AppendLine("Index,Elapsed (s),Timestamp,Reading,Unit");
                string unit = Math_Mode == Meter_Math.dB ? "dB" : Math_Mode == Meter_Math.dBm ? "dBm" : Current_Unit;
                List<double> values = new List<double>();
                for (int i = 0; i < readings.Length; i++)
                {
                    DateTime t = start.AddSeconds(i * interval);
                    measurements.Add(readings[i]);
                    Total_Samples++;
                    if (saveMeasurements == true || save_to_Table == true || save_to_Graph == true)
                    {
                        Process_Measurement_Data(readings[i], t);
                    }
                    csv.AppendLine((i + 1) + "," + (i * interval).ToString("0.000000", CultureInfo.InvariantCulture) + "," +
                                   t.ToString("yyyy-MM-dd HH:mm:ss.fff") + "," + readings[i] + "," + unit);
                    if (double.TryParse(readings[i], NumberStyles.Float, CultureInfo.InvariantCulture, out double v) && Math.Abs(v) < 9e37)
                    {
                        values.Add(v);
                    }
                }
                Process_Data.Start();

                string file = Save_Burst_File(csv.ToString());
                result = readings.Length + " readings in " + acquireSeconds.ToString("0.00", CultureInfo.InvariantCulture) + " s (" +
                         (acquireSeconds > 0 ? (readings.Length / acquireSeconds).ToString("0.0", CultureInfo.InvariantCulture) : "?") + " rdg/s), transfer " +
                         (totalSeconds - acquireSeconds).ToString("0.0", CultureInfo.InvariantCulture) + " s.";
                if (values.Count > 0)
                {
                    double mean = values.Average();
                    double sd = Math.Sqrt(values.Sum(x => (x - mean) * (x - mean)) / Math.Max(1, values.Count - 1));
                    result += "\nMin " + values.Min().ToString("G8", CultureInfo.InvariantCulture) +
                              "  Max " + values.Max().ToString("G8", CultureInfo.InvariantCulture) +
                              "  Mean " + mean.ToString("G8", CultureInfo.InvariantCulture) +
                              "  Std dev " + sd.ToString("G4", CultureInfo.InvariantCulture) + " " + unit;
                }
                if (file != null)
                {
                    result += "\nSaved to " + file;
                }
                insert_Log("Burst: " + result.Replace("\n", "  "), 0);
            }
            catch (Exception Ex)
            {
                result = "Burst failed: " + Ex.Message;
                insert_Log(result, 1);
                Discard_Late_Reply();
            }
            finally
            {
                HP34401A.ReadTimeout = oldTimeout;
                try
                {
                    Meter_Write("SAMPle:COUNt 1");
                    Meter_Write("TRIGger:SOURce " + oldTrigger);
                }
                catch (Exception)
                {
                }
            }
            Dispatcher.BeginInvoke(new Action(() => Tools_Window?.Burst_Finished(result)));
        }

        private string Save_Burst_File(string csv)
        {
            try
            {
                string folder = System.IO.Path.Combine(Serial_COM_Info.folder_Directory, "Burst");
                Directory.CreateDirectory(folder);
                string unit = MeasurementUnit_String().Item1.Replace("Ω", "Ohm");
                string path = System.IO.Path.Combine(folder, DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss") + "_" + Serial_COM_Info.COM_Port + "_" + unit + "_burst.csv");
                File.WriteAllText(path, csv);
                return path;
            }
            catch (Exception Ex)
            {
                insert_Log("Could not save burst file: " + Ex.Message, 1);
                return null;
            }
        }

        //---------------------------------------------------------------------------------------
        // Saved session settings
        //---------------------------------------------------------------------------------------

        private class Session_Settings
        {
            public string Function { get; set; } = "";
            public string Range { get; set; } = "";
            public List<string> Config { get; set; } = new List<string>(); //last button per config category, in click order
            public double UpdateSpeed { get; set; } = 1000;
            public double WindowWidth { get; set; } = 0;  //0 = use the default size
            public double WindowHeight { get; set; } = 0;
            public string DisplayFont { get; set; } = "Standard";
        }

        private static readonly string Session_File = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HP 34401A", "Session.json");

        static readonly string[] Function_Buttons = { "VDC_Button", "ADC_Button", "VAC_Button", "AAC_Button", "TwoOhms_Button",
                                                      "FourOhms_Button", "FREQ_Button", "PER_Button", "DIODE_Button", "CONT_Button" };

        //Config buttons worth restoring, grouped by category (prefix). Queries, display, trigger and calibration are left out.
        static readonly string[] Config_Categories = { "NPLC_", "Res_M", "AZ_O", "Impedance_O", "DET_BAND_", "FREQ_APER_", "PER_APER_" };

        Session_Settings Session = new Session_Settings();
        bool Restoring_Session = false;

        private void Setup_Session_Tracking()
        {
            try
            {
                if (File.Exists(Session_File))
                {
                    Session = JsonSerializer.Deserialize<Session_Settings>(File.ReadAllText(Session_File)) ?? new Session_Settings();
                }
            }
            catch (Exception)
            {
                Session = new Session_Settings();
            }
            Apply_Saved_Window_Size();
            //See every button click (also ones already handled) so settings can be remembered.
            AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler(Track_Session_Click), true);
        }

        private static string Config_Category(string name)
        {
            if (name.Contains("Query"))
            {
                return null;
            }
            if (name == "AZ_Once_Button")
            {
                return null;
            }
            return Config_Categories.FirstOrDefault(c => name.StartsWith(c, StringComparison.Ordinal));
        }

        private void Track_Session_Click(object sender, RoutedEventArgs e)
        {
            if (Restoring_Session || !(e.OriginalSource is Button b) || string.IsNullOrEmpty(b.Name))
            {
                return;
            }
            string name = b.Name;
            if (Function_Buttons.Contains(name))
            {
                //CONFigure resets range and config on the meter, so forget those too
                Session.Function = name;
                Session.Range = "";
                Session.Config.Clear();
            }
            else if (Range.IsAncestorOf(b) && !name.Contains("Query"))
            {
                Session.Range = name;
            }
            else
            {
                string category = Config_Category(name);
                if (category == null)
                {
                    return;
                }
                Session.Config.RemoveAll(n => n.StartsWith(category, StringComparison.Ordinal));
                Session.Config.Add(name);
            }
            Save_Session();
        }

        private void Save_Session()
        {
            try
            {
                Session.UpdateSpeed = UpdateSpeed;
                Remember_Window_Size();
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Session_File));
                File.WriteAllText(Session_File, JsonSerializer.Serialize(Session, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception)
            {
            }
        }

        //After connecting: press the remembered buttons again, in the same order. UI thread.
        private void Restore_Session()
        {
            if (RestoreSession.IsChecked != true || string.IsNullOrEmpty(Session.Function))
            {
                return;
            }
            List<string> order = new List<string> { Session.Function };
            if (!string.IsNullOrEmpty(Session.Range))
            {
                order.Add(Session.Range);
            }
            order.AddRange(Session.Config);

            Restoring_Session = true;
            try
            {
                foreach (string name in order)
                {
                    if (FindName(name) is Button b)
                    {
                        b.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                    }
                }
                if (Session.UpdateSpeed > 0 && Session.UpdateSpeed != UpdateSpeed)
                {
                    UpdateSpeed = Session.UpdateSpeed;
                    UpdateSpeed_Value.Text = (UpdateSpeed / 1000).ToString(CultureInfo.InvariantCulture);
                    UpdateSpeed_Selector(UpdateSpeed == 1000 ? 1 : UpdateSpeed == 10 ? 2 : 0);
                    isUpdateSpeed_Changed = true;
                }
                insert_Log("Restored last session settings (" + order.Count + " settings).", 0);
            }
            finally
            {
                Restoring_Session = false;
            }
        }
    }
}
