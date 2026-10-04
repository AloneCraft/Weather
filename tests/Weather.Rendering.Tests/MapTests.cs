using SkiaSharp;
using Weather.Core;
using Weather.Geo;
using Weather.Rendering.Map;

namespace Weather.Rendering.Tests;

/// <summary>地図のテスト用の格子(GFS の形: 1°、北端・経度 0 から)。日本周辺は Providers と同じく NaN にする。</summary>
internal static class MapSamples{
    public static readonly GeoDatabase Database=GeoDatabase.LoadEmbedded();
    public static readonly DateTimeOffset Time=new(2026,10,4,3,0,0,TimeSpan.Zero);
    private static readonly Geo.JapanArea Japan=new(Database);
    private static readonly SourceAttribution Gfs=new(){Provider=ProviderId.Gfs,AgencyName="NOAA",ProductName="GFS 1°",IssuedAt=Time.AddHours(-3),RetrievedAt=Time,License=new LicenseInfo("PD",null),Processing=DataProcessing.Interpolated};

    public static GridField Field(FieldQuantity quantity,Func<double,double,double> value){
        var geometry=new GridGeometry(360,181,90,0,1,1);
        var values=new float[geometry.Count];
        for(var r=0;r<geometry.Rows;r++){
            var lat=geometry.LatitudeOf(r);
            for(var c=0;c<geometry.Columns;c++){
                var lon=geometry.LongitudeOf(c);
                if(lon>180){
                    lon-=360;
                }
                if(Japan.Contains(lat,lon)){
                    values[r*geometry.Columns+c]=float.NaN;
                }else{
                    values[r*geometry.Columns+c]=(float)value(lat,lon);
                }
            }
        }
        return new GridField(geometry,values,quantity,Time,Time.AddHours(-3),Gfs);
    }

    public static GridField Temperature()=>Field(FieldQuantity.TemperatureC,static (lat,_)=>32-0.65*Math.Abs(lat));

    public static WindField Wind(){
        var u=Field(FieldQuantity.WindU,static (lat,lon)=>12*Math.Cos(lat*Math.PI/45)+3*Math.Sin(lon*Math.PI/30));
        var v=Field(FieldQuantity.WindV,static (lat,lon)=>6*Math.Sin(lon*Math.PI/40));
        return new WindField(u,v);
    }

    public static GridField Pressure()=>Field(FieldQuantity.PressureHpa,static (lat,lon)=>1012+14*Math.Sin(lat*Math.PI/30)*Math.Cos(lon*Math.PI/50));

    /// <summary>気象庁の海上分布予報から作る海上の風の代わり(0.5°、日本の陸上は NaN、海上は一様な西風)。</summary>
    public static WindField JapanWind(){
        var geometry=new GridGeometry(81,61,50,120,0.5,0.5);
        var u=new float[geometry.Count];
        var v=new float[geometry.Count];
        for(var r=0;r<geometry.Rows;r++){
            for(var c=0;c<geometry.Columns;c++){
                var i=r*geometry.Columns+c;
                if(Database.FindCountry(geometry.LatitudeOf(r),geometry.LongitudeOf(c)) is not null){
                    u[i]=float.NaN;
                    v[i]=float.NaN;
                }else{
                    u[i]=8;
                    v[i]=0;
                }
            }
        }
        var source=new SourceAttribution{Provider=ProviderId.Jma,AgencyName="気象庁",ProductName="海上分布予報(風向・風速)",RetrievedAt=Time,License=new LicenseInfo("t",null),Processing=DataProcessing.UnitConverted};
        return new WindField(new GridField(geometry,u,FieldQuantity.WindU,Time,Time,source),new GridField(geometry,v,FieldQuantity.WindV,Time,Time,source));
    }

    /// <summary>描画したビットマップの、緯度経度の位置の画素。</summary>
    public static SKColor PixelAt(SKBitmap bitmap,Map.MapRenderer map,double lat,double lon){
        var p=map.Camera.Snapshot().ToScreen(lat,lon,new SKSize(bitmap.Width,bitmap.Height));
        return bitmap.GetPixel((int)p.X,(int)p.Y);
    }

    public static SKBitmap Render(Map.MapRenderer map,int width,int height,int frames=1){
        var bitmap=new SKBitmap(width,height);
        using var canvas=new SKCanvas(bitmap);
        for(var i=0;i<frames;i++){
            map.Render(canvas,new SKSizeI(width,height),i/30.0);
        }
        return bitmap;
    }

