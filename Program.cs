using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using System.Web.Script.Serialization;
using Valve.VR;

namespace SteamGaze {
    static class Program {
        public static EventWaitHandle StopRequested;
        public static EventWaitHandle ShowRequested;
        public static EventWaitHandle VerifyRequested;
        public static bool StartInTray;
        public static bool StartMappingCheck;
        [DllImport("user32.dll")]static extern bool SetProcessDpiAwarenessContext(IntPtr context);
        [STAThread]static int Main(string[] args) {
            try{SetProcessDpiAwarenessContext(new IntPtr(-4));}catch{}
            if(Array.IndexOf(args,"--render-ui")>=0)Files.Data=Path.Combine(Files.Root,"test-artifacts");
            Files.Init();
            try{
                if(Array.IndexOf(args,"--stop")>=0){using(var stop=EventWaitHandle.OpenExisting("Local\\SteamGazeOverlay.Stop"))stop.Set();return 0;}
                if(Array.IndexOf(args,"--render-ui")>=0){Application.EnableVisualStyles();using(var engine=new Engine(new Settings(),false))using(var form=new MainForm(engine))form.RenderUiChecks(Files.Data);return 0;}
                if(Array.IndexOf(args,"--self-test")>=0)return Tests.Run();
                if(Array.IndexOf(args,"--probe")>=0)return Tests.Probe();
                if(Array.IndexOf(args,"--inspect")>=0)return Tests.Inspect();
                if(Array.IndexOf(args,"--mapping-test")>=0)return Tests.MappingProbe();
                bool fresh;using(var mutex=new Mutex(true,"Local\\SteamGazeOverlay.Singleton",out fresh)){
                    if(!fresh){try{using(var request=EventWaitHandle.OpenExisting(Array.IndexOf(args,"--verify-mapping")>=0?"Local\\SteamGazeOverlay.Verify":"Local\\SteamGazeOverlay.Show"))if(Array.IndexOf(args,"--tray")<0)request.Set();}catch{}Files.Log("An instance is already running");return 0;}
                    StopRequested=new EventWaitHandle(false,EventResetMode.ManualReset,"Local\\SteamGazeOverlay.Stop");StopRequested.Reset();
                    ShowRequested=new EventWaitHandle(false,EventResetMode.AutoReset,"Local\\SteamGazeOverlay.Show");
                    VerifyRequested=new EventWaitHandle(false,EventResetMode.AutoReset,"Local\\SteamGazeOverlay.Verify");
                    StartInTray=Array.IndexOf(args,"--tray")>=0;
                    StartMappingCheck=Array.IndexOf(args,"--verify-mapping")>=0;
                    if(Array.IndexOf(args,"--headless")>=0){using(var background=new Engine(Files.Load()))StopRequested.WaitOne();StopRequested.Dispose();mutex.ReleaseMutex();return 0;}
                    Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
                    Application.ThreadException+=(s,e)=>{Files.Log(e.Exception.ToString());MessageBox.Show(e.Exception.Message,"Steam Gaze");};
                    var engine=new Engine(Files.Load());Application.Run(new MainForm(engine));StopRequested.Dispose();StopRequested=null;ShowRequested.Dispose();ShowRequested=null;VerifyRequested.Dispose();VerifyRequested=null;mutex.ReleaseMutex();
                }
                return 0;
            }catch(Exception e){Files.Log(e.ToString());File.WriteAllText(Path.Combine(Files.Data,"fatal.txt"),e.ToString());return 1;}
        }
    }
    static class Tests {
        static int count;
        static void Assert(bool ok,string name){if(!ok)throw new Exception("FAIL: "+name);count++;}
        public static int Run(){
            var union=new Rectangle(0,-1440,2560,2880);var monitors=new[]{new Rectangle(0,-1440,2560,1440),new Rectangle(0,0,2560,1440)};Point p;
            Assert(Mapping.ToDesktop(.5,.25,union,monitors,out p)&&p==new Point(1280,720),"primary centre / bottom-left UV");
            Assert(Mapping.ToDesktop(.5,.75,union,monitors,out p)&&p==new Point(1280,-720),"negative monitor coordinates");
            Assert(Mapping.ToDesktop(0,1,union,monitors,out p)&&p==new Point(0,-1440),"top left");
            Assert(Mapping.ToDesktop(1,0,union,monitors,out p)&&p==new Point(2559,1439),"bottom right clamps boundary");
            Assert(!Mapping.ToDesktop(-.1,.5,union,monitors,out p),"offscreen rejected");
            Assert(!Mapping.ToDesktop(double.NaN,.5,union,monitors,out p),"NaN rejected");
            var gaps=new[]{new Rectangle(0,0,100,100),new Rectangle(200,0,100,100)};
            Assert(!Mapping.ToDesktop(.5,.5,new Rectangle(0,0,300,100),gaps,out p),"monitor gap rejected");
            Assert(Mapping.ToDesktop(.25,.25,union,monitors,out p)&&p.X==640,"cropped source UV must not be cropped twice");
            var filter=new GazeFilter();var a=new GazeSample{time=1,valid=true,origin=new Vec(),direction=new Vec(0,0,-1)};filter.Process(a,100);
            var b=new GazeSample{time=1.01,valid=true,direction=new Vec(.1,0,-1).Unit()};var c=filter.Process(b,100);
            Assert(c.direction.x>0&&c.direction.x<b.direction.x,"small jitter smoothed");
            Assert(Math.Abs(c.direction.Length-1)<1e-9,"filtered direction normalized");
            c=filter.Process(new GazeSample{time=1.02,valid=false,reason="blink"},100);Assert(!c.valid,"invalid immediately clears");
            b.time=1.03;c=filter.Process(b,100);Assert(Math.Abs(c.direction.x-b.direction.x)<1e-9,"reacquisition resets");
            b.direction=new Vec(0,0,-1);b.time=2;c=filter.Process(b,100);Assert(c.direction.x==0,"long gap resets");
            var matrix=new HmdMatrix34_t{m2=1,m5=1,m8=-1,m3=2,m7=3,m11=4};var local=new Vec(.3,.1,-2);var world=VrMath.Rotate(matrix,local)+VrMath.Position(matrix);var back=VrMath.InverseRotate(matrix,world-VrMath.Position(matrix));
            Assert((back-local).Length<1e-6,"head/world transforms under rotation and translation");
            Assert(Marshal.SizeOf(typeof(VREyeTrackingData_t))==28,"OpenVR eye structure ABI");
            Assert(Marshal.SizeOf(typeof(VRActiveActionSet_t))==32,"OpenVR action set ABI");
            var ss=new Settings{Distance=double.NaN,Opacity=2,SmoothingMs=-8};ss.Validate();Assert(ss.Distance==2&&ss.Opacity==1&&ss.SmoothingMs==0,"settings sanitized");
            var invalid=new Snapshot{valid=false,desktop=null};string json=new JavaScriptSerializer().Serialize(invalid);Assert(json.Contains("\"desktop\":null"),"invalid output never retains stale desktop");
            var check=new MappingCheck();check.Observe(new Snapshot{sequence=1,valid=false,reason="eye_not_tracked"});Assert(check.Result=="waiting_for_valid_eye_tracking","mapping check cannot pass without eye data");
            check.Observe(new Snapshot{sequence=2,valid=true,reason="valid"});Assert(check.Result=="eye_received_no_desktop_hit","valid eye alone is not desktop success");
            check.Observe(new Snapshot{sequence=3,valid=true,reason="valid",desktop=new DesktopOutput{x=20,y=-40}});Assert(check.Result=="pipeline_observed_accuracy_unverified"&&!check.humanAccuracyConfirmed,"desktop hit does not prove human accuracy");
            check.Observe(new Snapshot{sequence=3,valid=true});Assert(check.observations==3,"mapping check ignores duplicate frame observations");
            File.WriteAllText(Path.Combine(Files.Data,"self-test.txt"),"PASS "+count+" tests\n"+DateTime.UtcNow.ToString("O"));return 0;
        }
        public static int Inspect(){
            EVRInitError init=EVRInitError.None;OpenVR.Init(ref init,EVRApplicationType.VRApplication_Background);
            if(init!=EVRInitError.None)throw new Exception("Inspect: "+init);
            try {
                var list=new System.Collections.Generic.List<object>();
                foreach(string key in new[]{"local.steamgaze.raw","local.steamgaze.smooth","local.steamgaze.dashboard","elvissteinjr.DesktopPlus0"}){
                    ulong h=0;var found=OpenVR.Overlay.FindOverlay(key,ref h);bool shown=false;float width=0,alpha=0;uint w=0,height=0;var transform=(VROverlayTransformType)0;string texture="";
                    if(found==EVROverlayError.None){shown=OpenVR.Overlay.IsOverlayVisible(h);OpenVR.Overlay.GetOverlayWidthInMeters(h,ref width);OpenVR.Overlay.GetOverlayAlpha(h,ref alpha);OpenVR.Overlay.GetOverlayTransformType(h,ref transform);
                        if(key.StartsWith("local.steamgaze")){
                            var result=OpenVR.Overlay.GetOverlayImageData(h,IntPtr.Zero,0,ref w,ref height);texture=result.ToString();
                            if(w>0&&height>0&&w*height<4000000){int bytes=(int)(w*height*4);var ptr=Marshal.AllocHGlobal(bytes);try{
                                result=OpenVR.Overlay.GetOverlayImageData(h,ptr,(uint)bytes,ref w,ref height);texture=result.ToString();
                                if(result==EVROverlayError.None){var rgba=new byte[bytes];Marshal.Copy(ptr,rgba,0,bytes);for(int i=0;i<rgba.Length;i+=4){byte x=rgba[i];rgba[i]=rgba[i+2];rgba[i+2]=x;}
                                    using(var bmp=new Bitmap((int)w,(int)height,System.Drawing.Imaging.PixelFormat.Format32bppArgb)){
                                        var data=bmp.LockBits(new Rectangle(0,0,(int)w,(int)height),System.Drawing.Imaging.ImageLockMode.WriteOnly,System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                                        try{Marshal.Copy(rgba,0,data.Scan0,bytes);}finally{bmp.UnlockBits(data);}
                                        bmp.Save(Path.Combine(Files.Data,key+".png"));
                                    }
                                }
                            }finally{Marshal.FreeHGlobal(ptr);}}
                        }
                    }
                    list.Add(new{key,found=found.ToString(),handle=h,shown,width,alpha,transform=transform.ToString(),texture,w,height});
                }
                File.WriteAllText(Path.Combine(Files.Data,"inspect.json"),new JavaScriptSerializer().Serialize(new{scenePid=OpenVR.Applications.GetCurrentSceneProcessId(),overlays=list}));
            }finally{OpenVR.Shutdown();}return 0;
        }
        public static int Probe(){
            using(var source=new OpenVrSource()) {
                var watch=Stopwatch.StartNew();int total=0,valid=0;GazeSample last=null;var keys=DesktopPlusAdapter.Discover();
                while(watch.Elapsed.TotalSeconds<8){last=source.Read(watch.Elapsed.TotalSeconds);total++;if(last.valid)valid++;Thread.Sleep(11);}
                var surfaces=new System.Collections.Generic.List<object>();
                foreach(var k in keys){ulong h=0;OpenVR.Overlay.FindOverlay(k,ref h);var scale=new HmdVector2_t();var bounds=new VRTextureBounds_t();OpenVR.Overlay.GetOverlayMouseScale(h,ref scale);OpenVR.Overlay.GetOverlayTextureBounds(h,ref bounds);surfaces.Add(new{key=k,handle=h,visible=OpenVR.Overlay.IsOverlayVisible(h),scale,bounds});}
                var report=new{total,valid,lastReason=last.reason,lastDirection=last.direction.Array(),scenePid=OpenVR.Applications.GetCurrentSceneProcessId(),surfaces};
                File.WriteAllText(Path.Combine(Files.Data,"probe.json"),new JavaScriptSerializer().Serialize(report));
            }return 0;
        }
        public static int MappingProbe(){
            using(var source=new OpenVrSource()) {
                ulong overlay=0;OverlayRenderer.Check(OpenVR.Overlay.CreateOverlay("local.steamgaze.mappingtest","Steam Gaze geometry test",ref overlay));
                var report=new System.Collections.Generic.List<object>();int failures=0;
                try {
                    using(var bmp=new Bitmap(256,288))OverlayRenderer.Upload(overlay,bmp);
                    var scale=new HmdVector2_t{v0=2560,v1=2880};OverlayRenderer.Check(OpenVR.Overlay.SetOverlayMouseScale(overlay,ref scale));
                    // Intersection tests ignore hidden overlays; use a fully transparent visible test surface.
                    OverlayRenderer.Check(OpenVR.Overlay.SetOverlayAlpha(overlay,0));
                    OverlayRenderer.Check(OpenVR.Overlay.ShowOverlay(overlay));
                    Thread.Sleep(150);
                    foreach(float width in new[]{1f,2.5f})foreach(float curvature in new[]{0f,.17f})foreach(float angle in new[]{0f,.35f}) {
                        OverlayRenderer.Check(OpenVR.Overlay.SetOverlayWidthInMeters(overlay,width));OverlayRenderer.Check(OpenVR.Overlay.SetOverlayCurvature(overlay,curvature));
                        var pose=VrMath.Pose(new Vec(.3,1.2,-2));pose.m0=(float)Math.Cos(angle);pose.m2=(float)Math.Sin(angle);pose.m8=-(float)Math.Sin(angle);pose.m10=(float)Math.Cos(angle);
                        OverlayRenderer.Check(OpenVR.Overlay.SetOverlayTransformAbsolute(overlay,ETrackingUniverseOrigin.TrackingUniverseStanding,ref pose));
                        var bounds=new VRTextureBounds_t{uMin=.2f,uMax=.8f,vMin=.5f,vMax=1f};OverlayRenderer.Check(OpenVR.Overlay.SetOverlayTextureBounds(overlay,ref bounds));
                        Thread.Sleep(60);
                        foreach(var uv in new[]{new HmdVector2_t{v0=.5f,v1=.5f},new HmdVector2_t{v0=.05f,v1=.05f},new HmdVector2_t{v0=.95f,v1=.95f},new HmdVector2_t{v0=.05f,v1=.95f},new HmdVector2_t{v0=.95f,v1=.05f}}) {
                            double theta=(uv.v0-.5)*curvature*2*Math.PI;
                            double radius=curvature==0?0:width/(curvature*2*Math.PI);
                            Vec local=new Vec(curvature==0?(uv.v0-.5)*width:radius*Math.Sin(theta),(uv.v1-.5)*width*(288.0/256)*(.5/.6),curvature==0?0:radius*(1-Math.Cos(theta)));
                            Vec point=VrMath.Rotate(pose,local)+VrMath.Position(pose);Vec origin=VrMath.Rotate(pose,new Vec(0,0,2))+VrMath.Position(pose);Vec dir=(point-origin).Unit();
                            var pars=new VROverlayIntersectionParams_t{eOrigin=ETrackingUniverseOrigin.TrackingUniverseStanding,vSource=VrMath.V(origin),vDirection=VrMath.V(dir)};var result=new VROverlayIntersectionResults_t();
                            bool hit=OpenVR.Overlay.ComputeOverlayIntersection(overlay,ref pars,ref result);
                            double expectedU=.2+uv.v0*.6, expectedV=uv.v1*.5;
                            bool pass=hit&&Math.Abs(result.vUVs.v0-expectedU)<.003&&Math.Abs(result.vUVs.v1-expectedV)<.003;
                            if(!pass)failures++;
                            report.Add(new{width,curvature,angle,uv,point,hit,result.vUVs,expectedU,expectedV,pass});
                        }
                    }
                } finally {OpenVR.Overlay.DestroyOverlay(overlay);}
                File.WriteAllText(Path.Combine(Files.Data,"mapping-test.json"),new JavaScriptSerializer().Serialize(report));
                if(failures>0)throw new Exception("Mapping tests failed: "+failures);
            }return 0;
        }
    }
}
