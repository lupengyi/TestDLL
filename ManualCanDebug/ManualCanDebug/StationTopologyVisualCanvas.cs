using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.IO;
using Newtonsoft.Json;

namespace ManualCanDebug
{
    internal sealed class StationTopologyVisualCanvas : Grid
    {
        public event Action<StationInstrumentInstance, string> NodeCommand;
        public event Action<int> DbOffsetChanged;
        public event Action ConnectionDeleted;
        private readonly InstrumentWorkspaceDocument _document;
        private StationInstrumentDefinition _station;
        private readonly Canvas _canvas = new Canvas();
        private readonly ScaleTransform _scale = new ScaleTransform(1, 1);
        private Border _plcNode, _stationNode, _connectionPopup;
        private System.Windows.Shapes.Path _connection;
        private Point _dragStart, _nodeStart;
        private FrameworkElement _dragging;
        private readonly string _layoutPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Config", "InstrumentTopologyLayout.json");
        private TopologyLayoutDocument _layout;
        public StationTopologyVisualCanvas(InstrumentWorkspaceDocument document, StationInstrumentDefinition station)
        {
            _document = document; _station = station; _layout = LoadLayout(); ClipToBounds = true; Background = Brushes.White;
            DrawingGroup dots = new DrawingGroup(); dots.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(248,250,253)), null, new RectangleGeometry(new Rect(0,0,18,18)))); dots.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(222,229,238)), null, new EllipseGeometry(new Point(2,2),.7,.7))); _canvas.Background = new DrawingBrush(dots) { TileMode=TileMode.Tile, Viewport=new Rect(0,0,18,18), ViewportUnits=BrushMappingMode.Absolute };
            _canvas.RenderTransform = _scale; _canvas.RenderTransformOrigin = new Point(.5,.5); Children.Add(_canvas); PreviewMouseWheel += ZoomWheel; Build();
        }
        public void SetStation(StationInstrumentDefinition station) { _station = station; Build(); }
        public void Fit() { _scale.ScaleX = _scale.ScaleY = 1; SaveLayout(); }
        public void Zoom(double delta) { double value=Math.Max(.5,Math.Min(1.8,_scale.ScaleX+delta)); _scale.ScaleX=_scale.ScaleY=value; SaveLayout(); }
        public void AutoLayout() { Build(); }
        private void Build()
        {
            _canvas.Children.Clear();
            TopologyLayoutEntry entry=Entry();_scale.ScaleX=_scale.ScaleY=entry.Zoom<=0?1:entry.Zoom;_plcNode = SharedNode(); Canvas.SetLeft(_plcNode,entry.PlcX); Canvas.SetTop(_plcNode,entry.PlcY); _canvas.Children.Add(_plcNode);
            _stationNode = StationContainer(); Canvas.SetLeft(_stationNode,entry.StationX); Canvas.SetTop(_stationNode,entry.StationY); _canvas.Children.Add(_stationNode);
            _connection = new System.Windows.Shapes.Path { Stroke=new SolidColorBrush(Color.FromRgb(30,112,232)), StrokeThickness=2.2, Cursor=Cursors.Hand }; _connection.MouseLeftButtonDown += delegate { ShowConnectionPopup(); }; Panel.SetZIndex(_connection,1); _canvas.Children.Insert(0,_connection);
            Border label = new Border { Background=new SolidColorBrush(Color.FromRgb(230,242,255)), CornerRadius=new CornerRadius(10), Padding=new Thickness(8,3,8,3), Child=new TextBlock { Text="DB偏移 "+_station.PlcDbOffset, Foreground=new SolidColorBrush(Color.FromRgb(25,105,210)), FontSize=11 } }; Canvas.SetLeft(label,330); Canvas.SetTop(label,142); _canvas.Children.Add(label);
            Border drop = new Border { Width=165, Height=190, BorderBrush=new SolidColorBrush(Color.FromRgb(190,201,215)), BorderThickness=new Thickness(1), CornerRadius=new CornerRadius(8), Background=new SolidColorBrush(Color.FromArgb(25,220,228,238)), Child=new TextBlock { Text="＋\n拖入共用资源", HorizontalAlignment=HorizontalAlignment.Center, VerticalAlignment=VerticalAlignment.Center, Foreground=new SolidColorBrush(Color.FromRgb(112,126,145)), TextAlignment=TextAlignment.Center } }; Canvas.SetLeft(drop,65); Canvas.SetTop(drop,285); _canvas.Children.Add(drop);
            Border mini = new Border { Width=150, Height=105, BorderBrush=new SolidColorBrush(Color.FromRgb(190,205,225)), BorderThickness=new Thickness(1), CornerRadius=new CornerRadius(5), Background=new SolidColorBrush(Color.FromArgb(220,250,252,255)), Child=new TextBlock { Text="▭  ▫▫▫\n    ▫▫▫\n       ▫▫", Foreground=new SolidColorBrush(Color.FromRgb(79,137,218)), FontSize=18, TextAlignment=TextAlignment.Center, VerticalAlignment=VerticalAlignment.Center } }; mini.HorizontalAlignment=HorizontalAlignment.Right; mini.VerticalAlignment=VerticalAlignment.Bottom; mini.Margin=new Thickness(0,0,18,18); Children.Add(mini);
            SizeChanged += delegate { UpdateConnection(); }; Dispatcher.BeginInvoke(new Action(UpdateConnection), System.Windows.Threading.DispatcherPriority.Loaded);
        }
        private Border SharedNode()
        {
            Grid g=new Grid(); g.RowDefinitions.Add(new RowDefinition{Height=new GridLength(42)}); g.RowDefinitions.Add(new RowDefinition());
            g.Children.Add(new TextBlock{Text="▤  PLC-01       ⋯",FontWeight=FontWeights.SemiBold,FontSize=14,Margin=new Thickness(12),Foreground=Ink()}); StackPanel ports=new StackPanel{Margin=new Thickness(14,5,10,10)}; ports.Children.Add(PortRow("PLC OUT")); ports.Children.Add(PortRow("Status OUT")); Grid.SetRow(ports,1);g.Children.Add(ports);
            Border b=Card(g,180,125); EnableDrag(b); return b;
        }
        private UIElement PortRow(string name) { Grid r=new Grid{Margin=new Thickness(0,4,0,4)}; r.ColumnDefinitions.Add(new ColumnDefinition());r.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(20)});r.Children.Add(new TextBlock{Text=name,Foreground=Ink()});Ellipse e=new Ellipse{Width=12,Height=12,Fill=Brushes.White,Stroke=new SolidColorBrush(Color.FromRgb(34,168,84)),StrokeThickness=2};Grid.SetColumn(e,1);r.Children.Add(e);return r; }
        private Border StationContainer()
        {
            Grid root=new Grid{Margin=new Thickness(14)}; root.RowDefinitions.Add(new RowDefinition{Height=new GridLength(38)});root.RowDefinitions.Add(new RowDefinition());
            root.Children.Add(new TextBlock{Text="⌄   "+_station.StationName+"                                      ⋯",FontWeight=FontWeights.SemiBold,FontSize=16,Foreground=new SolidColorBrush(Color.FromRgb(22,103,220))});
            Grid categories=new Grid();categories.ColumnDefinitions.Add(new ColumnDefinition());categories.ColumnDefinitions.Add(new ColumnDefinition());categories.RowDefinitions.Add(new RowDefinition());categories.RowDefinitions.Add(new RowDefinition());categories.RowDefinitions.Add(new RowDefinition());
            AddCategory(categories,"电源",Devices("电源"),0,0);AddCategory(categories,"通信",Devices("通信"),0,1);AddCategory(categories,"测量采集",Devices("测量采集"),1,0,2);AddCategory(categories,"切换与负载",Devices("切换与负载"),2,0,2);Grid.SetRow(categories,1);root.Children.Add(categories);
            Border b=new Border{Width=720,Height=560,Background=new SolidColorBrush(Color.FromRgb(247,251,255)),BorderBrush=new SolidColorBrush(Color.FromRgb(71,139,234)),BorderThickness=new Thickness(1.5),CornerRadius=new CornerRadius(7),Child=root}; EnableDrag(b); return b;
        }
        private void AddCategory(Grid host,string title,IEnumerable<StationInstrumentInstance> items,int row,int col,int span=1)
        {
            Border box=new Border{Margin=new Thickness(5),Padding=new Thickness(10),Background=new SolidColorBrush(Color.FromRgb(252,253,255)),BorderBrush=new SolidColorBrush(Color.FromRgb(218,227,238)),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(5)};StackPanel s=new StackPanel();s.Children.Add(new TextBlock{Text=title+"  ("+items.Count()+")",FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,8)});WrapPanel wrap=new WrapPanel();foreach(var i in items)wrap.Children.Add(InstrumentCard(i));s.Children.Add(wrap);box.Child=s;Grid.SetRow(box,row);Grid.SetColumn(box,col);Grid.SetColumnSpan(box,span);host.Children.Add(box);
        }
        private Border InstrumentCard(StationInstrumentInstance item) { StackPanel s=new StackPanel();s.Children.Add(new TextBlock{Text="▣  "+item.TemplateDevice+"        ⋯",FontWeight=FontWeights.SemiBold});s.Children.Add(new TextBlock{Text=item.InstanceName,Foreground=new SolidColorBrush(Color.FromRgb(105,117,133)),FontSize=11,Margin=new Thickness(0,4,0,0)});Border b=new Border{Width=145,MinHeight=65,Margin=new Thickness(4),Padding=new Thickness(9),Background=Brushes.White,BorderBrush=item.TemplateDevice=="DMM"?new SolidColorBrush(Color.FromRgb(25,111,232)):new SolidColorBrush(Color.FromRgb(210,220,232)),BorderThickness=new Thickness(item.TemplateDevice=="DMM"?2:1),CornerRadius=new CornerRadius(5),Child=s,ContextMenu=NodeMenu(item)};b.MouseLeftButtonDown+=delegate{foreach(Border other in FindVisualChildren<Border>(_stationNode).Where(v=>v.Tag is StationInstrumentInstance)){other.BorderBrush=new SolidColorBrush(Color.FromRgb(210,220,232));other.BorderThickness=new Thickness(1);}b.BorderBrush=new SolidColorBrush(Color.FromRgb(25,111,232));b.BorderThickness=new Thickness(2);};b.Tag=item;return b; }
        private ContextMenu NodeMenu(StationInstrumentInstance item){ContextMenu m=new ContextMenu();AddMenu(m,"重命名实例",()=>NodeCommand?.Invoke(item,"rename"));AddMenu(m,"禁用",()=>NodeCommand?.Invoke(item,"disable"));AddMenu(m,"复制",()=>NodeCommand?.Invoke(item,"duplicate"));AddMenu(m,"从工位移除",()=>NodeCommand?.Invoke(item,"remove"));AddMenu(m,"在仪器中心打开",()=>NodeCommand?.Invoke(item,"open"));return m;}
        private IEnumerable<StationInstrumentInstance> Devices(string category)=>_station.IndependentDevices.Where(x=>Category(x.TemplateDevice)==category);
        private static string Category(string d){if(d.Contains("LVDC")||d.Contains("HVDC"))return"电源";if(d.Contains("CAN"))return"通信";if(d=="DMM"||d=="DAQ"||d=="RES")return"测量采集";return"切换与负载";}
        private void ShowConnectionPopup(){if(_connectionPopup!=null)_canvas.Children.Remove(_connectionPopup);StackPanel s=new StackPanel{Margin=new Thickness(12)};s.Children.Add(new TextBlock{Text="DB偏移",FontWeight=FontWeights.SemiBold});TextBox offset=new TextBox{Text=_station.PlcDbOffset.ToString(),Margin=new Thickness(0,6,0,8)};s.Children.Add(offset);s.Children.Add(new ComboBox{ItemsSource=new[]{"自动排队","立即失败"},SelectedIndex=0,Margin=new Thickness(0,0,0,8)});StackPanel buttons=new StackPanel{Orientation=Orientation.Horizontal};Button save=new Button{Content="保存",Background=new SolidColorBrush(Color.FromRgb(25,111,232)),Foreground=Brushes.White,MinWidth=60};save.Click+=delegate{int value;if(int.TryParse(offset.Text,out value))DbOffsetChanged?.Invoke(value);_canvas.Children.Remove(_connectionPopup);_connectionPopup=null;};Button delete=new Button{Content="删除连接",Margin=new Thickness(6,0,0,0),MinWidth=70};delete.Click+=delegate{ConnectionDeleted?.Invoke();_canvas.Children.Remove(_connectionPopup);_connectionPopup=null;};buttons.Children.Add(save);buttons.Children.Add(delete);s.Children.Add(buttons);_connectionPopup=new Border{Width=190,Background=Brushes.White,BorderBrush=new SolidColorBrush(Color.FromRgb(205,215,228)),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(6),Child=s};Canvas.SetLeft(_connectionPopup,310);Canvas.SetTop(_connectionPopup,165);Panel.SetZIndex(_connectionPopup,10);_canvas.Children.Add(_connectionPopup);}
        private void UpdateConnection(){if(_connection==null)return;Point a=new Point(Canvas.GetLeft(_plcNode)+180,Canvas.GetTop(_plcNode)+70),b=new Point(Canvas.GetLeft(_stationNode),Canvas.GetTop(_stationNode)+82);_connection.Data=new PathGeometry(new[]{new PathFigure(a,new PathSegment[]{new BezierSegment(new Point(a.X+100,a.Y),new Point(b.X-100,b.Y),b,true)},false)});}
        private void EnableDrag(FrameworkElement e){e.MouseLeftButtonDown+=(s,a)=>{_dragging=e;_dragStart=a.GetPosition(_canvas);_nodeStart=new Point(Canvas.GetLeft(e),Canvas.GetTop(e));e.CaptureMouse();};e.MouseMove+=(s,a)=>{if(_dragging!=e||a.LeftButton!=MouseButtonState.Pressed)return;Point p=a.GetPosition(_canvas);Canvas.SetLeft(e,_nodeStart.X+p.X-_dragStart.X);Canvas.SetTop(e,_nodeStart.Y+p.Y-_dragStart.Y);UpdateConnection();};e.MouseLeftButtonUp+=(s,a)=>{_dragging=null;e.ReleaseMouseCapture();SaveLayout();};}
        private void ZoomWheel(object s,MouseWheelEventArgs e){Zoom(e.Delta>0?.1:-.1);e.Handled=true;}
        private static Border Card(UIElement c,double w,double h)=>new Border{Width=w,Height=h,Background=Brushes.White,BorderBrush=new SolidColorBrush(Color.FromRgb(180,194,212)),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(6),Child=c};
        private static Brush Ink()=>new SolidColorBrush(Color.FromRgb(38,51,68));
        private TopologyLayoutDocument LoadLayout(){try{if(File.Exists(_layoutPath))return JsonConvert.DeserializeObject<TopologyLayoutDocument>(File.ReadAllText(_layoutPath))??new TopologyLayoutDocument();}catch{}return new TopologyLayoutDocument();}
        private TopologyLayoutEntry Entry(){string key=_station.StationNumber.ToString();TopologyLayoutEntry value;if(!_layout.Stations.TryGetValue(key,out value)){value=new TopologyLayoutEntry();_layout.Stations[key]=value;}return value;}
        private void SaveLayout(){try{TopologyLayoutEntry e=Entry();if(_plcNode!=null){e.PlcX=Canvas.GetLeft(_plcNode);e.PlcY=Canvas.GetTop(_plcNode);}if(_stationNode!=null){e.StationX=Canvas.GetLeft(_stationNode);e.StationY=Canvas.GetTop(_stationNode);}e.Zoom=_scale.ScaleX;Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_layoutPath));File.WriteAllText(_layoutPath,JsonConvert.SerializeObject(_layout,Formatting.Indented));}catch{}}
        private sealed class TopologyLayoutDocument{public TopologyLayoutDocument(){Stations=new Dictionary<string,TopologyLayoutEntry>();}public Dictionary<string,TopologyLayoutEntry> Stations{get;set;}}
        private sealed class TopologyLayoutEntry{public TopologyLayoutEntry(){PlcX=70;PlcY=80;StationX=460;StationY=55;Zoom=1;}public double PlcX{get;set;}public double PlcY{get;set;}public double StationX{get;set;}public double StationY{get;set;}public double Zoom{get;set;}}
        private static void AddMenu(ContextMenu menu,string text,Action action){MenuItem item=new MenuItem{Header=text};item.Click+=delegate{action();};menu.Items.Add(item);}
        private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root) where T:DependencyObject{if(root==null)yield break;for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){DependencyObject child=VisualTreeHelper.GetChild(root,i);if(child is T match)yield return match;foreach(T nested in FindVisualChildren<T>(child))yield return nested;}}
    }
}