    /// <summary>気象庁のタイルの代わり: ズーム 4・(14, 6) のタイルで、日本の陸地の画素だけ赤にしたもの(メルカトルの位置合わせの確認用)。</summary>
    public static byte[] JapanLandTile(){
        using var bitmap=new SKBitmap(new SKImageInfo(256,256,SKColorType.Rgba8888,SKAlphaType.Premul));
        bitmap.Erase(SKColors.Transparent);
        for(var py=0;py<256;py+=1){
            for(var px=0;px<256;px+=1){
                var g=Map.MapCamera.Unproject((14+(px+0.5)/256)/16,(6+(py+0.5)/256)/16);
                if(Database.FindCountry(g.Latitude,g.Longitude)?.Iso2=="JP"){
                    bitmap.SetPixel(px,py,SKColors.Red);
                }
            }
        }
        using var image=SKImage.FromBitmap(bitmap);
        using var data=image.Encode(SKEncodedImageFormat.Png,100);
        return data.ToArray();
    }

    public static JmaTileLayer TileLayer(){
        return new JmaTileLayer{
            Product=JmaTileProduct.Nowcast,
            Element="hrpns",
            BaseTime=Time,
            ValidTime=Time,
            MinZoom=4,
            MaxZoom=4,
            EvenZoomOnly=true,
            Pixelated=true,
            Legend=MapLegends.RainRate,
            Source=new SourceAttribution{Provider=ProviderId.Jma,AgencyName="気象庁",ProductName="test",RetrievedAt=Time,License=new LicenseInfo("t",null)},
        };
    }
}

public class MapCamera{
    [Fact,Trait("Category","Unit")]public void Project(){
        {
            //Web メルカトル: 経度 −180〜180 が 0〜1、赤道が 0.5。往復で元に戻る
            Assert.Equal((0.5,0.5),Map.MapCamera.Project(0,0));
            var (x,y)=Map.MapCamera.Project(35.69,139.75);
            var g=Map.MapCamera.Unproject(x,y);
            Assert.Equal(35.69,g.Latitude,6);
            Assert.Equal(139.75,g.Longitude,6);
        }
        {
            //気象庁のタイル (z=4, x=14, y=6) の左上は東経 135°・北緯 40.98°
            var g=Map.MapCamera.Unproject(14/16.0,6/16.0);
            Assert.Equal(135,g.Longitude,6);
            Assert.Equal(40.979898,g.Latitude,5);
        }
    }

    [Fact,Trait("Category","Unit")]public void ZoomBy(){
        var camera=new Map.MapCamera();
        var size=new SKSize(400,300);
        {
            //画面と地理座標の往復
            var view=camera.Snapshot();
            var p=view.ToScreen(34.69,135.5,size);
            var g=view.ToGeo(p,size);
            Assert.Equal(34.69,g.Latitude,4);
            Assert.Equal(135.5,g.Longitude,4);
        }
        {
            //焦点の地点を保ったまま拡大する
            var focus=new SKPoint(300,100);
            var before=camera.Snapshot().ToGeo(focus,size);
            camera.ZoomBy(2,focus,size);
            var after=camera.Snapshot().ToGeo(focus,size);
            Assert.Equal(before.Latitude,after.Latitude,6);
            Assert.Equal(before.Longitude,after.Longitude,6);
        }
        {
            //ズームの範囲に収める
            camera.ZoomBy(1e9,new SKPoint(200,150),size);
            Assert.Equal(Map.MapCamera.MaxZoom,camera.Snapshot().Zoom);
        }
    }

    [Fact,Trait("Category","Unit")]public void Pan(){
        //日付変更線をまたいでも経度は循環する(ズーム 2 は世界が 1024 画素。200 画素で約 70°)
        var camera=new Map.MapCamera();
        camera.CenterOn(new GeoPoint(0,179),2);
        var size=new SKSize(400,300);
        camera.Pan(-200,0);
        var center=camera.Snapshot().ToGeo(new SKPoint(200,150),size);
        Assert.Equal(179+200/1024.0*360-360,center.Longitude,6);
    }
}

