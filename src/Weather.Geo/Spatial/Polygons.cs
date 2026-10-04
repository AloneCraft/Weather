using Weather.Core;
using Weather.Geo.Data;

namespace Weather.Geo.Spatial;

/// <summary>多角形の内外判定と距離(小領域のため局所的な正距円筒近似を使う)。</summary>
public static class Polygons{
    /// <summary>偶奇規則による内外判定(穴の輪も同じ規則で扱える)。</summary>
    public static bool Contains(GeoShape shape,double lat,double lon){
        ArgumentNullException.ThrowIfNull(shape);
        if(!shape.BoxContains(lat,lon)){
            return false;
        }
        var inside=false;
        foreach(var ring in shape.Rings){
            var n=ring.Length/2;
            for(int i=0,j=n-1;i<n;j=i++){
                double yi=ring[i*2];
                double xi=ring[i*2+1];
                double yj=ring[j*2];
                double xj=ring[j*2+1];
                if((yi>lat)!=(yj>lat)&&lon<(xj-xi)*(lat-yi)/(yj-yi)+xi){
                    inside=!inside;
                }
            }
        }
        return inside;
    }

    /// <summary>点から多角形の境界までの最短距離(km)。内側なら 0。</summary>
    public static double DistanceKm(GeoShape shape,double lat,double lon){
        ArgumentNullException.ThrowIfNull(shape);
        if(Contains(shape,lat,lon)){
            return 0;
        }
        var kx=GeoMath.EarthRadiusKm*Math.PI/180*Math.Cos(GeoMath.DegreesToRadians(lat));
        var ky=GeoMath.EarthRadiusKm*Math.PI/180;
        var best=double.MaxValue;
        foreach(var ring in shape.Rings){
            var n=ring.Length/2;
            for(int i=0,j=n-1;i<n;j=i++){
                var ax=(ring[j*2+1]-lon)*kx;
                var ay=(ring[j*2]-lat)*ky;
                var bx=(ring[i*2+1]-lon)*kx;
                var by=(ring[i*2]-lat)*ky;
                var d=SegmentDistance(ax,ay,bx,by);
                if(d<best){
                    best=d;
                }
            }
        }
        return best;
    }

    /// <summary>外接矩形までの距離(km)。候補の絞り込みに使う。</summary>
    public static double BoxDistanceKm(GeoShape shape,double lat,double lon){
        ArgumentNullException.ThrowIfNull(shape);
        var clampedLat=Math.Clamp(lat,shape.MinLat,shape.MaxLat);
        var clampedLon=Math.Clamp(lon,shape.MinLon,shape.MaxLon);
        return GeoMath.HaversineKm(lat,lon,clampedLat,clampedLon);
    }

    /// <summary>原点から線分 AB までの距離。</summary>
    private static double SegmentDistance(double ax,double ay,double bx,double by){
        var dx=bx-ax;
        var dy=by-ay;
        var lengthSquared=dx*dx+dy*dy;
        double t=0;
        if(lengthSquared>0){
            t=Math.Clamp(-(ax*dx+ay*dy)/lengthSquared,0,1);
        }
        var px=ax+t*dx;
        var py=ay+t*dy;
        return Math.Sqrt(px*px+py*py);
    }

    /// <summary>Douglas-Peucker による輪の簡略化(GeoDataBuilder 用)。</summary>
    public static float[] Simplify(float[] ring,double tolerance){
        ArgumentNullException.ThrowIfNull(ring);
        var n=ring.Length/2;
        if(n<=4||tolerance<=0){
            return ring;
        }
        var keep=new bool[n];
        keep[0]=true;
        keep[n-1]=true;
        var stack=new Stack<(int,int)>();
        stack.Push((0,n-1));
        while(stack.Count>0){
            var (first,last)=stack.Pop();
            var maxDistance=0d;
            var index=-1;
            for(var i=first+1;i<last;i++){
                var d=PerpendicularDistance(ring,i,first,last);
                if(d>maxDistance){
                    maxDistance=d;
                    index=i;
                }
            }
            if(index>=0&&maxDistance>tolerance){
                keep[index]=true;
                stack.Push((first,index));
                stack.Push((index,last));
            }
        }
        var result=new List<float>();
        for(var i=0;i<n;i++){
            if(keep[i]){
                result.Add(ring[i*2]);
                result.Add(ring[i*2+1]);
            }
        }
        if(result.Count<8){
            return ring;
        }
        return [..result];
    }

    private static double PerpendicularDistance(float[] ring,int i,int a,int b){
        double y=ring[i*2];
        double x=ring[i*2+1];
        double ya=ring[a*2];
        double xa=ring[a*2+1];
        double yb=ring[b*2];
        double xb=ring[b*2+1];
        var dx=xb-xa;
        var dy=yb-ya;
        var length=Math.Sqrt(dx*dx+dy*dy);
        if(length==0){
            return Math.Sqrt((x-xa)*(x-xa)+(y-ya)*(y-ya));
        }
        return Math.Abs(dy*x-dx*y+xb*ya-yb*xa)/length;
    }
}
