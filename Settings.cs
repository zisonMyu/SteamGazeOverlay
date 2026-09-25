using System;
using System.IO;
using System.Web.Script.Serialization;

namespace SteamGaze {
    public sealed class Settings {
        public bool Enabled=true;
        public string Mode="Smooth";
        public double SmoothingMs=45;
        public double MarkerDegrees=0.65;
        public double Opacity=0.8;
        public double Distance=2.0;
        public string Color="#50E3C2";
        public bool DesktopEnabled=true;
        public bool DesktopMarker=false;
        public string OverlayKey="elvissteinjr.DesktopPlus0";
        public string DesktopMode="DesktopPlusFullDesktop";
        public string MonitorDevice="";
        public bool ExportEnabled=true;
        public bool AutoLaunch=false;
        public Settings Copy() { return (Settings)MemberwiseClone(); }
        public void Validate() {
            SmoothingMs=Clamp(SmoothingMs,0,200,45);MarkerDegrees=Clamp(MarkerDegrees,0.1,4,0.65);
            Opacity=Clamp(Opacity,0.05,1,0.8);Distance=Clamp(Distance,0.4,8,2);
            if(Mode!="Raw"&&Mode!="Both")Mode="Smooth";
            if(DesktopMode!="SingleMonitorTexture")DesktopMode="DesktopPlusFullDesktop";
            if(String.IsNullOrWhiteSpace(OverlayKey))OverlayKey="elvissteinjr.DesktopPlus0";
            try{System.Drawing.ColorTranslator.FromHtml(Color);}catch{Color="#50E3C2";}
        }
        static double Clamp(double v,double min,double max,double fallback) { return double.IsNaN(v)||double.IsInfinity(v)?fallback:Math.Max(min,Math.Min(max,v)); }
    }
    public static class Files {
        public static readonly string Root=AppDomain.CurrentDomain.BaseDirectory;
        public static string Data=Path.Combine(Root,"data");
        public static string LogPath;
        static readonly object gate=new object();
        public static void Init() { Directory.CreateDirectory(Data);LogPath=Path.Combine(Data,"session-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".log"); }
        public static Settings Load() {
            try { var s=new JavaScriptSerializer().Deserialize<Settings>(File.ReadAllText(Path.Combine(Data,"settings.json")));s.Validate();return s; }
            catch(Exception e) { Log("Settings defaults: "+e.Message);return new Settings(); }
        }
        public static void Save(Settings s) { AtomicWrite(Path.Combine(Data,"settings.json"),new JavaScriptSerializer().Serialize(s)); }
        public static void AtomicWrite(string path,string text) {
            string tmp=path+".tmp";File.WriteAllText(tmp,text,System.Text.Encoding.UTF8);
            if(File.Exists(path))File.Replace(tmp,path,null);else File.Move(tmp,path);
        }
        public static void Log(string message) { lock(gate) { if(LogPath!=null){if(File.Exists(LogPath)&&new FileInfo(LogPath).Length>4*1024*1024) {File.Copy(LogPath,LogPath+".previous",true);File.WriteAllText(LogPath,"");}File.AppendAllText(LogPath,DateTime.Now.ToString("O")+" "+message+Environment.NewLine);} } }
    }
}