public class MapRenderer{
    [Fact,Trait("Category","Unit")]public void Render_JapanArea(){
        //方針 5: 日本周辺には GFS の色を描かない(格子の NaN とシェーダーのマスクの二重)
        using var map=new Map.MapRenderer(MapSamples.Database);
        map.Camera.CenterOn(new GeoPoint(33,132),4.5);
        map.Frame=new MapFrame{Layer=FieldLayer.Temperature,Time=MapSamples.Time,Scalar=MapSamples.Temperature(),Legend=MapLegends.Temperature,Japan=JapanCoverage.NotAvailable};
        using var bitmap=MapSamples.Render(map,480,360);
        //熊野灘(日本周辺の海)は海の色のまま、東シナ海(中国の沖)は気温の色
        Assert.Equal(BaseMapSea,Rgb(MapSamples.PixelAt(bitmap,map,33.3,136.6)));
        Assert.NotEqual(BaseMapSea,Rgb(MapSamples.PixelAt(bitmap,map,29.5,123.5)));
    }

    [Fact,Trait("Category","Unit")]public async Task Render_Tiles(){
        //気象庁のタイルは Web メルカトルの位置に描く(日本の陸地だけ赤のタイルで、陸と海の位置を確かめる)
        var tile=MapSamples.JapanLandTile();
        using var map=new Map.MapRenderer(MapSamples.Database);
        var loaded=new TaskCompletionSource();
        map.RedrawRequested+=()=>loaded.TrySetResult();
        map.TileLoader=(layer,z,x,y,ct)=>{
            if((z,x,y)==(4,14,6)){
                return Task.FromResult<byte[]?>(tile);
            }
            return Task.FromResult<byte[]?>(null);
        };
        map.Camera.CenterOn(new GeoPoint(36,138),5);
        map.Frame=new MapFrame{Layer=FieldLayer.Precipitation,Time=MapSamples.Time,Tiles=[MapSamples.TileLayer()],Legend=MapLegends.RainRate,Japan=JapanCoverage.Available};
        using(MapSamples.Render(map,480,360)){
        }
        await loaded.Task.WaitAsync(TimeSpan.FromSeconds(10),TestContext.Current.CancellationToken);
        await Task.Delay(200,TestContext.Current.CancellationToken);
        using var bitmap=MapSamples.Render(map,480,360);
        {
            //内陸(長野)は赤、沖合の海(遠州灘の南)は赤でない
            var inland=MapSamples.PixelAt(bitmap,map,36.2,138.1);
            Assert.True(inland.Red>180&&inland.Green<120,$"内陸: {inland}");
            var sea=MapSamples.PixelAt(bitmap,map,33.6,138.3);
            Assert.True(sea.Red<120,$"海: {sea}");
        }
        {
            //日本周辺の外(朝鮮半島)はタイルに色があっても描かない(このタイルは日本だけ赤なので、海と同じであること)
            var korea=MapSamples.PixelAt(bitmap,map,36.5,127.8);
            Assert.True(korea.Red<120,$"朝鮮半島: {korea}");
        }
    }

    [Fact,Trait("Category","Unit")]public void Render_Wind(){
        //風の粒子は日本周辺に入らない(粒子の軌跡も描かれない)
        using var map=new Map.MapRenderer(MapSamples.Database);
        map.Options.AutoTier=false;
        map.Camera.CenterOn(new GeoPoint(35,138),4);
        map.Frame=new MapFrame{Layer=FieldLayer.Wind,Time=MapSamples.Time,Wind=MapSamples.Wind(),Legend=MapLegends.WindSpeed,Japan=JapanCoverage.NotAvailable};
        Assert.True(map.NeedsAnimation);
        using var bitmap=MapSamples.Render(map,480,360,frames:60);
        var view=map.Camera.Snapshot();
        var size=new SKSize(480,360);
        var mask=JapanMaskTexture.For(MapSamples.Database);
        var inside=0;
        var bright=0;
        for(var y=0;y<360;y+=3){
            for(var x=0;x<480;x+=3){
                var g=view.ToGeo(new SKPoint(x,y),size);
                if(!mask.Contains(g.Latitude,g.Longitude)){
                    continue;
                }
                inside++;
                //マスクの中心部(境界の 1 画素は線形補間でにじむため除く)に白い軌跡がない
                var c=bitmap.GetPixel(x,y);
                if(c.Red>200&&c.Green>200&&c.Blue>200&&mask.Contains(g.Latitude+0.3,g.Longitude)&&mask.Contains(g.Latitude-0.3,g.Longitude)&&mask.Contains(g.Latitude,g.Longitude+0.3)&&mask.Contains(g.Latitude,g.Longitude-0.3)){
                    bright++;
                }
            }
        }
        Assert.True(inside>500);
        //地名の文字(白)を除くため、ごく少数は許容する
        Assert.True(bright<inside/50,$"日本周辺の白い画素 {bright}/{inside}");
    }

