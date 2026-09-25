using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Runtime.InteropServices;

namespace SteamGaze {
    // Windows per-process high-resolution waitable timer; no timeBeginPeriod or compositor busy-wait.
    sealed class FrameClock : IDisposable {
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern IntPtr CreateWaitableTimerEx(IntPtr attributes,string name,uint flags,uint access);
        [DllImport("kernel32.dll",SetLastError=true)]static extern bool SetWaitableTimer(IntPtr timer,ref long due,int period,IntPtr routine,IntPtr arg,bool resume);
        [DllImport("kernel32.dll")]static extern uint WaitForSingleObject(IntPtr handle,uint milliseconds);
        [DllImport("kernel32.dll")]static extern bool CloseHandle(IntPtr handle);
        IntPtr handle;
        public FrameClock(){handle=CreateWaitableTimerEx(IntPtr.Zero,null,2,0x1F0003);if(handle==IntPtr.Zero)handle=CreateWaitableTimerEx(IntPtr.Zero,null,0,0x1F0003);}
        public void Wait(double ms){ms=Math.Max(.5,ms);if(handle==IntPtr.Zero){Thread.Sleep((int)Math.Ceiling(ms));return;}long due=-(long)(ms*10000);if(SetWaitableTimer(handle,ref due,0,IntPtr.Zero,IntPtr.Zero,false))WaitForSingleObject(handle,50);else Thread.Sleep(11);}
        public void Dispose(){if(handle!=IntPtr.Zero){CloseHandle(handle);handle=IntPtr.Zero;}}
    }
    public sealed class Snapshot {
        public int schemaVersion=1;
        public long sequence;
        public string utc;
        public double monotonicSeconds;
        public string timestampKind="host_poll_not_sensor";
        public string coordinateSpace="OpenVR_Standing_Metres";
        public bool valid;
        public string reason="waiting";
        public double[] rawOrigin,rawDirection,filteredDirection,headPosition;
        public string mappingInput;
        public DesktopOutput desktop;
        public string desktopStatus="等待 SteamVR";
        public double pollHz,validFraction,loopMs;
        public int scenePid;
        public string[] overlayKeys;
    }
    public sealed class DesktopOutput {
        public string target,monitor;
        public double u,v;
        public int x,y;
    }
    public sealed class NamedPipeOutput : IFrameSink {
        volatile bool stop; volatile string latest;
        Thread thread;NamedPipeServerStream pipe;object gate=new object();
        public NamedPipeOutput() {thread=new Thread(Run){IsBackground=true,Name="Gaze output pipe"};thread.Start();}
        public void Publish(string json) {latest=json;}
        void Run() {
            while(!stop)try {
                var acl=new PipeSecurity();acl.SetAccessRuleProtection(true,false);
                acl.AddAccessRule(new PipeAccessRule(WindowsIdentity.GetCurrent().User,PipeAccessRights.FullControl,AccessControlType.Allow));
                using(var p=new NamedPipeServerStream("SteamGazeOverlay.v1",PipeDirection.Out,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous,4096,4096,acl)) {
                    lock(gate){if(stop)return;pipe=p;}
                    p.WaitForConnection();
                    string previous=null;
                    while(!stop&&p.IsConnected) {
                        string value=latest;
                        if(value!=null&&value!=previous) {
                            byte[] bytes=Encoding.UTF8.GetBytes(value+"\n");
                            var task=p.WriteAsync(bytes,0,bytes.Length);
                            if(!task.Wait(500))break;
                            previous=value;
                        }
                        Thread.Sleep(33);
                    }
                }
            }catch(Exception e){if(!stop){Files.Log("Pipe: "+e.Message);Thread.Sleep(500);}}
        }
        public void Dispose(){stop=true;lock(gate){if(pipe!=null)pipe.Dispose();}thread.Join(1000);}
    }
    public sealed class Engine : IDisposable {
        volatile bool stop;Thread thread;readonly object gate=new object();Settings settings;
        Snapshot latest=new Snapshot();string[] keys=new string[0];int showDesktopRequested;
        public event Action SettingsChanged;
        public Engine(Settings s,bool start=true){settings=s.Copy();if(start){thread=new Thread(Run){IsBackground=true,Name="Steam Gaze runtime"};thread.Start();}}
        public Settings Config {get{lock(gate)return settings.Copy();}}
        public Snapshot Latest {get{lock(gate)return latest;}}
        public void RequestDesktopDashboard(){Interlocked.Exchange(ref showDesktopRequested,1);}
        public void Configure(Settings s){s.Validate();lock(gate)settings=s.Copy();Files.Save(s);}
        void DashboardCommand(string c) {
            var s=Config;
            if(c=="enabled")s.Enabled=!s.Enabled;
            if(c=="mode")s.Mode=s.Mode=="Smooth"?"Both":s.Mode=="Both"?"Raw":"Smooth";
            if(c=="smoothing")s.SmoothingMs=s.SmoothingMs<45?45:s.SmoothingMs<80?80:s.SmoothingMs<120?120:0;
            if(c=="desktopmarker")s.DesktopMarker=!s.DesktopMarker;
            Configure(s);if(SettingsChanged!=null)SettingsChanged();
        }
        void Set(Snapshot s){lock(gate)latest=s;}
        void Unavailable(NamedPipeOutput output,JavaScriptSerializer json,string reason,string message,double now){
            var state=new Snapshot{utc=DateTime.UtcNow.ToString("O"),monotonicSeconds=now,reason=reason,desktopStatus=message};
            Set(state);string serialized=json.Serialize(state);output.Publish(serialized);Files.AtomicWrite(Path.Combine(Files.Data,"status.json"),serialized);
        }
        static int ServerPid(){var processes=Process.GetProcessesByName("vrserver");try{return processes.Length==0?0:processes[0].Id;}finally{foreach(var process in processes)process.Dispose();}}
        void Run() {
            var clock=Stopwatch.StartNew();long seq=0;var json=new JavaScriptSerializer();int quittingServer=0;
            using(var pacing=new FrameClock())using(var output=new NamedPipeOutput())while(!stop) {
                OpenVrSource source=null;OverlayRenderer renderer=null;int serverPid=ServerPid();
                // VR_Init may launch SteamVR if called during shutdown. Wait for a different server instance after Quit.
                if(serverPid==0||serverPid==quittingServer){Unavailable(output,json,serverPid==0?"steamvr_not_running":"steamvr_shutting_down","请先启动 SteamVR",clock.Elapsed.TotalSeconds);Thread.Sleep(1000);continue;}
                try {
                    source=new OpenVrSource();renderer=new OverlayRenderer();renderer.Command=DashboardCommand;
                    var frequencyError=Valve.VR.ETrackedPropertyError.TrackedProp_Success;
                    double frequency=Valve.VR.OpenVR.System.GetFloatTrackedDeviceProperty(0,Valve.VR.ETrackedDeviceProperty.Prop_DisplayFrequency_Float,ref frequencyError);
                    if(frequencyError!=Valve.VR.ETrackedPropertyError.TrackedProp_Success||frequency<60||frequency>144)frequency=90;
                    Files.Log("Pacing target Hz="+frequency);
                    var filter=new GazeFilter();var adapter=new DesktopPlusAdapter(Config);
                    double last=clock.Elapsed.TotalSeconds,statsStart=last,nextSlow=last,nextExport=last,nextConfig=last;
                    int count=0,valid=0;double hz=0,ratio=0;bool auto=Config.AutoLaunch;source.SetAutoLaunch(auto);
                    string lastReason="";double desktopOpenDeadline=0;
                    while(!stop&&!source.ShouldQuit) {
                        double now=clock.Elapsed.TotalSeconds;var work=Stopwatch.StartNew();var s=Config;
                        if(now>=nextConfig){adapter.Configure(s);keys=DesktopPlusAdapter.Discover();source.ScenePid=(int)Valve.VR.OpenVR.Applications.GetCurrentSceneProcessId();nextConfig=now+1;}
                        if(auto!=s.AutoLaunch){source.SetAutoLaunch(s.AutoLaunch);auto=s.AutoLaunch;}
                        var raw=source.Read(now);
                        if(source.ShouldQuit){quittingServer=serverPid;break;}
                        if(Interlocked.Exchange(ref showDesktopRequested,0)!=0){
                            desktopOpenDeadline=now+15;ulong existingDashboard=0;
                            if(Valve.VR.OpenVR.Overlay.FindOverlay("elvissteinjr.DesktopPlusDashboard",ref existingDashboard)!=Valve.VR.EVROverlayError.None){
                                var launch=Valve.VR.OpenVR.Applications.LaunchDashboardOverlay("steam.overlay.1494460");
                                Files.Log("User requested Desktop+ launch: "+launch);
                                if(launch!=Valve.VR.EVRApplicationError.None)desktopOpenDeadline=0;
                            }
                        }
                        if(desktopOpenDeadline>now){ulong dashboard=0;if(Valve.VR.OpenVR.Overlay.FindOverlay("elvissteinjr.DesktopPlusDashboard",ref dashboard)==Valve.VR.EVROverlayError.None){Valve.VR.OpenVR.Overlay.ShowDashboard("elvissteinjr.DesktopPlusDashboard");desktopOpenDeadline=0;}}
                        if(now-last>0.25){raw.valid=false;raw.reason="poll_gap_reset";}
                        last=now;
                        var processed=filter.Process(raw,s.SmoothingMs);
                        var rawWorld=VrMath.World(raw,source.Head);var smoothWorld=VrMath.World(processed,source.Head);
                        var used=s.Mode=="Raw"?rawWorld:smoothWorld;
                        DesktopHit hit=adapter.Intersect(used);
                        renderer.Render(s,rawWorld,smoothWorld,source.Head,hit);
                        count++;if(raw.valid)valid++;
                        if(now-statsStart>=1){hz=count/(now-statsStart);ratio=valid/(double)count;statsStart=now;count=valid=0;}
                        var frame=new Snapshot{sequence=++seq,utc=DateTime.UtcNow.ToString("O"),monotonicSeconds=now,valid=raw.valid,reason=raw.reason,
                            rawOrigin=raw.valid?rawWorld.origin.Array():null,rawDirection=raw.valid?rawWorld.direction.Array():null,filteredDirection=processed.valid?smoothWorld.direction.Array():null,
                            headPosition=raw.valid?VrMath.Position(source.Head).Array():null,mappingInput=s.Mode=="Raw"?"raw":"filtered",desktopStatus=adapter.Status,
                            desktop=hit==null?null:new DesktopOutput{target=hit.target,monitor=hit.monitor,u=hit.u,v=hit.v,x=hit.x,y=hit.y},pollHz=hz,validFraction=ratio,loopMs=work.Elapsed.TotalMilliseconds,scenePid=source.ScenePid,overlayKeys=keys};
                        Set(frame);
                        if(frame.reason!=lastReason){Files.Log("Gaze: "+frame.reason);lastReason=frame.reason;}
                        if(now>=nextExport){output.Publish(s.ExportEnabled?json.Serialize(frame):"{\"schemaVersion\":1,\"valid\":false,\"reason\":\"export_disabled\",\"desktop\":null}");nextExport=now+1.0/30;}
                        if(now>=nextSlow){renderer.UpdateDashboard(frame,s);Files.AtomicWrite(Path.Combine(Files.Data,"status.json"),json.Serialize(frame));nextSlow=now+0.5;}
                        pacing.Wait(1000.0/frequency-work.Elapsed.TotalMilliseconds);
                    }
                }catch(Exception e){Files.Log(e.ToString());if(e.Message.Contains("Init_ShuttingDown"))quittingServer=serverPid;}
                finally {
                    if(renderer!=null)renderer.Dispose();if(source!=null)source.Dispose();
                    Unavailable(output,json,stop?"stopped":"runtime_disconnected",stop?"已停止":"等待 SteamVR 重新连接",clock.Elapsed.TotalSeconds);
                }
                if(!stop)Thread.Sleep(1500);
            }
        }
        public void Dispose(){stop=true;if(thread!=null&&!thread.Join(5000))Files.Log("Runtime thread did not stop within five seconds");try{Files.AtomicWrite(Path.Combine(Files.Data,"status.json"),"{\"schemaVersion\":1,\"valid\":false,\"reason\":\"stopped\",\"desktop\":null}");}catch{}}
    }
}
