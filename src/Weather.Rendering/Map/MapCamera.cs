using SkiaSharp;
using Weather.Core;

namespace Weather.Rendering.Map;

/// <summary>カメラの状態(描画の 1 フレームで使う値。描画スレッドへ渡すために不変にする)。</summary>
public readonly record struct MapView(double CenterX,double CenterY,double Zoom,float PixelRatio){
    /// <summary>世界(Web メルカトルの 0〜1)の 1 単位が何画素か。</summary>
    public double WorldSize=>MapCamera.TileSize*Math.Pow(2,this.Zoom)*this.PixelRatio;

    /// <summary>画面の左上が世界のどこか。</summary>
    public (double X,double Y) Origin(SKSize size){
        var s=this.WorldSize;
        return (this.CenterX-size.Width/2/s,this.CenterY-size.Height/2/s);
    }

    /// <summary>緯度経度 → 画面。経度方向は中心に最も近い複製を選ぶ。</summary>
    public SKPoint ToScreen(double latitude,double longitude,SKSize size){
        var (x,y)=MapCamera.Project(latitude,longitude);
        var dx=x-this.CenterX;
        dx-=Math.Round(dx);
        var s=this.WorldSize;
        return new SKPoint((float)(size.Width/2+dx*s),(float)(size.Height/2+(y-this.CenterY)*s));
    }

    public GeoPoint ToGeo(SKPoint point,SKSize size){
        var s=this.WorldSize;
        var x=this.CenterX+(point.X-size.Width/2)/s;
        var y=this.CenterY+(point.Y-size.Height/2)/s;
        return MapCamera.Unproject(x,y);
    }

    /// <summary>世界の座標 → 画面の行列(wrap は経度方向の複製の番号)。</summary>
    public SKMatrix WorldToScreen(SKSize size,int wrap){
        var s=this.WorldSize;
        var (ox,oy)=this.Origin(size);
        return SKMatrix.CreateScaleTranslation((float)s,(float)s,(float)((wrap-ox)*s),(float)(-oy*s));
    }
}

/// <summary>
/// 地図のカメラ(Web メルカトル。気象庁の XYZ タイルと重ねるため)。世界の座標は経度 −180〜180° が x 0〜1、北が y 0。
/// 操作は UI のスレッド、描画は描画スレッドで行うため、描画では Snapshot() の値を使う。
/// </summary>
public sealed class MapCamera{
    public const double TileSize=256;
    public const double MinZoom=1.5;
    public const double MaxZoom=11;
    public const double MaxLatitude=85.05112878;

    private readonly Lock gate=new();
    private double centerX;
    private double centerY;
    private double zoom;
    private float pixelRatio=1;

    public MapCamera(){
        this.CenterOn(new GeoPoint(36,138),4.5);
    }

    /// <summary>変更のたびに増える(粒子の軌跡を消す判断に使う)。</summary>
    public int Version{get;private set;}

    public float PixelRatio{
        get{
            lock(this.gate){
                return this.pixelRatio;
            }
        }
        set{
            lock(this.gate){
                this.pixelRatio=Math.Max(0.5f,value);
                this.Version++;
            }
        }
    }

    public MapView Snapshot(){
        lock(this.gate){
            return new MapView(this.centerX,this.centerY,this.zoom,this.pixelRatio);
        }
    }

    public static (double X,double Y) Project(double latitude,double longitude){
        var lat=Math.Clamp(latitude,-MaxLatitude,MaxLatitude)*Math.PI/180;
        var x=(longitude+180)/360;
        var y=(1-Math.Log(Math.Tan(lat)+1/Math.Cos(lat))/Math.PI)/2;
        return (x,y);
    }

    public static GeoPoint Unproject(double x,double y){
        var lon=(x-Math.Floor(x))*360-180;
        var n=Math.PI*(1-2*y);
        var lat=Math.Atan(Math.Sinh(n))*180/Math.PI;
        return new GeoPoint(Math.Clamp(lat,-90,90),lon);
    }

    public void CenterOn(GeoPoint point,double? newZoom=null){
        var (x,y)=Project(point.Latitude,point.Longitude);
        lock(this.gate){
            this.centerX=x;
            this.centerY=y;
            if(newZoom is {} z){
                this.zoom=Math.Clamp(z,MinZoom,MaxZoom);
            }
            this.Version++;
        }
    }

    /// <summary>画面上で dx・dy 画素だけ動かす(地図を指でずらす向き)。</summary>
    public void Pan(double dx,double dy){
        lock(this.gate){
            var s=TileSize*Math.Pow(2,this.zoom)*this.pixelRatio;
            this.centerX-=dx/s;
            this.centerX-=Math.Floor(this.centerX);
            this.centerY=Math.Clamp(this.centerY-dy/s,0,1);
            this.Version++;
        }
    }

    /// <summary>焦点(画面座標)の地点を保ったまま拡大縮小する。</summary>
    public void ZoomBy(double factor,SKPoint focus,SKSize size){
        if(!(factor>0)){
            return;
        }
        lock(this.gate){
            var before=TileSize*Math.Pow(2,this.zoom)*this.pixelRatio;
            var fx=this.centerX+(focus.X-size.Width/2)/before;
            var fy=this.centerY+(focus.Y-size.Height/2)/before;
            this.zoom=Math.Clamp(this.zoom+Math.Log2(factor),MinZoom,MaxZoom);
            var after=TileSize*Math.Pow(2,this.zoom)*this.pixelRatio;
            this.centerX=fx-(focus.X-size.Width/2)/after;
            this.centerX-=Math.Floor(this.centerX);
            this.centerY=Math.Clamp(fy-(focus.Y-size.Height/2)/after,0,1);
            this.Version++;
        }
    }
}
