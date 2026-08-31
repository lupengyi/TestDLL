using System;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace ManualCanDebug
{
    internal sealed class ProductLocatorImportWindow : Window
    {
        private readonly TextBox _productBox;
        private readonly TextBox _pathBox;

        public ProductLocatorImportWindow()
        {
            Title = "导入新产品Locator";
            Width = 620;
            Height = 230;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Grid root = new Grid { Margin = new Thickness(16) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(105) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            TextBlock note = new TextBlock { Text = "导入后工具会直接解析XLSX，不需要手工转CSV。产品型号示例：C97。", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) };
            Grid.SetColumnSpan(note, 3); root.Children.Add(note);
            TextBlock productLabel = new TextBlock { Text = "产品型号：", VerticalAlignment = VerticalAlignment.Center };
            Grid.SetRow(productLabel, 1); root.Children.Add(productLabel);
            _productBox = new TextBox { Text = "C97", Margin = new Thickness(4), Padding = new Thickness(6, 4, 6, 4) };
            Grid.SetRow(_productBox, 1); Grid.SetColumn(_productBox, 1); root.Children.Add(_productBox);
            TextBlock pathLabel = new TextBlock { Text = "Locator文件：", VerticalAlignment = VerticalAlignment.Center };
            Grid.SetRow(pathLabel, 2); root.Children.Add(pathLabel);
            _pathBox = new TextBox { Margin = new Thickness(4), Padding = new Thickness(6, 4, 6, 4) };
            Grid.SetRow(_pathBox, 2); Grid.SetColumn(_pathBox, 1); root.Children.Add(_pathBox);
            Button browse = new Button { Content = "浏览...", Margin = new Thickness(4), Padding = new Thickness(10, 4, 10, 4) };
            browse.Click += Browse_Click;
            Grid.SetRow(browse, 2); Grid.SetColumn(browse, 2); root.Children.Add(browse);
            StackPanel buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
            Button ok = new Button { Content = "解析并导入", MinWidth = 105, Margin = new Thickness(4), Padding = new Thickness(10, 5, 10, 5) };
            ok.Click += Ok_Click;
            Button cancel = new Button { Content = "取消", MinWidth = 80, Margin = new Thickness(4), Padding = new Thickness(10, 5, 10, 5) };
            cancel.Click += (s, e) => DialogResult = false;
            buttons.Children.Add(ok); buttons.Children.Add(cancel);
            Grid.SetRow(buttons, 3); Grid.SetColumnSpan(buttons, 3); root.Children.Add(buttons);
            Content = root;
        }

        public string Product { get { return (_productBox.Text ?? string.Empty).Trim().ToUpperInvariant(); } }
        public string LocatorPath { get { return (_pathBox.Text ?? string.Empty).Trim(); } }

        private void Browse_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog dialog = new OpenFileDialog { Title = "选择产品Locator", Filter = "Excel Locator (*.xlsx)|*.xlsx" };
            if (dialog.ShowDialog(this) == true) _pathBox.Text = dialog.FileName;
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            if (Product.Length == 0 || LocatorPath.Length == 0)
            {
                MessageBox.Show(this, "请填写产品型号并选择Locator文件。", "导入Locator", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            DialogResult = true;
        }
    }
}
