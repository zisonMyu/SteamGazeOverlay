using System;
using System.IO;
using System.Drawing;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Valve.VR;

namespace SteamGaze {
    public static class VrMath {
        public static Vec V(HmdVector3_t v) { return new Vec(v.v0,v.v1,v.v2); }
        public static HmdVector3_t V(Vec v) { return new HmdVector3_t{v0=(float)v.x,v1=(float)v.y,v2=(float)v.z}; }
        public static Vec Rotate(HmdMatrix34_t m,Vec v) { return new Vec(m.m0*v.x+m.m1*v.y+m.m2*v.z,m.m4*v.x+m.m5*v.y+m.m6*v.z,m.m8*v.x+m.m9*v.y+m.m10*v.z); }
        public static Vec InverseRotate(HmdMatrix34_t m,Vec v) { return new Vec(m.m0*v.x+m.m4*v.y+m.m8*v.z,m.m1*v.x+m.m5*v.y+m.m9*v.z,m.m2*v.x+m.m6*v.y+m.m10*v.z); }
        public static Vec Position(HmdMatrix34_t m) { return new Vec(m.m3,m.m7,m.m11); }
        public static HmdMatrix34_t Pose(Vec p) { return new HmdMatrix34_t{m0=1,m5=1,m10=1,m3=(float)p.x,m7=(float)p.y,m11=(float)p.z}; }
        public static GazeSample World(GazeSample s,HmdMatrix34_t m) { return new GazeSample{time=s.time,valid=s.valid,reason=s.reason,origin=Rotate(m,s.origin)+Position(m),direction=Rotate(m,s.direction).Unit()}; }
    }
    public sealed class OpenVrSource : IGazeSource {
        ulong action,actionSet;VRActiveActionSet_t[] sets=new VRActiveActionSet_t[1];
        TrackedDevicePose_t[] poses=new TrackedDevicePose_t[OpenVR.k_unMaxTrackedDeviceCount];
        public HmdMatrix34_t Head;
        public bool Connected;
        public string Error="Waiting for SteamVR";
        public bool ShouldQuit;
        public int ScenePid;
        public uint GazeSize=(uint)Marshal.SizeOf(typeof(VREyeTrackingData_t));
        public OpenVrSource() {
            EVRInitError err=EVRInitError.None;
            OpenVR.Init(ref err,EVRApplicationType.VRApplication_Overlay);
            if(err!=EVRInitError.None)throw new Exception("SteamVR: "+err);
            Connected=true;
            try {
                string manifest=Path.Combine(Files.Root,"steamgaze.vrmanifest");
                string exe=Path.Combine(Files.Root,"SteamGazeOverlay.exe").Replace("\\","/");
                Files.AtomicWrite(manifest,"{\"source\":\"builtin\",\"applications\":[{\"app_key\":\"local.steamgaze.overlay\",\"launch_type\":\"binary\",\"binary_path_windows\":\""+exe+"\",\"arguments\":\"--tray\",\"is_dashboard_overlay\":true,\"strings\":{\"en_us\":{\"name\":\"Steam Gaze\",\"description\":\"Eye gaze overlay and desktop mapping\"}}}]}");
                var appErr=OpenVR.Applications.AddApplicationManifest(manifest,false);
                if(appErr!=EVRApplicationError.None)throw new Exception("Register manifest: "+appErr);
                Check(OpenVR.Input.SetActionManifestPath(Path.Combine(Files.Root,"actions.json")));
                Check(OpenVR.Input.GetActionSetHandle("/actions/gaze",ref actionSet));
                Check(OpenVR.Input.GetActionHandle("/actions/gaze/in/eyes",ref action));
                sets[0].ulActionSet=actionSet;sets[0].nPriority=OpenVR.k_nActionSetOverlayGlobalPriorityMin+1;
                Files.Log("OpenVR connected as Overlay; eye struct bytes="+GazeSize);
            }catch {Dispose();throw;}
        }
        static void Check(EVRInputError e) { if(e!=EVRInputError.None)throw new Exception("OpenVR input: "+e); }
        public GazeSample Read(double now) {
            var s=new GazeSample{time=now,valid=false,reason="tracking_invalid"};
            var evt=new VREvent_t();
            while(OpenVR.System.PollNextEvent(ref evt,(uint)Marshal.SizeOf(typeof(VREvent_t)))) {
                if(evt.eventType==(uint)EVREventType.VREvent_Quit) {ShouldQuit=true;return s;}
            }
            OpenVR.System.GetDeviceToAbsoluteTrackingPose(ETrackingUniverseOrigin.TrackingUniverseStanding,0,poses);
            if(!poses[0].bDeviceIsConnected||!poses[0].bPoseIsValid) {s.reason="headset_pose_invalid";return s;}
            Head=poses[0].mDeviceToAbsoluteTracking;
            var state=OpenVR.Input.UpdateActionState(sets,(uint)Marshal.SizeOf(typeof(VRActiveActionSet_t)));
            if(state!=EVRInputError.None) {s.reason="action_state_"+state;return s;}
            var eyes=new VREyeTrackingData_t();
            state=OpenVR.Input.GetEyeTrackingDataRelativeToNow(action,ETrackingUniverseOrigin.TrackingUniverseStanding,0,ref eyes,GazeSize);
            if(state!=EVRInputError.None) {s.reason="eye_input_"+state;return s;}
            if(!eyes.bActive||!eyes.bValid||!eyes.bTracked) {s.reason="eye_not_tracked";return s;}
            Vec origin=VrMath.V(eyes.vGazeOrigin),direction=VrMath.V(eyes.vGazeTarget)-origin;
            if(!origin.Finite||!direction.Finite||direction.Length<0.0001) {s.reason="invalid_numeric_data";return s;}
            // Filter eye direction in head space, so smoothing does not make the marker lag head motion.
            s.origin=VrMath.InverseRotate(Head,origin-VrMath.Position(Head));
            s.direction=VrMath.InverseRotate(Head,direction.Unit()).Unit();s.valid=true;s.reason="valid";
            return s;
        }
        public void SetAutoLaunch(bool on) {
            var e=OpenVR.Applications.SetApplicationAutoLaunch("local.steamgaze.overlay",on);
            if(e!=EVRApplicationError.None)throw new Exception("SteamVR autostart: "+e);
        }
        public void Dispose() { if(Connected) {OpenVR.Shutdown();Connected=false;} }
    }
    public sealed class DesktopPlusAdapter : IDesktopSurfaceAdapter {
        Settings settings; Rectangle[] monitors;string[] names;Rectangle desktop;
        public string Status {get;private set;}
        public ulong Handle;
        public HmdVector2_t MouseScale;
        public VRTextureBounds_t Bounds;
        public DesktopPlusAdapter(Settings s) { Configure(s); }
        public void Configure(Settings s) {
            settings=s;var screens=Screen.AllScreens;monitors=new Rectangle[screens.Length];names=new string[screens.Length];desktop=Rectangle.Empty;
            for(int i=0;i<screens.Length;i++){monitors[i]=screens[i].Bounds;names[i]=screens[i].DeviceName;desktop=i==0?monitors[i]:Rectangle.Union(desktop,monitors[i]);}
        }
        public Rectangle TextureRect {
            get {if(settings.DesktopMode=="SingleMonitorTexture")for(int i=0;i<names.Length;i++)if(names[i]==settings.MonitorDevice)return monitors[i];return desktop;}
        }
        public DesktopHit Intersect(GazeSample gaze) {
            Handle=0;
            if(!settings.DesktopEnabled){Status="桌面映射已关闭";return null;}
            if(OpenVR.Overlay.FindOverlay(settings.OverlayKey,ref Handle)!=EVROverlayError.None){Status="未找到桌面目标；检查 Desktop+ 是否已退出及所选目标";return null;}
            if(!OpenVR.Overlay.IsOverlayVisible(Handle)){Status="桌面已隐藏：请在 Dashboard 切到 Desktop+ 标签页";return null;}
            float alpha=0;
            if(OpenVR.Overlay.GetOverlayAlpha(Handle,ref alpha)!=EVROverlayError.None||alpha<0.05){Status="目标 Overlay 透明，停止映射";return null;}
            var scale=new HmdVector2_t();var bounds=new VRTextureBounds_t();
            if(OpenVR.Overlay.GetOverlayMouseScale(Handle,ref scale)!=EVROverlayError.None || OpenVR.Overlay.GetOverlayTextureBounds(Handle,ref bounds)!=EVROverlayError.None){Status="无法读取目标尺寸";return null;}
            MouseScale=scale;Bounds=bounds;
            Rectangle rect=TextureRect;
            if(settings.DesktopMode=="SingleMonitorTexture"&&Array.IndexOf(names,settings.MonitorDevice)<0){Status="所选显示器已断开";return null;}
            bool stereo=false;
            OpenVR.Overlay.GetOverlayFlag(Handle,VROverlayFlags.SideBySide_Parallel,ref stereo);
            if(stereo){Status="暂不支持桌面 SBS 纹理映射";return null;}
            OpenVR.Overlay.GetOverlayFlag(Handle,VROverlayFlags.SideBySide_Crossed,ref stereo);
            if(stereo){Status="暂不支持桌面 SBS 纹理映射";return null;}
            if(Math.Abs(scale.v0-rect.Width)>1||Math.Abs(scale.v1-rect.Height)>1) {Status=String.Format("纹理 {0}×{1} 与桌面 {2}×{3} 不匹配；请选择正确来源",scale.v0,scale.v1,rect.Width,rect.Height);return null;}
            if(!gaze.valid){Status="桌面就绪，等待有效眼动";return null;}
            var p=new VROverlayIntersectionParams_t{vSource=VrMath.V(gaze.origin),vDirection=VrMath.V(gaze.direction),eOrigin=ETrackingUniverseOrigin.TrackingUniverseStanding};
            var hit=new VROverlayIntersectionResults_t();
            if(!OpenVR.Overlay.ComputeOverlayIntersection(Handle,ref p,ref hit)||hit.fDistance<=0||Vec.Dot(VrMath.V(hit.vNormal),gaze.direction)>=0){Status="有效眼动 · 未命中桌面";return null;}
            Point pixel;
            if(!Mapping.ToDesktop(hit.vUVs.v0,hit.vUVs.v1,rect,monitors,out pixel)){Status="落在显示器间隙或桌面外";return null;}
            string device="";for(int i=0;i<monitors.Length;i++)if(monitors[i].Contains(pixel))device=names[i];
            Status=String.Format("命中 {0} · ({1}, {2})",device,pixel.X,pixel.Y);
            return new DesktopHit{target=settings.OverlayKey,monitor=device,u=hit.vUVs.v0,v=1-hit.vUVs.v1,x=pixel.X,y=pixel.Y,point=VrMath.V(hit.vPoint),distance=hit.fDistance};
        }
        public static string[] Discover() {
            var keys=new List<string>();
            for(int i=0;i<64;i++){ulong h=0;string k="elvissteinjr.DesktopPlus"+i;if(OpenVR.Overlay.FindOverlay(k,ref h)==EVROverlayError.None)keys.Add(k);}
            return keys.ToArray();
        }
    }
}
