using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DynamicData;
using NodeNetwork.ViewModels;
using NodeNetwork.Views;
using Newtonsoft.Json;

namespace ManualCanDebug
{
    internal sealed class StationTopologyCanvasPanel : Grid
    {
        private readonly InstrumentWorkspaceDocument _document;
        private readonly Action _save;
        private readonly NetworkViewModel _network = new NetworkViewModel();
        private readonly NetworkView _view;
        private readonly StationTopologyVisualCanvas _visual;
        private StationInstrumentDefinition _station;
        private TreeView _tree;
        private ComboBox _stationSelector;
        private readonly Stack<string> _undo = new Stack<string>();
        private readonly Stack<string> _redo = new Stack<string>();
        private bool _restoring;
        private string _searchText = string.Empty;

        public StationTopologyCanvasPanel(InstrumentWorkspaceDocument document, Action save)
        {
            _document = document; _save = save ?? delegate { }; _station = document.Stations.OrderBy(s => s.StationNumber).First();
            Background = new SolidColorBrush(Color.FromRgb(246, 248, 251));
            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); RowDefinitions.Add(new RowDefinition()); RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Children.Add(BuildToolbar());
            Grid body = new Grid { Margin = new Thickness(0, 8, 0, 8) }; body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(330) }); body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) }); body.ColumnDefinitions.Add(new ColumnDefinition());
            body.Children.Add(BuildTreeCard());
            _view = new NetworkView { ViewModel = _network, DataContext = _network, AllowDrop = true, Background = Brushes.White, NetworkBackground = new SolidColorBrush(Color.FromRgb(248, 250, 253)) };
            _view.Loaded += delegate { ApplyCanvasStyles(); _view.CenterAndZoomView(); };
            _view.Drop += CanvasDrop; _visual = new StationTopologyVisualCanvas(_document, _station) { AllowDrop = true }; _visual.Drop += CanvasDrop; _visual.NodeCommand += VisualNodeCommand; _visual.DbOffsetChanged += value => { Capture(); _station.PlcDbOffset = value; RefreshAll(); }; _visual.ConnectionDeleted += delegate { Capture(); _station.PlcInstrumentId = string.Empty; RefreshAll(); }; Border canvasCard = Card(); Grid canvasGrid = new Grid(); canvasGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); canvasGrid.RowDefinitions.Add(new RowDefinition()); canvasGrid.Children.Add(new TextBlock { Text = _station.StationName + " 拓扑画布", FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new Thickness(14, 10, 14, 8) }); Grid.SetRow(_visual, 1); canvasGrid.Children.Add(_visual); canvasCard.Child = canvasGrid; Grid.SetColumn(canvasCard, 2); body.Children.Add(canvasCard);
            Grid.SetRow(body, 1); Children.Add(body);
            Border info = new Border { Background = new SolidColorBrush(Color.FromRgb(235, 244, 255)), CornerRadius = new CornerRadius(4), Padding = new Thickness(12, 7, 12, 7), Child = new TextBlock { Text = "左侧树用于层级管理和拖入节点；双击节点或连线进行设置。", Foreground = new SolidColorBrush(Color.FromRgb(35, 103, 178)) } }; Grid.SetRow(info, 2); Children.Add(info);
            BuildNetwork();
        }

        private UIElement BuildToolbar()
        {
            DockPanel bar = new DockPanel(); StackPanel left = new StackPanel { Orientation = Orientation.Horizontal }; left.Children.Add(new TextBlock { Text = "当前工位", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) }); _stationSelector = new ComboBox { Width = 125, Height = 34 }; _stationSelector.SelectionChanged += delegate { StationInstrumentDefinition selected = _document.Stations.FirstOrDefault(v => v.StationName == Convert.ToString(_stationSelector.SelectedItem)); if (selected != null) SwitchStation(selected); }; RefreshStationSelector(); left.Children.Add(_stationSelector); left.Children.Add(Button("＋ 新增工位", AddStation)); left.Children.Add(Button("复制工位", CopyStation)); left.Children.Add(Button("删除工位", DeleteStation)); bar.Children.Add(left);
            StackPanel right = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right }; right.Children.Add(Button("撤销", Undo)); right.Children.Add(Button("重做", Redo)); right.Children.Add(Button("自动布局", AutoLayout)); right.Children.Add(Button("适应画布", delegate { if(_visual!=null)_visual.Fit(); })); right.Children.Add(Button("－", delegate { if(_visual!=null)_visual.Zoom(-.1); })); right.Children.Add(Button("100%", delegate { if(_visual!=null)_visual.Fit(); })); right.Children.Add(Button("＋", delegate { if(_visual!=null)_visual.Zoom(.1); })); right.Children.Add(Button("检查冲突", CheckConflicts)); Button save = Button("保存配置", delegate { _save(); }); save.Background = new SolidColorBrush(Color.FromRgb(28, 110, 230)); save.Foreground = Brushes.White; right.Children.Add(save); DockPanel.SetDock(right, Dock.Right); bar.Children.Add(right); return bar;
        }

        private UIElement BuildTreeCard() { Border card = Card(); Grid grid = new Grid(); grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); grid.RowDefinitions.Add(new RowDefinition()); grid.Children.Add(new TextBlock { Text = "工位配置树", FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new Thickness(14, 10, 14, 8) }); TextBox search=new TextBox{Height=32,Margin=new Thickness(12,0,12,6),Padding=new Thickness(8,4,8,4),ToolTip="搜索节点"};search.TextChanged+=delegate{_searchText=search.Text??string.Empty;RefreshTree();};Grid.SetRow(search,1);grid.Children.Add(search); _tree = new TreeView { BorderThickness = new Thickness(0), Margin = new Thickness(8), Background = Brushes.White }; Grid.SetRow(_tree, 2); grid.Children.Add(_tree); card.Child = grid; RefreshTree(); return card; }
        private void RefreshTree()
        {
            if (_tree == null) return; _tree.Items.Clear();
            foreach (StationInstrumentDefinition stationData in _document.Stations.OrderBy(v=>v.StationNumber))
            {
                bool current=ReferenceEquals(stationData,_station); TreeViewItem station = Item("▣  " + stationData.StationName, null, current); station.IsSelected=current; station.Selected+=delegate{SwitchStation(stationData);}; station.ContextMenu = Menu(("新增工位", AddStation), ("复制工位", delegate{CopyStationData(stationData);}), ("重命名", RenameStation), ("删除工位", delegate{DeleteStationData(stationData);}), ("展开/折叠", delegate { station.IsExpanded = !station.IsExpanded; }));
                TreeViewItem shared = Item("▦  共用资源", null, current); ProjectInstrumentDefinition plc = _document.Instruments.FirstOrDefault(i => i.Usage == "Shared" && i.Device == "PLC"); if (plc != null && !string.IsNullOrWhiteSpace(stationData.PlcInstrumentId)) shared.Items.Add(Item("▤  " + plc.DisplayName + "    DB偏移 " + stationData.PlcDbOffset, "SHARED:PLC", false)); station.Items.Add(shared);
                TreeViewItem independent = Item("▣  独立仪器", null, current); foreach (var group in stationData.IndependentDevices.Where(v=>string.IsNullOrWhiteSpace(_searchText)||v.TemplateDevice.IndexOf(_searchText,StringComparison.OrdinalIgnoreCase)>=0||v.InstanceName.IndexOf(_searchText,StringComparison.OrdinalIgnoreCase)>=0).GroupBy(v => Category(v.TemplateDevice))) { TreeViewItem category = Item("▰  " + group.Key, null, current); foreach (StationInstrumentInstance instance in group){TreeViewItem child=Item("  " + instance.TemplateDevice, instance.TemplateDevice, false);child.ContextMenu=InstrumentMenu(instance);category.Items.Add(child);} independent.Items.Add(category); } station.Items.Add(independent); _tree.Items.Add(station);
            }
            TreeViewItem available=Item("＋  可分配仪器",null,true);foreach(ProjectInstrumentDefinition template in _document.Instruments.Where(v=>v.Usage!="Shared"&&!_station.IndependentDevices.Any(i=>i.TemplateDevice==v.Device)))available.Items.Add(Item("  "+template.DisplayName,template.Device,false));_tree.Items.Add(available);
        }
        private TreeViewItem Item(string text, string payload, bool expanded) { TreeViewItem item = new TreeViewItem { Header = text, IsExpanded = expanded, Padding = new Thickness(4), Tag = payload }; if (payload != null) { item.PreviewMouseMove += TreeDrag; item.ContextMenu = Menu(("定位节点", delegate { SelectNode(payload); }), ("重命名实例", delegate { }), ("启用/停用", delegate { }), ("复制", delegate { Duplicate(payload); }), ("从工位移除", delegate { Remove(payload); }), ("在仪器中心打开", delegate { })); } return item; }
        private void TreeDrag(object sender, MouseEventArgs e) { if (e.LeftButton == MouseButtonState.Pressed && sender is TreeViewItem item && item.Tag is string payload) DragDrop.DoDragDrop(item, payload, DragDropEffects.Copy); }

        private void BuildNetwork()
        {
            _network.Connections.Clear(); _network.Nodes.Clear(); NodeViewModel stationNode = Node(_station.StationName, new Point(520, 120)); NodeInputViewModel stationInput = new NodeInputViewModel { Name = "PLC IN" }; stationNode.Inputs.Add(stationInput); _network.Nodes.Add(stationNode);
            ProjectInstrumentDefinition plcDef = _document.Instruments.FirstOrDefault(i => i.Usage == "Shared" && i.Device == "PLC"); if (plcDef != null) { NodeViewModel plc = Node(plcDef.DisplayName, new Point(120, 130)); NodeOutputViewModel output = new NodeOutputViewModel { Name = "PLC OUT" }; plc.Outputs.Add(output); _network.Nodes.Add(plc); _network.Connections.Add(_network.ConnectionFactory(stationInput, output)); }
            int index = 0; foreach (StationInstrumentInstance instance in _station.IndependentDevices) { int col = index % 4, row = index / 4; _network.Nodes.Add(Node(instance.InstanceName, new Point(520 + col * 180, 300 + row * 120))); index++; } if (_view != null && _view.IsLoaded) _view.CenterAndZoomView();
        }
        private static NodeViewModel Node(string name, Point position) { return new NodeViewModel { Name = name, Position = position }; }
        private void ApplyCanvasStyles()
        {
            Style baseNode = _view.TryFindResource(typeof(NodeView)) as Style; Style node = new Style(typeof(NodeView), baseNode); node.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.White)); node.Setters.Add(new Setter(Control.ForegroundProperty, new SolidColorBrush(Color.FromRgb(38, 52, 70)))); node.Setters.Add(new Setter(Control.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(31, 111, 232)))); node.Setters.Add(new Setter(NodeView.TitleFontSizeProperty, 14d)); node.Setters.Add(new Setter(NodeView.CornerRadiusProperty, new CornerRadius(6))); _view.Resources[typeof(NodeView)] = node;
            Style baseConnection = _view.TryFindResource(typeof(ConnectionView)) as Style; Style connection = new Style(typeof(ConnectionView), baseConnection); connection.Setters.Add(new Setter(ConnectionView.RegularBrushProperty, new SolidColorBrush(Color.FromRgb(31, 111, 232)))); connection.Setters.Add(new Setter(ConnectionView.HighlightBrushProperty, new SolidColorBrush(Color.FromRgb(0, 153, 255)))); _view.Resources[typeof(ConnectionView)] = connection;
        }
        private void CanvasDrop(object sender, DragEventArgs e) { string payload = e.Data.GetData(typeof(string)) as string; if (string.IsNullOrWhiteSpace(payload)) return; Capture(); if(payload=="SHARED:PLC"){_station.PlcInstrumentId=_document.Instruments.FirstOrDefault(v=>v.Device=="PLC")?.Id;RefreshAll();return;} if(_station.IndependentDevices.Any(v => v.TemplateDevice == payload))return; _station.IndependentDevices.Add(new StationInstrumentInstance { TemplateDevice = payload, InstanceName = payload + "-" + _station.StationNumber.ToString("00"), Resource = string.Empty }); RefreshAll(); }
        private void AutoLayout(object sender, RoutedEventArgs e) { BuildNetwork(); if(_visual!=null)_visual.AutoLayout(); }
        private void AddStation(object sender, RoutedEventArgs e) { Capture(); int number = _document.Stations.Count == 0 ? 1 : _document.Stations.Max(v => v.StationNumber) + 1; if (number > 12) return; _station = new StationInstrumentDefinition { StationNumber = number, PlcInstrumentId = _document.Instruments.FirstOrDefault(i => i.Device == "PLC")?.Id, PlcDbOffset = (number - 1) * 100 }; _document.Stations.Add(_station); _document.StationCount = _document.Stations.Count; RefreshAll(); }
        private void CopyStation(object sender, RoutedEventArgs e) { CopyStationData(_station); }
        private void CopyStationData(StationInstrumentDefinition source){Capture();int number=_document.Stations.Max(v=>v.StationNumber)+1;if(number>12)return;_station=new StationInstrumentDefinition{StationNumber=number,PlcInstrumentId=source.PlcInstrumentId,PlcDbOffset=(number-1)*100,PowerInstrumentId=source.PowerInstrumentId,PowerChannelGroup=source.PowerChannelGroup,IndependentDevices=source.IndependentDevices.Select(v=>new StationInstrumentInstance{TemplateDevice=v.TemplateDevice,InstanceName=v.TemplateDevice+"-"+number.ToString("00"),Resource=v.Resource}).ToList()};_document.Stations.Add(_station);_document.StationCount=_document.Stations.Count;RefreshAll();}
        private void DeleteStation(object sender, RoutedEventArgs e) { DeleteStationData(_station); }
        private void DeleteStationData(StationInstrumentDefinition target){if(_document.Stations.Count<=1)return;Capture();_document.Stations.Remove(target);_station=_document.Stations.OrderBy(v=>v.StationNumber).First();_document.StationCount=_document.Stations.Count;RefreshAll();}
        private void RenameStation(object sender, RoutedEventArgs e) { }
        private void Duplicate(string device) { StationInstrumentInstance source = _station.IndependentDevices.FirstOrDefault(v => v.TemplateDevice == device); if (source != null){Capture();_station.IndependentDevices.Add(new StationInstrumentInstance { TemplateDevice = device + "_COPY", InstanceName = source.InstanceName + "_COPY", Resource = source.Resource });RefreshAll();} }
        private void Remove(string device) { Capture();_station.IndependentDevices.RemoveAll(v => v.TemplateDevice == device);RefreshAll(); }
        private void SelectNode(string device) { foreach (NodeViewModel node in _network.Nodes.Items) node.IsSelected = node.Name.IndexOf(device, StringComparison.OrdinalIgnoreCase) >= 0; }
        private void VisualNodeCommand(StationInstrumentInstance item,string command){if(item==null)return;if(command=="remove"){Remove(item.TemplateDevice);return;}if(command=="duplicate"){Duplicate(item.TemplateDevice);return;}if(command=="disable"){Capture();item.InstanceName=item.InstanceName.StartsWith("[停用] ")?item.InstanceName.Substring(5):"[停用] "+item.InstanceName;RefreshAll();return;}if(command=="open"){MessageBox.Show("请切换到“仪器中心”页查看 "+item.TemplateDevice+" 的驱动和方法。","仪器中心");return;}if(command=="rename"){string value=Prompt("重命名实例",item.InstanceName);if(value!=null){Capture();item.InstanceName=value;RefreshAll();}}}
        private void SwitchStation(StationInstrumentDefinition station){if(station==null||ReferenceEquals(station,_station))return;_station=station;RefreshAll();}
        private void RefreshAll(){RefreshStationSelector();RefreshTree();BuildNetwork();if(_visual!=null)_visual.SetStation(_station);}
        private void RefreshStationSelector(){if(_stationSelector==null)return;_restoring=true;_stationSelector.ItemsSource=_document.Stations.OrderBy(v=>v.StationNumber).Select(v=>v.StationName).ToList();_stationSelector.SelectedItem=_station.StationName;_restoring=false;}
        private void Capture(){if(_restoring)return;_undo.Push(JsonConvert.SerializeObject(_document));_redo.Clear();}
        private void Undo(object sender,RoutedEventArgs e){if(_undo.Count==0)return;_redo.Push(JsonConvert.SerializeObject(_document));Restore(_undo.Pop());}
        private void Redo(object sender,RoutedEventArgs e){if(_redo.Count==0)return;_undo.Push(JsonConvert.SerializeObject(_document));Restore(_redo.Pop());}
        private void Restore(string json){_restoring=true;InstrumentWorkspaceDocument value=JsonConvert.DeserializeObject<InstrumentWorkspaceDocument>(json);int selected=_station.StationNumber;_document.Version=value.Version;_document.StationCount=value.StationCount;_document.Instruments=value.Instruments;_document.Stations=value.Stations;_station=_document.Stations.FirstOrDefault(v=>v.StationNumber==selected)??_document.Stations.First();_restoring=false;RefreshAll();}
        private void CheckConflicts(object sender,RoutedEventArgs e){List<string> conflicts=new List<string>();foreach(var duplicate in _document.Stations.GroupBy(v=>v.PlcDbOffset).Where(v=>v.Count()>1))conflicts.Add("PLC DB偏移 "+duplicate.Key+" 被多个工位使用");foreach(StationInstrumentDefinition station in _document.Stations)foreach(var duplicate in station.IndependentDevices.GroupBy(v=>v.TemplateDevice).Where(v=>v.Count()>1))conflicts.Add(station.StationName+" 重复仪器 "+duplicate.Key);MessageBox.Show(conflicts.Count==0?"未发现资源冲突。":string.Join(Environment.NewLine,conflicts),"检查冲突",MessageBoxButton.OK,conflicts.Count==0?MessageBoxImage.Information:MessageBoxImage.Warning);}
        private static string Prompt(string title,string initial){Window w=new Window{Title=title,Width=360,Height=150,WindowStartupLocation=WindowStartupLocation.CenterOwner,ResizeMode=ResizeMode.NoResize};StackPanel p=new StackPanel{Margin=new Thickness(16)};TextBox box=new TextBox{Text=initial,Height=30};p.Children.Add(box);Button ok=new Button{Content="确定",Width=75,Height=30,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,10,0,0)};ok.Click+=delegate{w.DialogResult=true;w.Close();};p.Children.Add(ok);w.Content=p;return w.ShowDialog()==true?box.Text:null;}
        private static string Category(string device) { string d = device ?? string.Empty; if (d.Contains("LVDC") || d.Contains("HVDC")) return "电源"; if (d.Contains("CAN")) return "通信"; if (d.Contains("DMM") || d.Contains("DAQ") || d == "RES") return "测量采集"; return "切换与负载"; }
        private static Border Card() { return new Border { Background = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromRgb(224, 230, 238)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6) }; }
        private static Button Button(string text, RoutedEventHandler click) { Button button = new Button { Content = text, Height = 34, MinWidth = 92, Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(10, 4, 10, 4), Background = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromRgb(215, 223, 233)) }; button.Click += click; return button; }
        private static ContextMenu Menu(params (string, RoutedEventHandler)[] actions) { ContextMenu menu = new ContextMenu(); foreach (var action in actions) { MenuItem item = new MenuItem { Header = action.Item1 }; item.Click += action.Item2; menu.Items.Add(item); } return menu; }
        private ContextMenu InstrumentMenu(StationInstrumentInstance instance){return Menu(("定位节点",delegate{SelectNode(instance.TemplateDevice);}), ("重命名实例",delegate{VisualNodeCommand(instance,"rename");}), ("启用/停用",delegate{VisualNodeCommand(instance,"disable");}), ("复制",delegate{VisualNodeCommand(instance,"duplicate");}), ("从工位移除",delegate{VisualNodeCommand(instance,"remove");}), ("在仪器中心打开",delegate{VisualNodeCommand(instance,"open");}));}
    }
}
