using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using Valve.VR;

namespace SteamGaze {
    public sealed class OverlayRenderer : IDisposable {
        public ulong RawHandle,SmoothHandle,Dashboard,Thumbnail;
        public Action<string> Command;
        public static readonly Rectangle[] Buttons={new Rectangle(28,300,194,64),new Rectangle(244,300,194,64),new Rectangle(460,300,194,64),new Rectangle(676,300,194,64)};
        public OverlayRenderer() {
            try {
                RawHandle=Create("raw");SmoothHandle=Create("smooth");
                Check(OpenVR.Overlay.CreateDashboardOverlay("local.steamgaze.dashboard","Steam Gaze",ref Dashboard,ref Thumbnail));
                Check(OpenVR.Overlay.SetOverlayWidthInMeters(Dashboard,1.3f));
                Check(OpenVR.Overlay.SetOverlayInputMethod(Dashboard,VROverlayInputMethod.Mouse));
                var size=new HmdVector2_t{v0=900,v1=460};Check(OpenVR.Overlay.SetOverlayMouseScale(Dashboard,ref size));
                using(var bmp=Ring(64)) {Upload(RawHandle,bmp);Upload(SmoothHandle,bmp);Upload(Thumbnail,bmp);}
                using(var bmp=DashboardBitmap(new Snapshot(),new Settings()))Upload(Dashboard,bmp);
                OpenVR.Overlay.SetOverlayColor(RawHandle,1,0.65f,0.15f);
            } catch {Dispose();throw;}
        }
        static ulong Create(string name) {
            ulong h=0;Check(OpenVR.Overlay.CreateOverlay("local.steamgaze."+name,"Steam Gaze "+name,ref h));
            Check(OpenVR.Overlay.SetOverlayInputMethod(h,VROverlayInputMethod.None));
            Check(OpenVR.Overlay.SetOverlayFlag(h,VROverlayFlags.VisibleInDashboard,true));
            Check(OpenVR.Overlay.SetOverlayFlag(h,VROverlayFlags.NoBackside,true));
            Check(OpenVR.Overlay.SetOverlaySortOrder(h,100));return h;
        }
        public static void Check(EVROverlayError e){if(e!=EVROverlayError.None)throw new Exception("Overlay: "+e);}
        public static Bitmap Ring(int size) {
            var b=new Bitmap(size,size,PixelFormat.Format32bppArgb);
            using(var g=Graphics.FromImage(b))using(var pen=new Pen(Color.White,3)) {g.Clear(Color.Transparent);g.SmoothingMode=SmoothingMode.AntiAlias;g.DrawEllipse(pen,6,6,size-12,size-12);}
            return b;
        }
        public static void Upload(ulong h,Bitmap b) {
            var d=b.LockBits(new Rectangle(0,0,b.Width,b.Height),ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);
            try {
                byte[] pixels=new byte[b.Width*b.Height*4];
                for(int y=0;y<b.Height;y++)Marshal.Copy(IntPtr.Add(d.Scan0,y*d.Stride),pixels,y*b.Width*4,b.Width*4);
                for(int i=0;i<pixels.Length;i+=4){byte r=pixels[i];pixels[i]=pixels[i+2];pixels[i+2]=r;}
                var pin=GCHandle.Alloc(pixels,GCHandleType.Pinned);
                try {Check(OpenVR.Overlay.SetOverlayRaw(h,pin.AddrOfPinnedObject(),(uint)b.Width,(uint)b.Height,4));}finally{pin.Free();}
            }finally{b.UnlockBits(d);}
        }
        public void Render(Settings s,GazeSample raw,GazeSample smooth,HmdMatrix34_t head,DesktopHit hit) {
            Color c=ColorTranslator.FromHtml(s.Color);OpenVR.Overlay.SetOverlayColor(SmoothHandle,c.R/255f,c.G/255f,c.B/255f);
            ShowMarker(RawHandle,s.Enabled&&(s.Mode=="Raw"||s.Mode=="Both"),s,raw,head,s.Mode=="Raw"?hit:null);
            ShowMarker(SmoothHandle,s.Enabled&&(s.Mode=="Smooth"||s.Mode=="Both"),s,smooth,head,hit);
        }
        void ShowMarker(ulong h,bool enabled,Settings s,GazeSample sample,HmdMatrix34_t head,DesktopHit hit) {
            if(!enabled||sample==null||!sample.valid){OpenVR.Overlay.HideOverlay(h);return;}
            double distance=s.Distance;
            if(hit!=null) {
                var transform=head;distance=(hit.point-VrMath.Position(head)).Length;
                Vec pos=hit.point-sample.direction*0.003;
                transform.m3=(float)pos.x;transform.m7=(float)pos.y;transform.m11=(float)pos.z;
                Check(OpenVR.Overlay.SetOverlayTransformAbsolute(h,ETrackingUniverseOrigin.TrackingUniverseStanding,ref transform));
            } else {
                // The sample is world-space; derive head-local location to let the compositor late-latch the HMD.
                Vec pos=sample.origin+sample.direction*distance;
                var transform=VrMath.Pose(VrMath.InverseRotate(head,pos-VrMath.Position(head)));
                Check(OpenVR.Overlay.SetOverlayTransformTrackedDeviceRelative(h,0,ref transform));
            }
            Check(OpenVR.Overlay.SetOverlayWidthInMeters(h,(float)(2*Math.Tan(s.MarkerDegrees*Math.PI/360)*Math.Max(0.1,distance))));
            Check(OpenVR.Overlay.SetOverlayAlpha(h,(float)s.Opacity));Check(OpenVR.Overlay.ShowOverlay(h));
        }
        public void UpdateDashboard(Snapshot state,Settings s) {
            if(OpenVR.Overlay.IsOverlayVisible(Dashboard))using(var b=DashboardBitmap(state,s))Upload(Dashboard,b);
            var e=new VREvent_t();
            while(OpenVR.Overlay.PollNextOverlayEvent(Dashboard,ref e,(uint)Marshal.SizeOf(typeof(VREvent_t)))) {
                if(e.eventType!=(uint)EVREventType.VREvent_MouseButtonUp)continue;
                var pt=new Point((int)e.data.mouse.x,460-(int)e.data.mouse.y);
                for(int i=0;i<Buttons.Length;i++)if(Buttons[i].Contains(pt)&&Command!=null)Command(new[]{"enabled","mode","smoothing","desktopmarker"}[i]);
            }
        }
        public static Bitmap DashboardBitmap(Snapshot state,Settings s) {
            var b=new Bitmap(900,460,PixelFormat.Format32bppArgb);
            using(var g=Graphics.FromImage(b))using(var title=new Font("Segoe UI",30,FontStyle.Bold))using(var font=new Font("Microsoft YaHei UI",15))using(var small=new Font("Microsoft YaHei UI",12)) {
                g.Clear(Color.FromArgb(17,24,36));g.DrawString("STEAM GAZE",title,Brushes.White,26,22);
                g.DrawString("眼动状态："+(state.valid?"正在追踪":"等待有效眼动"),font,state.valid?Brushes.Turquoise:Brushes.Goldenrod,28,98);
                g.DrawString(state.desktopStatus??"等待 SteamVR",font,Brushes.White,28,150);
                g.DrawString(String.Format("采样轮询 {0:F0} Hz   ·   平滑 {1:F0} ms   ·   模式 {2}",state.pollHz,s.SmoothingMs,s.Mode),font,Brushes.LightGray,28,207);
                var labels=new[]{s.Enabled?"隐藏 VR 标记":"显示 VR 标记","显示："+s.Mode,"平滑："+s.SmoothingMs+" ms",s.DesktopMarker?"关闭桌面标记":"开启桌面标记"};
                for(int i=0;i<Buttons.Length;i++){using(var brush=new SolidBrush(Color.FromArgb(42,56,73)))g.FillRectangle(brush,Buttons[i]);g.DrawString(labels[i],small,Brushes.White,Buttons[i].X+10,Buttons[i].Y+20);}
                g.DrawString("青色：处理后视线    橙色：原始视线    未追踪时自动隐藏",small,Brushes.LightGray,28,392);
            }return b;
        }
        public void Dispose() { foreach(ulong h in new[]{RawHandle,SmoothHandle,Dashboard,Thumbnail})if(h!=0)try{OpenVR.Overlay.DestroyOverlay(h);}catch{}RawHandle=SmoothHandle=Dashboard=Thumbnail=0; }
    }
}