    [Fact,Trait("Category","Unit")]public void Render_JapanWind(){
        //日本周辺の海上の風(気象庁)があれば、マスクの中の海上にも粒子が流れる(GFS は使わない)
        using var map=new Map.MapRenderer(MapSamples.Database);
        map.Options.AutoTier=false;
        map.Camera.CenterOn(new GeoPoint(35,138),4);
        map.Frame=new MapFrame{Layer=FieldLayer.Wind,Time=MapSamples.Time,Wind=MapSamples.Wind(),JapanWind=MapSamples.JapanWind(),Legend=MapLegends.WindSpeed,Japan=JapanCoverage.Available};
        Assert.True(map.NeedsAnimation);
        using var bitmap=MapSamples.Render(map,480,360,frames:60);
        var view=map.Camera.Snapshot();
        var size=new SKSize(480,360);
        var mask=JapanMaskTexture.For(MapSamples.Database);
        //日本周辺の海上と、日本周辺の外(GFS)の海上で、粒子の軌跡(白い画素)の密度を比べる
        var japanSea=0;
        var japanBright=0;
        var outsideSea=0;
        var outsideBright=0;
        for(var y=0;y<360;y+=3){
            for(var x=0;x<480;x+=3){
                var g=view.ToGeo(new SKPoint(x,y),size);
                if(MapSamples.Database.FindCountry(g.Latitude,g.Longitude) is not null){
                    continue;
                }
                var c=bitmap.GetPixel(x,y);
                var white=c.Red>200&&c.Green>200&&c.Blue>200;
                if(mask.Contains(g.Latitude,g.Longitude)){
                    japanSea++;
                    if(white){
                        japanBright++;
                    }
                }else{
                    outsideSea++;
                    if(white){
                        outsideBright++;
                    }
                }
            }
        }
        Assert.True(japanSea>300&&outsideSea>300);
        var japanDensity=(double)japanBright/japanSea;
        var outsideDensity=(double)outsideBright/outsideSea;
        Assert.True(japanDensity>outsideDensity/3,$"日本周辺の海上 {japanBright}/{japanSea}、外 {outsideBright}/{outsideSea}");
        //GFS がなくても日本周辺の海上の風だけで動く
        map.Frame=new MapFrame{Layer=FieldLayer.Wind,Time=MapSamples.Time,JapanWind=MapSamples.JapanWind(),Legend=MapLegends.WindSpeed,Japan=JapanCoverage.Available};
        Assert.True(map.NeedsAnimation);
    }

    [Fact,Trait("Category","Unit")]public void Render_Allocation(){
        //地図の 1 フレームのメモリ確保(粒子・格子の描画は使い回す)
        using var map=new Map.MapRenderer(MapSamples.Database);
        map.Options.AutoTier=false;
        map.Camera.CenterOn(new GeoPoint(35,138),4);
        map.Frame=new MapFrame{Layer=FieldLayer.Wind,Time=MapSamples.Time,Wind=MapSamples.Wind(),Scalar=Fields.WindSpeed(MapSamples.Wind()),Legend=MapLegends.WindSpeed,Japan=JapanCoverage.NotAvailable};
        using var bitmap=new SKBitmap(360,640);
        using var canvas=new SKCanvas(bitmap);
        for(var i=0;i<20;i++){
            map.Render(canvas,new SKSizeI(360,640),i/30.0);
        }
        var before=GC.GetAllocatedBytesForCurrentThread();
        for(var i=20;i<80;i++){
            map.Render(canvas,new SKSizeI(360,640),i/30.0);
        }
        var perFrame=(GC.GetAllocatedBytesForCurrentThread()-before)/60;
        Assert.True(perFrame<16*1024,$"1 フレームあたり {perFrame} バイト");
    }

    [Fact,Trait("Category","Golden")]public void Render_Golden(){
        //代表の地図(気温 + 等圧線、風)。更新: WEATHER_UPDATE_GOLDEN=1
        foreach(var (name,frame,center,zoom) in Goldens()){
            using var map=new Map.MapRenderer(MapSamples.Database);
            map.Options.AutoTier=false;
            map.Camera.CenterOn(center,zoom);
            map.Frame=frame;
            map.Pins=[new MapPin(new GeoPoint(35.69,139.75),"東京 21°",false)];
            using var bitmap=MapSamples.Render(map,480,360,frames:30);
            Golden.Check(name,bitmap);
        }
    }

