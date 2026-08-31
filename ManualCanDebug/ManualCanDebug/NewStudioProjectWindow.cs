using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ManualCanDebug.Core;

namespace ManualCanDebug
{
    internal sealed class NewStudioProjectWindow : Window
    {
        private readonly ComboBox _product = new ComboBox { Width = 300, Margin = new Thickness(4), Padding = new Thickness(7, 5, 7, 5), MinHeight = 32, DisplayMemberPath = "DisplayName" };
        private readonly TextBox _sequenceName = new TextBox { Width = 300, Margin = new Thickness(4), Padding = new Thickness(7, 5, 7, 5), MinHeight = 32 };
        private readonly List<ProductChoice> _choices = new List<ProductChoice>();
        private NewProductSetup _newProduct;
        private bool _changing;

        public NewStudioProjectWindow(ProductModel selected, IEnumerable<string> importedProducts = null)
        {
            Title = "新建FCT测试SEQ"; Width = 540; Height = 235; WindowStartupLocation = WindowStartupLocation.CenterOwner; ResizeMode = ResizeMode.NoResize; FontFamily = new FontFamily("Segoe UI"); FontSize = 12;
            foreach (ProductModel model in new[] { ProductModel.C91, ProductModel.C92, ProductModel.C95, ProductModel.C96 }) { ProductCanProfile profile = ProductCanProfile.For(model); _choices.Add(new ProductChoice(profile.Model.ToString(), profile.DisplayName, false)); }
            foreach (string product in importedProducts ?? Enumerable.Empty<string>()) if (!_choices.Any(value => value.Name.Equals(product, StringComparison.OrdinalIgnoreCase))) _choices.Add(new ProductChoice(product, product + "（已导入产品）", false));
            _choices.Add(new ProductChoice(string.Empty, "＋ 增加新产品...", true)); _product.ItemsSource = _choices; _product.SelectedItem = _choices.First(value => value.Name == selected.ToString()); _product.SelectionChanged += Product_SelectionChanged;
            _sequenceName.Text = selected + "_FCT_A0"; _sequenceName.ToolTip = "例如 C96_15KW_FCT_A0；程序自动添加.json";
            Grid root = new Grid { Margin = new Thickness(24) }; root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            WrapPanel row = new WrapPanel(); row.Children.Add(new TextBlock { Text = "产品型号", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 8, 12, 4) }); row.Children.Add(_product); root.Children.Add(row);
            WrapPanel nameRow = new WrapPanel(); nameRow.Children.Add(new TextBlock { Text = "SEQ名称", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 8, 12, 4) }); nameRow.Children.Add(_sequenceName); Grid.SetRow(nameRow, 1); root.Children.Add(nameRow);
            StackPanel buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) }; Button ok = new Button { Content = "创建JSON SEQ", MinWidth = 120, Margin = new Thickness(4), Padding = new Thickness(12, 6, 12, 6), Background = new SolidColorBrush(Color.FromRgb(24, 112, 224)), Foreground = Brushes.White }; ok.Click += Create_Click; Button cancel = new Button { Content = "取消", MinWidth = 80, Margin = new Thickness(4), Padding = new Thickness(12, 6, 12, 6) }; cancel.Click += (s, e) => DialogResult = false; buttons.Children.Add(ok); buttons.Children.Add(cancel); Grid.SetRow(buttons, 2); root.Children.Add(buttons); Content = root;
        }

        public string SelectedProductName { get { ProductChoice choice = _product.SelectedItem as ProductChoice; return choice == null ? string.Empty : choice.Name; } }
        public string SelectedSequenceName { get { return (_sequenceName.Text ?? string.Empty).Trim(); } }
        public NewProductSetup NewProduct { get { return _newProduct; } }

        private void Product_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_changing) return; ProductChoice choice = _product.SelectedItem as ProductChoice; if (choice == null || !choice.IsNew) return;
            NewProductSetupWindow dialog = new NewProductSetupWindow { Owner = this }; if (dialog.ShowDialog() == true) { _newProduct = dialog.Result; ProductChoice created = new ProductChoice(_newProduct.Product, _newProduct.Product + "（新产品）", false); _choices.Insert(_choices.Count - 1, created); _changing = true; _product.ItemsSource = null; _product.ItemsSource = _choices; _product.SelectedItem = created; _changing = false; } else { _changing = true; _product.SelectedIndex = 0; _changing = false; }
        }

        private void Create_Click(object sender, RoutedEventArgs e)
        {
            ProductChoice choice = _product.SelectedItem as ProductChoice; if (choice == null || choice.IsNew || string.IsNullOrWhiteSpace(choice.Name)) { MessageBox.Show(this, "请选择已有产品，或先完成新产品配置。", "新建SEQ", MessageBoxButton.OK, MessageBoxImage.Information); return; } if (string.IsNullOrWhiteSpace(SelectedSequenceName)) { MessageBox.Show(this, "请输入SEQ名称，例如 C96_15KW_FCT_A0。", "新建SEQ", MessageBoxButton.OK, MessageBoxImage.Information); return; } if (SelectedSequenceName.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0) { MessageBox.Show(this, "SEQ名称包含文件名不允许的字符。", "新建SEQ", MessageBoxButton.OK, MessageBoxImage.Information); return; } DialogResult = true;
        }
    }

    internal sealed class ProductChoice
    {
        public ProductChoice(string name, string displayName, bool isNew) { Name = name; DisplayName = displayName; IsNew = isNew; }
        public string Name { get; private set; } public string DisplayName { get; private set; } public bool IsNew { get; private set; }
    }
}
