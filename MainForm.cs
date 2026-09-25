using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SteamGaze {
    public sealed class ValueSlider : UserControl {
        readonly TrackBar slider=new TrackBar();readonly Label valueText=new Label();decimal scale=1;int decimals;
        public event EventHandler ValueChanged;
        public ValueSlider(){Width=400;Height=35;slider.Dock=DockStyle.Fill;slider.TickStyle=TickStyle.None;slider.AutoSize=false;valueText.Dock=DockStyle.Right;valueText.Width=66;valueText.TextAlign=ContentAlignment.MiddleRight;Controls.Add(slider);Controls.Add(valueText);slider.ValueChanged+=(s,a)=>{valueText.Text=Value.ToString("F"+decimals);if(ValueChanged!=null)ValueChanged(this,EventArgs.Empty);};}
        public decimal Value {get{return slider.Value/scale;}set{slider.Value=Math.Max(slider.Minimum,Math.Min(slider.Maximum,(int)Math.Round(value*scale)));valueText.Text=Value.ToString("F"+decimals);}}
        public void Range(decimal min,decimal max,decimal step,int digits){decimals=digits;scale=(decimal)Math.Pow(10,digits);slider.Minimum=(int)(min*scale);slider.Maximum=(int)(max*scale);slider.SmallChange=Math.Max(1,(int)(step*scale));slider.LargeChange=slider.SmallChange*5;}
    }
    public sealed class MarkerWindow : Form {
        protected override bool ShowWithoutActivation {get{return true;}}
        protected override CreateParams CreateParams {get{var p=base.CreateParams;p.ExStyle|=0x08000000|0x20|0x80;return p;}}
        public MarkerWindow(){FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;TopMost=true;BackColor=Color.Black;TransparencyKey=Color.Black;Size=new Size(52,52);DoubleBuffered=true;}
        protected override void OnPaint(PaintEventArgs e){e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;using(var pen=new Pen(Color.Turquoise,3))e.Graphics.DrawEllipse(pen,10,10,30,30);}
        public void UpdateHit(DesktopOutput hit,bool show){if(!show||hit==null){Hide();return;}Location=new Point(hit.x-26,hit.y-26);if(!Visible)Show();Invalidate();}
    }
    public sealed class DesktopPreview : Control {
        public DesktopOutput Hit;
        public DesktopPreview(){DoubleBuffered=true;MinimumSize=new Size(400,190);Dock=DockStyle.Fill;}
        protected override void OnPaint(PaintEventArgs e){
            e.Graphics.Clear(Color.FromArgb(20,28,40));Rectangle all=SystemInformation.VirtualScreen;
            double scale=Math.Min((Width-30.0)/all.Width,(Height-30.0)/all.Height);
            foreach(var screen in Screen.AllScreens){var r=screen.Bounds;var draw=new RectangleF(15+(float)((r.X-all.X)*scale),15+(float)((r.Y-all.Y)*scale),(float)(r.Width*scale),(float)(r.Height*scale));
                using(var p=new Pen(Color.SlateGray))e.Graphics.DrawRectangle(p,draw.X,draw.Y,draw.Width,draw.Height);
                e.Graphics.DrawString(screen.DeviceName+"  "+r.Width+"×"+r.Height,Font,Brushes.LightGray,draw.X+7,draw.Y+7);
            }
            if(Hit!=null){float x=15+(float)((Hit.x-all.X)*scale),y=15+(float)((Hit.y-all.Y)*scale);using(var p=new Pen(Color.Turquoise,2))e.Graphics.DrawEllipse(p,x-6,y-6,12,12);}
        }
    }
    public sealed class TargetForm : Form {
        readonly Engine engine;
        readonly MappingCheck check=new MappingCheck();readonly System.Diagnostics.Stopwatch watch=System.Diagnostics.Stopwatch.StartNew();bool saved;
        public TargetForm(Engine e){engine=e;Text="Steam Gaze — 桌面映射验收靶板";StartPosition=FormStartPosition.Manual;var screen=Screen.PrimaryScreen.WorkingArea;Size=new Size(Math.Min(1100,screen.Width),Math.Min(760,screen.Height));Location=new Point(screen.X+(screen.Width-Width)/2,screen.Y+(screen.Height-Height)/2);BackColor=Color.FromArgb(17,24,36);DoubleBuffered=true;var timer=new Timer{Interval=100};timer.Tick+=(s,a)=>{if(!saved){check.Observe(engine.Latest);if(watch.Elapsed.TotalSeconds>=30)SaveReport();}Invalidate();};timer.Start();FormClosed+=(s,a)=>{timer.Dispose();SaveReport();};}
        void SaveReport(){if(saved)return;saved=true;check.finishedUtc=DateTime.UtcNow.ToString("O");try{Files.AtomicWrite(System.IO.Path.Combine(Files.Data,"mapping-check.json"),new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(check));}catch(Exception e){Files.Log("Mapping check report: "+e.Message);}}
        protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;using(var f=new Font("Microsoft YaHei UI",14))using(var p=new Pen(Color.White,2)){
            e.Graphics.DrawString("在 VR 的 Desktop+ 标签页中看此靶板；固定看白色十字，勿追逐圆环。",f,Brushes.White,28,22);
            e.Graphics.DrawString(String.Format("30 秒链路检查：有效眼动 {0} 次 / 桌面命中 {1} 次   {2}",check.validEyeObservations,check.desktopHitObservations,saved?"已保存报告（精度未验收）":Math.Max(0,30-(int)watch.Elapsed.TotalSeconds)+" 秒剩余"),f,Brushes.LightGray,28,58);
            foreach(var uv in new[]{new PointF(.5f,.5f),new PointF(.15f,.2f),new PointF(.85f,.2f),new PointF(.15f,.8f),new PointF(.85f,.8f)}){
                var c=new Point((int)(ClientSize.Width*uv.X),100+(int)((ClientSize.Height-215)*uv.Y));var desktop=PointToScreen(c);
                e.Graphics.DrawLine(p,c.X-12,c.Y,c.X+12,c.Y);e.Graphics.DrawLine(p,c.X,c.Y-12,c.X,c.Y+12);
                e.Graphics.DrawString(desktop.X+", "+desktop.Y,f,Brushes.Gray,c.X+17,c.Y+8);
            }
            var h=engine.Latest.desktop;if(h!=null){var pt=PointToClient(new Point(h.x,h.y));using(var pen=new Pen(Color.Turquoise,3))e.Graphics.DrawEllipse(pen,pt.X-10,pt.Y-10,20,20);}
            var frame=engine.Latest;
            e.Graphics.DrawString(frame.valid?"眼动：有效":"眼动："+frame.reason,f,frame.valid?Brushes.Turquoise:Brushes.Goldenrod,28,ClientSize.Height-108);
            e.Graphics.DrawString(frame.desktopStatus,f,Brushes.Turquoise,28,ClientSize.Height-74);
            e.Graphics.DrawString(h==null?"当前 Windows 坐标：无有效命中。Esc 关闭。":"当前 Windows 坐标："+h.x+", "+h.y+"  "+h.monitor+"。Esc 关闭。",f,Brushes.White,28,ClientSize.Height-40);
        }}
        protected override bool ProcessCmdKey(ref Message m,Keys key){if(key==Keys.Escape){Close();return true;}return base.ProcessCmdKey(ref m,key);}
    }
    public sealed class MainForm : Form {
        readonly Engine engine;readonly MarkerWindow marker=new MarkerWindow();readonly Timer timer=new Timer{Interval=100};
        readonly NotifyIcon tray=new NotifyIcon();
        double lastLabels;readonly System.Diagnostics.Stopwatch uiWatch=System.Diagnostics.Stopwatch.StartNew();
        Label status=new Label(),stats=new Label(),mapStatus=new Label();DesktopPreview preview=new DesktopPreview();
        CheckBox enabled=new CheckBox(),desktopEnabled=new CheckBox(),desktopMark=new CheckBox(),auto=new CheckBox(),export=new CheckBox();
        ComboBox mode=new ComboBox(),key=new ComboBox(),sourceMode=new ComboBox(),monitor=new ComboBox();
        ValueSlider smoothing=new ValueSlider(),size=new ValueSlider(),opacity=new ValueSlider(),distance=new ValueSlider();Button color=new Button();bool loading;
        TargetForm targetForm;
        public MainForm(Engine e){
            engine=e;Text="Steam Gaze — 眼动与桌面映射";Size=new Size(850,780);MinimumSize=new Size(760,700);StartPosition=FormStartPosition.CenterScreen;Font=new Font("Microsoft YaHei UI",10);BackColor=Color.FromArgb(245,247,250);
            var root=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(20),ColumnCount=1,RowCount=5};
            root.RowStyles.Add(new RowStyle(SizeType.Absolute,46));root.RowStyles.Add(new RowStyle(SizeType.Absolute,35));root.RowStyles.Add(new RowStyle(SizeType.Absolute,35));root.RowStyles.Add(new RowStyle(SizeType.Percent,100));root.RowStyles.Add(new RowStyle(SizeType.Absolute,37));
            root.Controls.Add(new Label{Text="STEAM GAZE",Font=new Font("Segoe UI",22,FontStyle.Bold),AutoSize=true},0,0);
            status.Dock=DockStyle.Fill;status.Font=new Font(Font,FontStyle.Bold);root.Controls.Add(status,0,1);stats.Dock=DockStyle.Fill;root.Controls.Add(stats,0,2);
            var tabs=new TabControl{Dock=DockStyle.Fill};root.Controls.Add(tabs,0,3);Controls.Add(root);
            var gaze=Page(tabs,"眼动显示");
            Check(gaze,"VR 注视标记",enabled);Choice(gaze,"显示模式",mode,new[]{"平滑视线","原始视线","同时显示两者"});
            Number(gaze,"平滑时间（毫秒）",smoothing,0,200,5,0);Number(gaze,"标记直径（角度）",size,.1m,4,.1m,2);
            Number(gaze,"不透明度（%）",opacity,5,100,5,0);Number(gaze,"未命中桌面时距离（米）",distance,.4m,8,.1m,1);
            color.Text="选择处理后视线颜色";color.AutoSize=true;Add(gaze,"颜色",color);
            color.Click+=(s,a)=>{using(var d=new ColorDialog{Color=ColorTranslator.FromHtml(engine.Config.Color)})if(d.ShowDialog(this)==DialogResult.OK){var c=engine.Config;c.Color=ColorTranslator.ToHtml(d.Color);engine.Configure(c);color.BackColor=d.Color;}};
            Add(gaze,"说明",new Label{AutoSize=true,MaximumSize=new Size(430,0),Text="青色默认为处理后视线，橙色为原始视线。平滑改善显示稳定性，不提高测量准确度。眼动失效时立即隐藏，恢复后重置滤波。"});
            var desk=Page(tabs,"虚拟桌面映射");Check(desk,"启用桌面映射",desktopEnabled);
            key.DropDownStyle=ComboBoxStyle.DropDown;key.Width=410;Add(desk,"目标 Overlay",key);
            key.DropDown+=(s,a)=>{string text=key.Text;key.Items.Clear();if(engine.Latest.overlayKeys!=null)key.Items.AddRange(engine.Latest.overlayKeys);key.Text=text;};
            Choice(desk,"纹理来源",sourceMode,new[]{"Desktop+：完整桌面纹理","指定显示器：独立完整纹理"});
            Choice(desk,"单显示器来源",monitor,Array.ConvertAll(Screen.AllScreens,x=>x.DeviceName));
            var apply=new Button{Text="应用桌面来源",AutoSize=true};Add(desk,"",apply);apply.Click+=(s,a)=>Save();
            Check(desk,"在 Windows 上显示注视环",desktopMark);mapStatus.AutoSize=true;mapStatus.MaximumSize=new Size(430,0);Add(desk,"映射状态",mapStatus);
            var target=new Button{Text="打开 Desktop+ 并验收（30 秒）",AutoSize=true};Add(desk,"验收",target);target.Click+=(s,a)=>OpenMappingCheck();
            Add(desk,"桌面预览",preview);
            Add(desk,"支持范围",new Label{AutoSize=true,MaximumSize=new Size(430,0),Text="第一种模式对应 Desktop+ 默认桌面复制（完整 Windows 桌面纹理）。第二种仅用于整块单显示器纹理。窗口捕获、网页、任意第三方内容需要专用适配器。移动/旋转/缩放/曲率由 SteamVR 实时求交处理。"});
            var other=Page(tabs,"启动与扩展");Check(other,"随 SteamVR 自动启动",auto);Check(other,"启用本机眼动数据输出",export);
            var info=new TextBox{Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Vertical,Width=450,Height=240,Text="本机命名管道：\\\\.\\pipe\\SteamGazeOverlay.v1\r\n格式：每行一个 JSON，schemaVersion=1，最多约 30 Hz。仅当前 Windows 用户可连接。\r\n\r\n字段包含原始/平滑视线、有效状态、桌面目标、归一化坐标和 Windows 物理像素坐标。未命中时 desktop=null。\r\n\r\n时间戳为主机轮询时间，SteamVR 接口未提供传感器采样时间；重复坐标不被误判为失效。\r\n\r\n鼠标不会移动，也不会自动点击。关闭窗口即可退出 Overlay。"};Add(other,"扩展接口",info);
            var folder=new Button{Text="打开设置与诊断目录",AutoSize=true};Add(other,"文件",folder);folder.Click+=(s,a)=>System.Diagnostics.Process.Start("explorer.exe",Files.Data);
            root.Controls.Add(new Label{Text="SteamVR Dashboard 中也可打开 Steam Gaze。关闭此窗口退出。",Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft},0,4);
            foreach(var c in new[]{enabled,desktopEnabled,desktopMark,auto,export})c.CheckedChanged+=(s,a)=>Save();
            foreach(var c in new[]{smoothing,size,opacity,distance})c.ValueChanged+=(s,a)=>Save();mode.SelectedIndexChanged+=(s,a)=>Save();
            LoadConfig();engine.SettingsChanged+=()=>{if(IsHandleCreated&&!IsDisposed)BeginInvoke((Action)LoadConfig);};
            tray.Icon=SystemIcons.Information;tray.Text="Steam Gaze";tray.Visible=true;
            var menu=new ContextMenuStrip();menu.Items.Add("打开设置",null,(s,a)=>{Show();WindowState=FormWindowState.Normal;Activate();});menu.Items.Add("退出 Steam Gaze",null,(s,a)=>Close());tray.ContextMenuStrip=menu;
            tray.DoubleClick+=(s,a)=>{Show();WindowState=FormWindowState.Normal;Activate();};
            Resize+=(s,a)=>{if(WindowState==FormWindowState.Minimized)Hide();};
            if(Program.StartInTray)Shown+=(s,a)=>Hide();
            if(Program.StartMappingCheck)Shown+=(s,a)=>BeginInvoke((Action)OpenMappingCheck);
            timer.Tick+=(s,a)=>RefreshState();timer.Start();
            FormClosed+=(s,a)=>{timer.Stop();timer.Dispose();tray.Visible=false;tray.Dispose();if(targetForm!=null&&!targetForm.IsDisposed)targetForm.Close();marker.Close();engine.Dispose();};
        }
        void OpenMappingCheck(){if(targetForm==null||targetForm.IsDisposed)targetForm=new TargetForm(engine);targetForm.Show();targetForm.Activate();engine.RequestDesktopDashboard();}
        TableLayoutPanel Page(TabControl tabs,string name){var tab=new TabPage(name){AutoScroll=true};tabs.TabPages.Add(tab);var t=new TableLayoutPanel{Dock=DockStyle.Top,AutoSize=true,ColumnCount=2,Padding=new Padding(14)};t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,220));t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));tab.Controls.Add(t);return t;}
        void Add(TableLayoutPanel t,string label,Control c){int row=t.RowCount++;t.RowStyles.Add(new RowStyle(SizeType.AutoSize));t.Controls.Add(new Label{Text=label,AutoSize=true,Margin=new Padding(0,9,8,12)},0,row);c.Margin=new Padding(0,6,0,10);t.Controls.Add(c,1,row);}
        void Check(TableLayoutPanel t,string label,CheckBox c){c.AutoSize=true;c.Text="开启";Add(t,label,c);}
        void Choice(TableLayoutPanel t,string label,ComboBox c,string[] values){c.DropDownStyle=ComboBoxStyle.DropDownList;c.Width=410;c.Items.AddRange(values);Add(t,label,c);}
        void Number(TableLayoutPanel t,string label,ValueSlider c,decimal min,decimal max,decimal inc,int decimals){c.Range(min,max,inc,decimals);Add(t,label,c);}
        void LoadConfig(){loading=true;var s=engine.Config;enabled.Checked=s.Enabled;mode.SelectedIndex=s.Mode=="Raw"?1:s.Mode=="Both"?2:0;smoothing.Value=(decimal)s.SmoothingMs;size.Value=(decimal)s.MarkerDegrees;opacity.Value=(decimal)(s.Opacity*100);distance.Value=(decimal)s.Distance;color.BackColor=ColorTranslator.FromHtml(s.Color);desktopEnabled.Checked=s.DesktopEnabled;desktopMark.Checked=s.DesktopMarker;key.Text=s.OverlayKey;sourceMode.SelectedIndex=s.DesktopMode=="SingleMonitorTexture"?1:0;monitor.SelectedItem=s.MonitorDevice;auto.Checked=s.AutoLaunch;export.Checked=s.ExportEnabled;loading=false;}
        void Save(){if(loading)return;var s=engine.Config;s.Enabled=enabled.Checked;s.Mode=mode.SelectedIndex==1?"Raw":mode.SelectedIndex==2?"Both":"Smooth";s.SmoothingMs=(double)smoothing.Value;s.MarkerDegrees=(double)size.Value;s.Opacity=(double)opacity.Value/100;s.Distance=(double)distance.Value;s.DesktopEnabled=desktopEnabled.Checked;s.DesktopMarker=desktopMark.Checked;s.OverlayKey=key.Text;s.DesktopMode=sourceMode.SelectedIndex==1?"SingleMonitorTexture":"DesktopPlusFullDesktop";s.MonitorDevice=monitor.Text;s.AutoLaunch=auto.Checked;s.ExportEnabled=export.Checked;try{engine.Configure(s);}catch(Exception e){MessageBox.Show(this,e.Message,"保存失败");}}
        public void RenderUiChecks(string directory){
            StartPosition=FormStartPosition.Manual;Location=new Point(-30000,-30000);ShowInTaskbar=false;Show();
            var tabs=FindTabs(this);for(int i=0;i<tabs.TabCount;i++){tabs.SelectedIndex=i;PerformLayout();Application.DoEvents();using(var b=new Bitmap(Width,Height)){DrawToBitmap(b,new Rectangle(0,0,Width,Height));b.Save(System.IO.Path.Combine(directory,"ui-tab-"+i+".png"));}}
            var before=engine.Config;enabled.Checked=!before.Enabled;if(engine.Config.Enabled==before.Enabled)throw new Exception("Enabled checkbox did not apply");enabled.Checked=before.Enabled;
            smoothing.Value=80;if(engine.Config.SmoothingMs!=80)throw new Exception("Smoothing setting did not apply");smoothing.Value=(decimal)before.SmoothingMs;
            var loaded=Files.Load();if(loaded.Enabled!=before.Enabled||loaded.SmoothingMs!=before.SmoothingMs)throw new Exception("Settings persistence failed");
            System.IO.File.WriteAllText(System.IO.Path.Combine(directory,"ui-test.txt"),"PASS: 3 tabs rendered; enable, smoothing and settings round-trip verified");
            using(var target=new TargetForm(engine)){target.Location=new Point(-30000,-30000);target.ShowInTaskbar=false;target.Show();Application.DoEvents();using(var bmp=new Bitmap(target.Width,target.Height)){target.DrawToBitmap(bmp,new Rectangle(Point.Empty,target.Size));bmp.Save(System.IO.Path.Combine(directory,"mapping-check-ui.png"));}}
        }
        static TabControl FindTabs(Control c){foreach(Control child in c.Controls){if(child is TabControl)return (TabControl)child;var found=FindTabs(child);if(found!=null)return found;}return null;}
        void RefreshState(){
            if(Program.StopRequested!=null&&Program.StopRequested.WaitOne(0)){Close();return;}
            if(Program.ShowRequested!=null&&Program.ShowRequested.WaitOne(0)){Show();WindowState=FormWindowState.Normal;Activate();}
            if(Program.VerifyRequested!=null&&Program.VerifyRequested.WaitOne(0))OpenMappingCheck();
            var watch=System.Diagnostics.Stopwatch.StartNew();var v=engine.Latest;
            marker.UpdateHit(v.desktop,engine.Config.DesktopMarker&&v.valid);
            if(!Visible)return;
            if(uiWatch.Elapsed.TotalSeconds-lastLabels<.5)return;
            lastLabels=uiWatch.Elapsed.TotalSeconds;
            SuspendLayout();
            try{status.Text=v.valid?"● 眼动正在追踪":"● 等待眼动 — "+v.reason;status.ForeColor=v.valid?Color.Teal:Color.DarkGoldenrod;stats.Text=String.Format("轮询 {0:F0} Hz  |  有效 {1:P0}  |  本轮处理 {2:F2} ms  |  场景 PID {3}",v.pollHz,v.validFraction,v.loopMs,v.scenePid);if(mapStatus.Visible)mapStatus.Text=v.desktopStatus;if(preview.Visible){preview.Hit=v.desktop;preview.Invalidate();}}
            finally{ResumeLayout(false);}
            if(watch.Elapsed.TotalMilliseconds>20)Files.Log("Slow UI refresh ms="+watch.Elapsed.TotalMilliseconds);
        }
    }
}
