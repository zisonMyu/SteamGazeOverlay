using System;
using System.Drawing;

namespace SteamGaze {
    // All adapters exchange metres, right-handed OpenVR coordinates and monotonic seconds.
    public struct Vec {
        public double x, y, z;
        public Vec(double X,double Y,double Z) { x=X;y=Y;z=Z; }
        public static Vec operator +(Vec a,Vec b) { return new Vec(a.x+b.x,a.y+b.y,a.z+b.z); }
        public static Vec operator -(Vec a,Vec b) { return new Vec(a.x-b.x,a.y-b.y,a.z-b.z); }
        public static Vec operator *(Vec a,double b) { return new Vec(a.x*b,a.y*b,a.z*b); }
        public double Length { get { return Math.Sqrt(Dot(this,this)); } }
        public bool Finite { get { return !(double.IsNaN(x)||double.IsNaN(y)||double.IsNaN(z)||double.IsInfinity(x)||double.IsInfinity(y)||double.IsInfinity(z)); } }
        public Vec Unit() { double n=Length; return n>1e-9 ? this*(1/n) : new Vec(); }
        public static double Dot(Vec a,Vec b) { return a.x*b.x+a.y*b.y+a.z*b.z; }
        public double[] Array() { return new [] {x,y,z}; }
    }
    public sealed class GazeSample {
        public double time;
        public bool valid;
        public string reason;
        public Vec origin, direction;
    }
    public sealed class DesktopHit {
        public string target, monitor;
        public double u,v,distance;
        public int x,y;
        public Vec point;
    }
    public interface IGazeSource : IDisposable { GazeSample Read(double now); }
    public interface IGazeProcessor { GazeSample Process(GazeSample raw,double smoothingMs); void Reset(); }
    public interface IDesktopSurfaceAdapter { DesktopHit Intersect(GazeSample gaze); string Status {get;} }
    public interface IFrameSink : IDisposable { void Publish(string json); }

    public sealed class GazeFilter : IGazeProcessor {
        Vec previous; double last; bool initialized;
        public void Reset() { initialized=false; }
        public GazeSample Process(GazeSample raw,double smoothingMs) {
            if(!raw.valid || !raw.direction.Finite || raw.direction.Length<0.01) { Reset();return new GazeSample{time=raw.time,valid=false,reason=raw.reason ?? "invalid"}; }
            double dt=raw.time-last;
            if(!initialized || dt<=0 || dt>0.25 || smoothingMs<=0) previous=raw.direction.Unit();
            else {
                // A time-based EMA, not a frame-count average; preserve quick intentional saccades.
                double angle=Math.Acos(Math.Max(-1,Math.Min(1,Vec.Dot(previous,raw.direction.Unit()))))*180/Math.PI;
                double tau=smoothingMs/1000.0;
                if(angle>12) tau=Math.Min(tau,0.012);
                double alpha=1-Math.Exp(-dt/Math.Max(0.001,tau));
                previous=(previous*(1-alpha)+raw.direction.Unit()*alpha).Unit();
            }
            initialized=true;last=raw.time;
            return new GazeSample{time=raw.time,valid=true,reason="valid",origin=raw.origin,direction=previous};
        }
    }
    public static class Mapping {
        // ComputeOverlayIntersection returns source-texture UV, V increasing upwards.
        // Bounds already affect the returned UV: applying crop again would double-crop.
        public static bool ToDesktop(double u,double v,Rectangle textureRect,Rectangle[] monitors,out Point p) {
            p=Point.Empty;
            if(double.IsNaN(u)||double.IsNaN(v)||u<0||u>1||v<0||v>1||textureRect.Width<=0||textureRect.Height<=0) return false;
            p=new Point(textureRect.Left+Math.Min(textureRect.Width-1,(int)Math.Floor(u*textureRect.Width)),
                        textureRect.Top+Math.Min(textureRect.Height-1,(int)Math.Floor((1-v)*textureRect.Height)));
            foreach(var r in monitors) if(r.Contains(p)) return true;
            return false;
        }
    }
}
