using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace HP_34401A
{
    /// <summary>
    /// Display Panel -> Display Font: Standard / VFD / VFD Bold.
    /// VFD mode shows the reading and MIN/MAX/AVG in a 14-segment font like the 34401A's vacuum fluorescent
    /// display: dim "unlit" segments behind each character, a soft glow, and units the way the meter spells
    /// them (OHM, kOHM, MOHM; u for micro). The font "HP34401A VFD" is a modified DSEG14 Classic
    /// (SIL Open Font License 1.1, see Fonts\OFL-LICENSE.txt).
    /// </summary>
    public partial class MainWindow
    {
        //VFD = thin (Light) segments like the meter's display; Regular and Bold are heavier alternatives.
        private const string Font_Standard = "Standard", Font_VFD = "VFD", Font_VFD_Regular = "VFD Regular", Font_VFD_Bold = "VFD Bold";
        private const string VFD_Colour = "#FF3DF5D0";      //blue-green of a vacuum fluorescent display
        private const string Default_Colour = "#FF00C0FF";  //the app's original display colour
        private const double Ghost_Opacity = 0.10;          //brightness of the unlit segments

        private static readonly FontFamily VFD_Font = new FontFamily(new Uri("pack://application:,,,/"), "./Fonts/#HP34401A VFD");

        private class Readout_Part
        {
            public Label Label;
            public TextBlock Ghost;
            public bool IsMain;
            public bool IsUnit;   //Type label: Ω -> OHM
            public bool IsScale;  //Scale label: μ -> u
            public FontFamily StandardFamily;
            public double StandardSize;
            public Thickness StandardMargin;
        }

        private readonly List<Readout_Part> Readout_Parts = new List<Readout_Part>();

        //The segment font has no space above its characters (the standard font has about a quarter of its
        //size), so in VFD mode the readouts are moved to sit where the standard text sat.
        private const double VFD_Main_Size = 64, VFD_Small_Size = 13;  //13 = same height as the MIN:/MAX:/AVG: capitals
        private const double VFD_Main_Top = 6;    //centres the 64 px reading in the 75 px top row
        private const double VFD_Small_Top = 6;   //puts the MIN/MAX/AVG values on the same baseline as their labels
        private Viewbox Main_Readout_Box;
        private Thickness Main_Box_Standard_Margin;
        private double Main_Box_Standard_Height;
        private string Display_Font = Font_Standard;
        private bool Translating_Units = false;

        //Called once from the constructor: puts a "ghost" text behind each readout label.
        private void Setup_VFD_Display()
        {
            Main_Readout_Box = (Viewbox)((StackPanel)Measurement_Value.Parent).Parent;
            Main_Box_Standard_Margin = Main_Readout_Box.Margin;
            Main_Box_Standard_Height = Main_Readout_Box.Height;
            Add_Readout(Measurement_Value, true, false, false);
            Add_Readout(Measurement_Scale, true, false, true);
            Add_Readout(Measurement_Type, true, true, false);
            Add_Readout(MIN_Value, false, false, false);
            Add_Readout(MIN_Scale, false, false, true);
            Add_Readout(MIN_Type, false, true, false);
            Add_Readout(MAX_Value, false, false, false);
            Add_Readout(MAX_Scale, false, false, true);
            Add_Readout(MAX_Type, false, true, false);
            Add_Readout(AVG_Value, false, false, false);
            Add_Readout(AVG_Scale, false, false, true);
            Add_Readout(AVG_Type, false, true, false);

            Set_Display_Font(string.IsNullOrEmpty(Session.DisplayFont) ? Font_Standard : Session.DisplayFont, false);
        }

        private void Add_Readout(Label label, bool isMain, bool isUnit, bool isScale)
        {
            //Wrap the label in a one-cell grid so the ghost text sits exactly behind it.
            Panel parent = (Panel)label.Parent;
            int index = parent.Children.IndexOf(label);
            parent.Children.RemoveAt(index);

            Grid cell = new Grid { Margin = label.Margin };
            label.Margin = new Thickness(0);
            TextBlock ghost = new TextBlock
            {
                Visibility = Visibility.Collapsed,
                IsHitTestVisible = false,
                Opacity = Ghost_Opacity,
                VerticalAlignment = VerticalAlignment.Top,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            cell.Children.Add(ghost);
            cell.Children.Add(label);
            parent.Children.Insert(index, cell);

            Readout_Part part = new Readout_Part
            {
                Label = label,
                Ghost = ghost,
                IsMain = isMain,
                IsUnit = isUnit,
                IsScale = isScale,
                StandardFamily = label.FontFamily,
                StandardSize = label.FontSize,
                StandardMargin = cell.Margin
            };
            Readout_Parts.Add(part);

            DependencyPropertyDescriptor.FromProperty(ContentControl.ContentProperty, typeof(Label))
                .AddValueChanged(label, (s, e) => Readout_Changed(part));
            DependencyPropertyDescriptor.FromProperty(Control.ForegroundProperty, typeof(Label))
                .AddValueChanged(label, (s, e) => Readout_Colour_Changed(part));
        }

        private bool VFD_On => Display_Font != Font_Standard;

        //Keeps the unit spelling and the ghost segments in step with what the label shows.
        private void Readout_Changed(Readout_Part part)
        {
            if (Translating_Units)
            {
                return;
            }
            string text = part.Label.Content?.ToString() ?? "";
            string shown = VFD_On ? To_VFD_Text(part, text) : To_Standard_Text(part, text);
            if (shown != text)
            {
                Translating_Units = true;
                part.Label.Content = shown;
                Translating_Units = false;
            }
            part.Ghost.Text = Ghost_Text(shown);
        }

        private static string To_VFD_Text(Readout_Part part, string text)
        {
            if (part.IsUnit)
            {
                return text.Replace("Ω", "OHM");
            }
            if (part.IsScale)
            {
                return text.Replace("μ", "u").Replace("µ", "u");
            }
            return text;
        }

        private static string To_Standard_Text(Readout_Part part, string text)
        {
            if (part.IsUnit && text == "OHM")
            {
                return "Ω";
            }
            if (part.IsScale && text == "u")
            {
                return "μ";
            }
            return text;
        }

        //All segments lit ('~' in the font) for every character position; spaces and decimal points kept.
        private static string Ghost_Text(string text)
        {
            char[] chars = text.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                if (chars[i] != '.' && chars[i] != ' ')
                {
                    chars[i] = '~';
                }
            }
            return new string(chars);
        }

        private void Readout_Colour_Changed(Readout_Part part)
        {
            part.Ghost.Foreground = part.Label.Foreground;
            if (part.Label.Effect is DropShadowEffect glow && part.Label.Foreground is SolidColorBrush brush)
            {
                glow.Color = brush.Color;
            }
        }

        private void Set_Display_Font(string font, bool save)
        {
            bool wasOn = VFD_On;
            Display_Font = font;
            FontWeight weight = font == Font_VFD_Bold ? FontWeights.Bold : font == Font_VFD_Regular ? FontWeights.Normal : FontWeights.Light;

            foreach (Readout_Part part in Readout_Parts)
            {
                if (VFD_On)
                {
                    part.Label.FontFamily = VFD_Font;
                    part.Label.FontWeight = weight;
                    part.Label.FontSize = part.IsMain ? VFD_Main_Size : VFD_Small_Size;
                    part.Ghost.FontFamily = VFD_Font;
                    part.Ghost.FontWeight = part.Label.FontWeight;
                    part.Ghost.FontSize = part.Label.FontSize;
                    part.Ghost.Foreground = part.Label.Foreground;
                    part.Ghost.Visibility = Visibility.Visible;
                    Color colour = (part.Label.Foreground as SolidColorBrush)?.Color ?? Colors.Cyan;
                    part.Label.Effect = new DropShadowEffect { ShadowDepth = 0, BlurRadius = part.IsMain ? 14 : 6, Opacity = 0.7, Color = colour };
                    Grid vfdCell = (Grid)part.Label.Parent;
                    if (!part.IsMain)
                    {
                        Thickness m = part.StandardMargin;
                        vfdCell.Margin = new Thickness(m.Left, m.Top + VFD_Small_Top, m.Right, m.Bottom);
                    }
                }
                else
                {
                    part.Label.FontFamily = part.StandardFamily;
                    part.Label.FontWeight = FontWeights.Normal;
                    part.Label.FontSize = part.StandardSize;
                    part.Ghost.Visibility = Visibility.Collapsed;
                    part.Label.Effect = null;
                    ((Grid)part.Label.Parent).Margin = part.StandardMargin;
                }
                Readout_Changed(part); //re-spell units and refresh the ghost
            }

            //Main reading: no negative top offset for the segment font; a box just as tall as the text.
            if (VFD_On)
            {
                Main_Readout_Box.Margin = new Thickness(Main_Box_Standard_Margin.Left, VFD_Main_Top, Main_Box_Standard_Margin.Right, 0);
                Main_Readout_Box.Height = VFD_Main_Size;
            }
            else
            {
                Main_Readout_Box.Margin = Main_Box_Standard_Margin;
                Main_Readout_Box.Height = Main_Box_Standard_Height;
            }

            //Switch to the VFD colour (and back) only when the display still has the default colour.
            if (VFD_On && !wasOn)
            {
                Swap_Display_Colour(Default_Colour, VFD_Colour);
            }
            else if (!VFD_On && wasOn)
            {
                Swap_Display_Colour(VFD_Colour, Default_Colour);
            }

            DisplayFont_Standard.IsChecked = font == Font_Standard;
            DisplayFont_VFD.IsChecked = font == Font_VFD;
            DisplayFont_VFD_Regular.IsChecked = font == Font_VFD_Regular;
            DisplayFont_VFD_Bold.IsChecked = font == Font_VFD_Bold;

            if (save)
            {
                Session.DisplayFont = font;
                Save_Session();
                insert_Log("Display font set to " + font + ".", 3);
            }
        }

        private void Swap_Display_Colour(string from, string to)
        {
            Color fromColour = (Color)ColorConverter.ConvertFromString(from);
            bool Is(Label l) => l.Foreground is SolidColorBrush b && b.Color == fromColour;
            int check = to == Default_Colour ? 1 : -1; //1 = "Blue" in the colour menus; -1 = none ticked
            if (Is(Measurement_Value))
            {
                Measurement_Color(to);
                Measurement_Color_Checker(check);
            }
            if (Is(MIN_Value))
            {
                MIN_Color(to);
                MIN_Color_Checker(check);
            }
            if (Is(MAX_Value))
            {
                MAX_Color(to);
                MAX_Color_Checker(check);
            }
            if (Is(AVG_Value))
            {
                AVG_Color(to);
                AVG_Color_Checker(check);
            }
        }

        private void DisplayFont_Standard_Click(object sender, RoutedEventArgs e)
        {
            Set_Display_Font(Font_Standard, true);
        }

        private void DisplayFont_VFD_Click(object sender, RoutedEventArgs e)
        {
            Set_Display_Font(Font_VFD, true);
        }

        private void DisplayFont_VFD_Regular_Click(object sender, RoutedEventArgs e)
        {
            Set_Display_Font(Font_VFD_Regular, true);
        }

        private void DisplayFont_VFD_Bold_Click(object sender, RoutedEventArgs e)
        {
            Set_Display_Font(Font_VFD_Bold, true);
        }
    }
}
