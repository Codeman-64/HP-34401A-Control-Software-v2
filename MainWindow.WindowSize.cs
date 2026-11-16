using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace HP_34401A
{
    /// <summary>
    /// Display Panel -> Window Size: preset sizes, a custom size, and remembering the size between runs
    /// (stored with the session settings in %LocalAppData%\HP 34401A\Session.json).
    /// </summary>
    public partial class MainWindow
    {
        private void Size_800x800_Click(object sender, RoutedEventArgs e)
        {
            Set_Window_Size(800, 800);
        }

        private void Size_600x800_Click(object sender, RoutedEventArgs e)
        {
            Set_Window_Size(600, 800);
        }

        private void Size_Original_Click(object sender, RoutedEventArgs e)
        {
            Set_Window_Size(625, 670);
        }

        private void Size_Custom_Click(object sender, RoutedEventArgs e)
        {
            Rect area = SystemParameters.WorkArea;
            TextBox widthBox = new TextBox { Text = Math.Round(ActualWidth).ToString(CultureInfo.InvariantCulture), Width = 80, Margin = new Thickness(8, 0, 0, 0) };
            TextBox heightBox = new TextBox { Text = Math.Round(ActualHeight).ToString(CultureInfo.InvariantCulture), Width = 80, Margin = new Thickness(8, 0, 0, 0) };
            TextBlock message = new TextBlock { Foreground = System.Windows.Media.Brushes.Firebrick, Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap, MaxWidth = 260 };
            Button ok = new Button { Content = "OK", Width = 80, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
            Button cancel = new Button { Content = "Cancel", Width = 80, IsCancel = true };

            StackPanel widthRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
            widthRow.Children.Add(new TextBlock { Text = "Width (pixels):", Width = 100, VerticalAlignment = VerticalAlignment.Center });
            widthRow.Children.Add(widthBox);
            StackPanel heightRow = new StackPanel { Orientation = Orientation.Horizontal };
            heightRow.Children.Add(new TextBlock { Text = "Height (pixels):", Width = 100, VerticalAlignment = VerticalAlignment.Center });
            heightRow.Children.Add(heightBox);
            StackPanel buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);

            StackPanel content = new StackPanel { Margin = new Thickness(14) };
            content.Children.Add(new TextBlock
            {
                Text = "Minimum " + (int)MinWidth + " × " + (int)MinHeight + ", maximum " + (int)area.Width + " × " + (int)area.Height + " (your screen).",
                Foreground = System.Windows.Media.Brushes.Gray,
                Margin = new Thickness(0, 0, 0, 10)
            });
            content.Children.Add(widthRow);
            content.Children.Add(heightRow);
            content.Children.Add(message);
            content.Children.Add(buttons);

            Window dialog = new Window
            {
                Title = "Custom Window Size",
                Content = content,
                SizeToContent = SizeToContent.WidthAndHeight,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ShowInTaskbar = false,
                Owner = this
            };

            ok.Click += (a, b) =>
            {
                if (!int.TryParse(widthBox.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int w) ||
                    !int.TryParse(heightBox.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int h))
                {
                    message.Text = "Enter whole numbers of pixels.";
                    return;
                }
                if (w < MinWidth || h < MinHeight || w > area.Width || h > area.Height)
                {
                    message.Text = "The size must be between " + (int)MinWidth + " × " + (int)MinHeight + " and " + (int)area.Width + " × " + (int)area.Height + ".";
                    return;
                }
                dialog.DialogResult = true;
                Set_Window_Size(w, h);
            };
            dialog.ShowDialog();
            Update_Size_Menu_Checks();
        }

        //Resizes the window (keeping it on screen), remembers the size and ticks the matching menu item.
        private void Set_Window_Size(double width, double height)
        {
            Rect area = SystemParameters.WorkArea;
            width = Math.Max(MinWidth, Math.Min(width, area.Width));
            height = Math.Max(MinHeight, Math.Min(height, area.Height));
            if (WindowState != WindowState.Normal)
            {
                WindowState = WindowState.Normal;
            }
            Width = width;
            Height = height;
            if (IsLoaded)
            {
                if (Left + width > area.Right)
                {
                    Left = Math.Max(area.Left, area.Right - width);
                }
                if (Top + height > area.Bottom)
                {
                    Top = Math.Max(area.Top, area.Bottom - height);
                }
            }
            Session.WindowWidth = width;
            Session.WindowHeight = height;
            Save_Session();
            Update_Size_Menu_Checks();
            insert_Log("Window size set to " + (int)width + " × " + (int)height + ".", 3);
        }

        //Called at startup (constructor), before the window is shown.
        private void Apply_Saved_Window_Size()
        {
            Rect area = SystemParameters.WorkArea;
            if (Session.WindowWidth >= MinWidth && Session.WindowHeight >= MinHeight)
            {
                Width = Math.Min(Session.WindowWidth, area.Width);
                Height = Math.Min(Session.WindowHeight, area.Height);
            }
            SizeChanged += (a, b) => Update_Size_Menu_Checks();
            Update_Size_Menu_Checks();
        }

        //Called from Save_Session: keeps the size the user dragged the window to (not when maximised).
        private void Remember_Window_Size()
        {
            if (IsLoaded && WindowState == WindowState.Normal)
            {
                Session.WindowWidth = Math.Round(ActualWidth);
                Session.WindowHeight = Math.Round(ActualHeight);
            }
        }

        private void Update_Size_Menu_Checks()
        {
            double w = IsLoaded ? ActualWidth : Width;
            double h = IsLoaded ? ActualHeight : Height;
            bool Is(double a, double b) => Math.Abs(w - a) < 2 && Math.Abs(h - b) < 2;
            Size_800x800.IsChecked = Is(800, 800);
            Size_600x800.IsChecked = Is(600, 800);
            Size_Original.IsChecked = Is(625, 670);
            Size_Custom.IsChecked = !(Size_800x800.IsChecked || Size_600x800.IsChecked || Size_Original.IsChecked);
        }
    }
}
