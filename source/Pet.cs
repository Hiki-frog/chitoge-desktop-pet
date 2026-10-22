using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace Chitoge {
public static class Program {
    public static readonly string Home=AppDomain.CurrentDomain.BaseDirectory;
    [STAThread] public static int Main(string[] args) {
        try {
            if(args.Contains("--self-test")) return SelfTest.Run(args);
            bool created; using(var mutex=new Mutex(true,"Local\\ChitogeRecyclePet-v2",out created)) {
                if(!created) { MessageBox.Show("桐崎千棘已经在运行了。\n可以从系统托盘找到她。","桐崎千棘"); return 0; }
                var app=new Application();
                app.DispatcherUnhandledException += (s,e) => { MessageBox.Show(e.Exception.Message,"桐崎千棘 · 出错了"); e.Handled=true; };
                app.Run(new PetWindow(args.Contains("--test-ui")));
            }
            return 0;
        } catch(Exception ex) { MessageBox.Show(ex.ToString(),"桐崎千棘 · 启动失败"); return 1; }
    }
}
public class PetWindow : Window {
    public readonly BitmapSource[][] Frames=new BitmapSource[11][];
    public readonly BitmapSource Reaction;
    readonly int[] counts={6,8,8,4,5,8,6,6,6,8,8};
    readonly string[] names={"待机","向右跑","向左跑","挥手","跳跃","委屈","等待","思考","检查"};
    Image sprite; TextBlock status,caption; Border bubble,badge;
    DispatcherTimer timer; Forms.NotifyIcon tray; Grid design;
    int row,frame,ticks; DateTime actionUntil=DateTime.MinValue, bubbleUntil=DateTime.MinValue;
    bool hovering,busy,moving,follow=true,menuOpen,paused,testing;
    Point anchor; double startLeft,startTop,scale=1.0;
    IntPtr hwnd;
    internal Func<string[],bool> ConfirmForTests;
    internal bool IsHoveringForTests {get{return hovering;}}
    public PetWindow(bool test) {
        testing=test;
        Title="桐崎千棘 · 回收站桌宠";
        WindowStyle=WindowStyle.None; ResizeMode=ResizeMode.NoResize;
        AllowsTransparency=true; Background=Brushes.Transparent; Topmost=true; ShowInTaskbar=true;
        FontFamily=new FontFamily("Microsoft YaHei UI"); FontSize=12;
        BitmapSource atlas=Load("original.png");
        if(atlas.PixelWidth!=1536 || atlas.PixelHeight!=2288) throw new IOException("原始动画素材尺寸不正确。");
        for(int r=0;r<11;r++) {
            Frames[r]=new BitmapSource[counts[r]];
            for(int c=0;c<counts[r];c++) { var crop=new CroppedBitmap(atlas,new Int32Rect(c*192,r*208,192,208)); crop.Freeze(); Frames[r][c]=crop; }
        }
        Reaction=Load("handkerchief.png");
        design=new Grid {Width=240,Height=302, Background=new SolidColorBrush(Color.FromArgb(1,255,255,255))};
        design.RowDefinitions.Add(new RowDefinition{Height=new GridLength(54)});
        design.RowDefinitions.Add(new RowDefinition{Height=new GridLength(208)});
        design.RowDefinitions.Add(new RowDefinition{Height=new GridLength(40)});
        status=new TextBlock {Foreground=Brush("#563E51"),TextWrapping=TextWrapping.Wrap,TextAlignment=TextAlignment.Center,FontSize=12};
        bubble=new Border {Background=Brush("#FFF5F8"),BorderBrush=Brush("#EDCBD6"),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(12),Padding=new Thickness(9,5,9,5),Margin=new Thickness(5,0,5,4),Child=status,Visibility=Visibility.Hidden,VerticalAlignment=VerticalAlignment.Bottom};
        design.Children.Add(bubble);
        sprite=new Image {Source=Frames[0][0],Width=192,Height=208,Stretch=Stretch.Uniform,Cursor=Cursors.Hand};
        RenderOptions.SetBitmapScalingMode(sprite,BitmapScalingMode.HighQuality); Grid.SetRow(sprite,1); design.Children.Add(sprite);
        caption=new TextBlock {Text="桐崎千棘  ·  拖文件给我",Foreground=Brush("#67465C"),FontSize=11,HorizontalAlignment=HorizontalAlignment.Center};
        badge=new Border {Child=caption,Background=Brush("#FDF0F5"),BorderBrush=Brush("#E5C9D5"),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(12),Padding=new Thickness(11,5,11,5),HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center,Cursor=Cursors.Hand};
        Grid.SetRow(badge,2); design.Children.Add(badge);
        Content=new Viewbox{Child=design,Stretch=Stretch.Uniform};
        AllowDrop=true;
        PreviewDragEnter+=DragOverPet; PreviewDragOver+=DragOverPet;
        PreviewDragLeave+=(s,e)=> { if(!busy) ResetHover(); e.Handled=true; };
        PreviewDrop+=DropOnPet;
        design.MouseLeftButtonDown+=(s,e)=> {
            if(busy) return;
            if(e.ClickCount==2) { Play(3,1800); Say("哼，才不是特意陪你的。",2400); return; }
            anchor=PointToScreen(e.GetPosition(this)); startLeft=Left; startTop=Top; moving=true; design.CaptureMouse(); e.Handled=true;
        };
        design.MouseMove+=(s,e)=> {
            if(!moving || e.LeftButton!=MouseButtonState.Pressed) return;
            Point p=PointToScreen(e.GetPosition(this));
            var source=PresentationSource.FromVisual(this);
            Vector v=p-anchor; if(source!=null) v=source.CompositionTarget.TransformFromDevice.Transform(v);
            Left=startLeft+v.X; Top=startTop+v.Y;
            if(Math.Abs(v.X)>3) row=v.X>0?1:2;
        };
        design.MouseLeftButtonUp+=(s,e)=> { if(moving) {moving=false; design.ReleaseMouseCapture(); Clamp(); Play(0,0); Save();} };
        design.LostMouseCapture+=(s,e)=>moving=false;
        ContextMenu=BuildMenu();
        ContextMenu.Opened+=(s,e)=>menuOpen=true; ContextMenu.Closed+=(s,e)=>menuOpen=false;
        timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(120)}; timer.Tick+=Tick;
        SourceInitialized+=(s,e)=> {hwnd=new WindowInteropHelper(this).Handle; var source=HwndSource.FromHwnd(hwnd); source.AddHook(WindowHook); RegisterHotKey(hwnd,1,0x0002|0x0001,0x50);};
        Loaded+=(s,e)=> {LoadSettings(); timer.Start(); Say("拖入文件变脸，松开后确认回收\n右键：动作、大小和回收站",5500);};
        Closing+=(s,e)=> {if(busy){e.Cancel=true;Say("正在处理文件，请稍等。",2000);return;} Save();};
        Closed+=(s,e)=> {timer.Stop(); UnregisterHotKey(hwnd,1); if(tray!=null) tray.Dispose();};
        StateChanged+=(s,e)=> {if(WindowState==WindowState.Minimized) WindowState=WindowState.Normal;};
        MakeTray();
    }
    public static SolidColorBrush Brush(string hex) {return (SolidColorBrush)new BrushConverter().ConvertFromString(hex);}
    static BitmapSource Load(string file) {
        var b=new BitmapImage();b.BeginInit();b.CacheOption=BitmapCacheOption.OnLoad;b.UriSource=new Uri(Path.Combine(Program.Home,"assets",file));b.EndInit();b.Freeze();return b;
    }
    void Tick(object sender,EventArgs args) {
        ticks++;
        if(!moving && !hovering && !busy && DateTime.Now>actionUntil) row=0;
        if(hovering) {
            sprite.Source=Reaction;
            sprite.RenderTransform=new TranslateTransform(Math.Sin(ticks*1.25)*0.7,0);
        } else {
            sprite.RenderTransform=Transform.Identity;
            if(!paused && !menuOpen) frame++;
            if(row==0 && follow && !paused && !menuOpen && ticks%45>12 && !moving && !busy) {
                NativePoint p; GetCursorPos(out p); Point local=PointFromScreen(new Point(p.X,p.Y));
                double dx=local.X-Width/2, dy=local.Y-Height*0.48;
                double angle=(Math.Atan2(dy,dx)*180/Math.PI+450)%360;
                int idx=((int)Math.Round(angle/22.5))%16;
                sprite.Source=Frames[9+idx/8][idx%8];
            } else sprite.Source=Frames[row][frame%counts[row]];
        }
        if(DateTime.Now>bubbleUntil && !hovering && !busy) bubble.Visibility=Visibility.Hidden;
        if(!busy && !hovering && !moving && !paused && !menuOpen && ticks%250==0) Play(3,1800);
    }
    void Play(int r,int duration) {if(busy)return;hovering=false;row=r;frame=0;actionUntil=DateTime.Now.AddMilliseconds(duration);sprite.Source=Frames[r][0];}
    void Say(string text,int ms) {status.Text=text;bubble.Visibility=Visibility.Visible;bubbleUntil=DateTime.Now.AddMilliseconds(ms);}
    void ResetHover() {hovering=false;caption.Text="桐崎千棘  ·  拖文件给我";sprite.RenderTransform=Transform.Identity;Play(0,0);bubble.Visibility=Visibility.Hidden;}
    void DragOverPet(object sender,DragEventArgs e) {
        bool accept=!busy && e.Data.GetDataPresent(DataFormats.FileDrop) && (e.AllowedEffects&DragDropEffects.Move)!=0;
        e.Effects=accept?DragDropEffects.Move:DragDropEffects.None;e.Handled=true;
        if(accept) {hovering=true;sprite.Source=Reaction;caption.Text="松开后确认回收";Say("怎么又把垃圾交给我啦！",2000);}
        else if(!busy) ResetHover();
    }
    void DropOnPet(object sender,DragEventArgs e) {
        // We perform the shell operation ourselves. Never tell the drag source
        // to delete its originals after the drop returns.
        e.Effects=DragDropEffects.None;e.Handled=true;
        if(busy || !e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        string[] paths;
        try {paths=FilePolicy.Validate((string[])e.Data.GetData(DataFormats.FileDrop));}
        catch(Exception ex) {ResetHover();MessageBox.Show(this,ex.Message,"这次不能接收",MessageBoxButton.OK,MessageBoxImage.Information);return;}
        ConfirmAndRecycle(paths);
    }
    public void ConfirmAndRecycle(string[] paths) {
        hovering=true;sprite.Source=Reaction;
        bool yes=ConfirmForTests!=null ? ConfirmForTests(paths) : new ConfirmWindow(this,paths).ShowDialog()==true;
        if(!yes) {ResetHover();Say("好吧，这次先留着。",2200);return;}
        hovering=false;busy=true;row=7;frame=0;caption.Text="正在送入系统回收站…";Say("稍等，我正在收拾。",60000);
        var thread=new Thread(()=> {
            RecycleResult result;
            try {result=RecycleService.Send(paths,hwnd);} catch(Exception ex){result=new RecycleResult{Error=ex.Message,Aborted=true};}
            Dispatcher.BeginInvoke(new Action(()=> {
                busy=false;ResetHover();
                if(result.Completed==paths.Length && !result.Aborted && result.Error=="") {Play(8,2300);Say("已回收 "+result.Completed+" 项，记得保持整洁！",4200);}
                else {Play(5,2600);Say("已回收 "+result.Completed+" / "+paths.Length+" 项",4000);MessageBox.Show(this,"已回收 "+result.Completed+" / "+paths.Length+" 项。\n其余项目未完成；可打开回收站核对。\n\n"+result.Error,"回收结果",MessageBoxButton.OK,MessageBoxImage.Information);}
            }));
        });thread.SetApartmentState(ApartmentState.STA);thread.IsBackground=false;thread.Start();
    }
    MenuItem Item(string text,Action action) {var i=new MenuItem{Header=text};i.Click+=(s,e)=>action();return i;}
    ContextMenu BuildMenu() {
        var m=new ContextMenu{FontFamily=FontFamily,FontSize=13};
        m.Items.Add(new MenuItem{Header="桐崎千棘 · 回收站桌宠",IsEnabled=false});
        m.Items.Add(Item("打开系统回收站",OpenBin));
        m.Items.Add(Item("预览：咬手帕",()=> {hovering=true;sprite.Source=Reaction;caption.Text="表情预览 · 不操作文件";Say("怎么又把垃圾交给我啦！",4000);var t=new DispatcherTimer{Interval=TimeSpan.FromSeconds(4)};t.Tick+=(s,e)=> {t.Stop();if(!busy)ResetHover();};t.Start();}));
        var motions=new MenuItem{Header="原有动作（全部保留）"};
        for(int r=0;r<9;r++){int index=r;motions.Items.Add(Item(names[r],()=>Play(index,index==0?0:4000)));}m.Items.Add(motions);
        var look=new MenuItem{Header="视线跟随鼠标 · 16 个方向",IsCheckable=true,IsChecked=true};look.Click+=(s,e)=>{follow=look.IsChecked;Save();};m.Items.Add(look);
        var size=new MenuItem{Header="桌宠大小"};
        foreach(double v in new[]{0.8,1.0,1.25,1.5}){double val=v;size.Items.Add(Item((int)(v*100)+"%",()=>{SetScale(val);Clamp();Save();}));}m.Items.Add(size);
        m.Items.Add(Item("暂停 / 继续日常动画",()=>paused=!paused));
        m.Items.Add(new Separator());
        m.Items.Add(Item("使用说明",()=>MessageBox.Show(this,"左键拖动：移动桌宠\n双击：挥手\n拖文件到她身上：咬手帕\n松开：核对文件并确认回收\n右键：动作、大小、打开回收站\nCtrl + Alt + P：找回桌宠\n\n从系统回收站可以还原文件。\n不提供清空或永久删除功能。\n暂不支持网络位置、移动设备、链接和云端占位文件。","桐崎千棘 · 使用说明")));
        if(testing) m.Items.Add(Item("测试：打开确认界面（不自动执行）",()=> {new ConfirmWindow(this,new[]{Path.Combine(Program.Home,"示例文件.txt")}).ShowDialog();}));
        m.Items.Add(Item("退出桌宠",Close));return m;
    }
    void OpenBin() {try{Process.Start("explorer.exe","shell:RecycleBinFolder");}catch(Exception e){MessageBox.Show(e.Message);}}
    void MakeTray() {
        tray=new Forms.NotifyIcon{Icon=System.Drawing.SystemIcons.Application,Text="桐崎千棘 · 回收站桌宠",Visible=true};
        var menu=new Forms.ContextMenuStrip();
        menu.Items.Add("找回桌宠",null,(s,e)=>BringBack());menu.Items.Add("打开系统回收站",null,(s,e)=>OpenBin());
        menu.Items.Add("退出",null,(s,e)=>Close());tray.ContextMenuStrip=menu;tray.DoubleClick+=(s,e)=>BringBack();
    }
    void BringBack() {Show();WindowState=WindowState.Normal;Topmost=true;Clamp();Activate();Say("我在这里！",2000);}
    void SetScale(double s) {scale=s;Width=240*s;Height=302*s;}
    void Clamp() {
        // WPF coordinates are DIPs; primary work area is a reliable rescue target.
        Rect wa=SystemParameters.WorkArea;
        if(Double.IsNaN(Left)||Double.IsNaN(Top)||Left+Width<0||Top+Height<0||Left>SystemParameters.VirtualScreenWidth+SystemParameters.VirtualScreenLeft||Top>SystemParameters.VirtualScreenHeight+SystemParameters.VirtualScreenTop){Left=wa.Right-Width-24;Top=wa.Bottom-Height-20;}
        if(Left>=wa.Left && Left<wa.Right && Top>=wa.Top && Top<wa.Bottom){Left=Math.Min(Left,wa.Right-Width);Top=Math.Min(Top,wa.Bottom-Height);}
    }
    void LoadSettings() {
        SetScale(1);Rect wa=SystemParameters.WorkArea;Left=wa.Right-Width-28;Top=wa.Bottom-Height-20;
        try {var a=File.ReadAllLines(Path.Combine(Program.Home,"preferences.txt"));double l,t,s;
            if(a.Length>=3 && Double.TryParse(a[0],NumberStyles.Float,CultureInfo.InvariantCulture,out l)&&Double.TryParse(a[1],NumberStyles.Float,CultureInfo.InvariantCulture,out t)&&Double.TryParse(a[2],NumberStyles.Float,CultureInfo.InvariantCulture,out s)&&!Double.IsNaN(l)&&!Double.IsNaN(t)&&!Double.IsInfinity(l)&&!Double.IsInfinity(t)){SetScale(Math.Max(.8,Math.Min(1.5,s)));Left=l;Top=t;}
        }catch{} Clamp();
    }
    void Save(){try{File.WriteAllLines(Path.Combine(Program.Home,"preferences.txt"),new[]{Left.ToString(CultureInfo.InvariantCulture),Top.ToString(CultureInfo.InvariantCulture),scale.ToString(CultureInfo.InvariantCulture)});}catch{}}
    IntPtr WindowHook(IntPtr h,int msg,IntPtr wp,IntPtr lp,ref bool handled){if(msg==0x0312){BringBack();handled=true;}return IntPtr.Zero;}
    [DllImport("user32.dll")]static extern bool RegisterHotKey(IntPtr h,int id,uint modifiers,uint key);
    [DllImport("user32.dll")]static extern bool UnregisterHotKey(IntPtr h,int id);
    [DllImport("user32.dll")]static extern bool GetCursorPos(out NativePoint p);
    [StructLayout(LayoutKind.Sequential)]struct NativePoint {public int X,Y;}
}

public class ConfirmWindow : Window {
    public ConfirmWindow(Window owner,string[] paths) {
        Owner=owner;Title="确认回收 · 桐崎千棘";Width=540;Height=390;ResizeMode=ResizeMode.NoResize;WindowStartupLocation=WindowStartupLocation.CenterScreen;
        Background=PetWindow.Brush("#FFF9FB");FontFamily=new FontFamily("Microsoft YaHei UI");FontSize=13;Topmost=true;
        var root=new Grid{Margin=new Thickness(24)};
        root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});root.RowDefinitions.Add(new RowDefinition());root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        root.Children.Add(new TextBlock{Text="这 "+paths.Length+" 项，真的交给我？",FontSize=22,FontWeight=FontWeights.SemiBold,Foreground=PetWindow.Brush("#503447"),Margin=new Thickness(0,0,0,8)});
        var sub=new TextBlock{Text="移入 Windows 回收站，可在那里还原。无法回收时停止。",Foreground=PetWindow.Brush("#806E7B"),Margin=new Thickness(0,0,0,15)};Grid.SetRow(sub,1);root.Children.Add(sub);
        var files=new ListBox{Background=Brushes.White,BorderBrush=PetWindow.Brush("#EAD9E2"),Padding=new Thickness(8),Margin=new Thickness(0,0,0,18)};
        foreach(string p in paths)files.Items.Add(new TextBlock{Text=p,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(2,5,2,5),MaxWidth=420});Grid.SetRow(files,2);root.Children.Add(files);
        var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};
        var cancel=new Button{Content="先留着",IsCancel=true,IsDefault=true,MinWidth=94,Padding=new Thickness(14,9,14,9),Margin=new Thickness(0,0,10,0)};
        cancel.Click+=(s,e)=>DialogResult=false;
        var yes=new Button{Content="移入回收站",MinWidth=130,Padding=new Thickness(14,9,14,9),Background=PetWindow.Brush("#C94E76"),Foreground=Brushes.White,BorderBrush=PetWindow.Brush("#C94E76")};
        yes.Click+=(s,e)=>DialogResult=true;buttons.Children.Add(cancel);buttons.Children.Add(yes);Grid.SetRow(buttons,3);root.Children.Add(buttons);Content=root;
        Loaded+=(s,e)=>cancel.Focus();
    }
}
}