    private static IEnumerable<(string Name,MapFrame Frame,GeoPoint Center,double Zoom)> Goldens(){
        yield return ("map-temperature",new MapFrame{Layer=FieldLayer.Temperature,Time=MapSamples.Time,Scalar=MapSamples.Temperature(),Pressure=MapSamples.Pressure(),Legend=MapLegends.Temperature,Japan=JapanCoverage.OutOfRange},new GeoPoint(35,135),3.5);
        yield return ("map-wind",new MapFrame{Layer=FieldLayer.Wind,Time=MapSamples.Time,Wind=MapSamples.Wind(),Scalar=Fields.WindSpeed(MapSamples.Wind()),Legend=MapLegends.WindSpeed,Japan=JapanCoverage.NotAvailable},new GeoPoint(30,140),3);
    }

    private static readonly SKColor BaseMapSea=new(0x10,0x1a,0x2b);

    private static SKColor Rgb(SKColor c){
        return new SKColor(c.Red,c.Green,c.Blue);
    }

    internal static class Fields{
        public static GridField WindSpeed(WindField wind){
            var u=wind.U.Values;
            var v=wind.V.Values;
            var speed=new float[u.Length];
            for(var i=0;i<u.Length;i++){
                speed[i]=MathF.Sqrt(u[i]*u[i]+v[i]*v[i]);
            }
            return new GridField(wind.U.Geometry,speed,FieldQuantity.WindSpeed,wind.U.ValidTime,wind.U.ReferenceTime,wind.U.Source);
        }
    }
}

public class IsobarLayer{
    [Fact,Trait("Category","Unit")]public void Contour(){
        {
            //緯度だけで変わる気圧(北ほど低い)→ 等圧線は東西にのびる。4 hPa ごと、20 hPa ごとは太線
            var field=MapSamples.Field(FieldQuantity.PressureHpa,static (lat,_)=>1000+lat*0.5);
            var (thin,thick)=Rendering.Map.IsobarLayer.Contour(field);
            Assert.NotEmpty(thin);
            Assert.NotEmpty(thick);
            Assert.Equal(0,thin.Length%4);
        }
        {
            //日本周辺(NaN)のセルには線を引かない
            var field=MapSamples.Field(FieldQuantity.PressureHpa,static (lat,_)=>1000+lat*0.5);
            var (thin,thick)=Rendering.Map.IsobarLayer.Contour(field);
            var japan=new Geo.JapanArea(MapSamples.Database);
            for(var i=0;i+1<thin.Length;i+=2){
                var g=Rendering.Map.MapCamera.Unproject(thin[i],thin[i+1]);
                Assert.False(japan.Contains(g.Latitude,g.Longitude)&&japan.Contains(g.Latitude+1.5,g.Longitude)&&japan.Contains(g.Latitude-1.5,g.Longitude),$"{g}");
            }
            Assert.NotNull(thick);
        }
    }
}

/// <summary>ゴールデン画像の比較(Scene と同じ方式)。</summary>
internal static class Golden{
    public static void Check(string name,SKBitmap bitmap){
        var update=Environment.GetEnvironmentVariable("WEATHER_UPDATE_GOLDEN")=="1";
        var dir=AppContext.BaseDirectory;
        var root=new DirectoryInfo(dir);
        while(root is not null&&!File.Exists(Path.Combine(root.FullName,"Weather.Rendering.Tests.csproj"))){
            root=root.Parent;
        }
        var file=Path.Combine(root?.FullName??dir,"Golden",name+".png");
        if(update){
            using var image=SKImage.FromBitmap(bitmap);
            using var data=image.Encode(SKEncodedImageFormat.Png,100);
            File.WriteAllBytes(file,data.ToArray());
            return;
        }
        Assert.True(File.Exists(file),$"ゴールデン画像がありません: {name}(WEATHER_UPDATE_GOLDEN=1 で生成)");
        using var golden=SKBitmap.Decode(file);
        Assert.Equal((golden.Width,golden.Height),(bitmap.Width,bitmap.Height));
        double sum=0;
        var n=0;
        for(var y=0;y<bitmap.Height;y+=2){
            for(var x=0;x<bitmap.Width;x+=2){
                var a=bitmap.GetPixel(x,y);
                var b=golden.GetPixel(x,y);
                sum+=Math.Abs(a.Red-b.Red)+Math.Abs(a.Green-b.Green)+Math.Abs(a.Blue-b.Blue);
                n+=3;
            }
        }
        Assert.True(sum/n<3,$"{name}: 平均差 {sum/n:0.00}");
    }
}
