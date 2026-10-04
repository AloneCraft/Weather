using System.Collections.Concurrent;
using SkiaSharp;
using Weather.Core;

namespace Weather.Rendering.Map;

/// <summary>タイルの読み込み(Presentation / App が IMapDataService につなぐ)。</summary>
public delegate Task<byte[]?> MapTileLoader(JmaTileLayer layer,int z,int x,int y,CancellationToken cancellationToken);

/// <summary>
/// 気象庁の地図タイル(配色を変えずにそのまま描く。方針 5)。表示中のタイルを非同期に読み、読み終えたら再描画を求める。
/// まだ読めていないタイルは、読み込み済みの低いズームのタイルを拡大して代わりに描く。
/// </summary>
internal sealed class TileLayer:IDisposable{
    private const int MaxCached=192;
    private readonly ConcurrentDictionary<string,SKImage?> cache=new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<string> order=new();
    private readonly ConcurrentDictionary<string,byte> pending=new(StringComparer.Ordinal);
    private readonly SKPaint paint=new(){IsAntialias=false};
    private CancellationTokenSource cancel=new();

    public MapTileLoader? Loader{get;set;}

    /// <summary>タイルを読み終えたとき(描画スレッド以外から呼ばれる)。</summary>
    public event Action? TileLoaded;

    public float Opacity{get;set;}=0.85f;

    /// <summary>表示する層が変わったら、読み込み中の要求を取り消す(キャッシュは残す)。</summary>
    public void CancelPending(){
        this.cancel.Cancel();
        this.cancel.Dispose();
        this.cancel=new CancellationTokenSource();
        this.pending.Clear();
    }

    public void Draw(SKCanvas canvas,MapView view,SKSize size,JmaTileLayer layer){
        var z=layer.TileZoomFor(view.Zoom+Math.Log2(Math.Max(1,view.PixelRatio)));
        var n=1<<z;
        var s=view.WorldSize;
        var (ox,oy)=view.Origin(size);
        var x0=(int)Math.Floor(ox*n);
        var x1=(int)Math.Floor((ox+size.Width/s)*n);
        var y0=Math.Max(0,(int)Math.Floor(oy*n));
        var y1=Math.Min(n-1,(int)Math.Floor((oy+size.Height/s)*n));
        var sampling=new SKSamplingOptions(SKFilterMode.Linear);
        if(layer.Pixelated){
            sampling=new SKSamplingOptions(SKFilterMode.Nearest);
        }
        this.paint.Color=SKColors.White.WithAlpha((byte)(this.Opacity*255));
        for(var ty=y0;ty<=y1;ty++){
            for(var tx=x0;tx<=x1;tx++){
                var wx=((tx%n)+n)%n;
                var dest=new SKRect(
                    (float)((tx/(double)n-ox)*s),
                    (float)((ty/(double)n-oy)*s),
                    (float)(((tx+1)/(double)n-ox)*s),
                    (float)(((ty+1)/(double)n-oy)*s));
                if(this.TryGet(layer,z,wx,ty,out var image)){
                    if(image is not null){
                        canvas.DrawImage(image,dest,sampling,this.paint);
                    }
                    continue;
                }
                this.Request(layer,z,wx,ty);
                this.DrawFallback(canvas,layer,z,wx,ty,dest,sampling);
            }
        }
    }

    /// <summary>読み込み済みの低いズームのタイルの該当部分を拡大して描く。</summary>
    private void DrawFallback(SKCanvas canvas,JmaTileLayer layer,int z,int x,int y,SKRect dest,SKSamplingOptions sampling){
        var step=1;
        if(layer.EvenZoomOnly){
            step=2;
        }
        for(var pz=z-step;pz>=layer.MinZoom;pz-=step){
            var shift=z-pz;
            var px=x>>shift;
            var py=y>>shift;
            if(!this.TryGet(layer,pz,px,py,out var parent)){
                continue;
            }
            if(parent is null){
                return;
            }
            var part=256f/(1<<shift);
            var source=SKRect.Create((x-(px<<shift))*part,(y-(py<<shift))*part,part,part);
            canvas.DrawImage(parent,source,dest,sampling,this.paint);
            return;
        }
    }

    private static string Key(JmaTileLayer layer,int z,int x,int y){
        return string.Create(System.Globalization.CultureInfo.InvariantCulture,$"{layer.ProductPath}/{layer.Element}/{layer.BaseTime.UtcTicks}/{layer.ValidTime.UtcTicks}/{z}/{x}/{y}");
    }

    private bool TryGet(JmaTileLayer layer,int z,int x,int y,out SKImage? image){
        return this.cache.TryGetValue(Key(layer,z,x,y),out image);
    }

    private void Request(JmaTileLayer layer,int z,int x,int y){
        var loader=this.Loader;
        if(loader is null){
            return;
        }
        var key=Key(layer,z,x,y);
        if(!this.pending.TryAdd(key,0)){
            return;
        }
        var token=this.cancel.Token;
        _=Task.Run(async ()=>{
            try{
                var bytes=await loader(layer,z,x,y,token).ConfigureAwait(false);
                SKImage? image=null;
                if(bytes is not null){
                    image=SKImage.FromEncodedData(bytes);
                }
                this.Store(key,image);
                this.TileLoaded?.Invoke();
            }catch(OperationCanceledException){
                //層の切り替えで取り消した
            }catch(Exception ex) when(ex is HttpRequestException or IOException or WeatherProviderException){
                //読めないタイルは描かない(次の再描画で再び要求する)
            }finally{
                this.pending.TryRemove(key,out _);
            }
        },token);
    }

    private void Store(string key,SKImage? image){
        this.cache[key]=image;
        this.order.Enqueue(key);
        while(this.order.Count>MaxCached&&this.order.TryDequeue(out var old)){
            if(this.cache.TryRemove(old,out var removed)){
                //描画中の画像を破棄しないよう、参照は GC に任せる
                _=removed;
            }
        }
    }

    public int CachedCount=>this.cache.Count;

    public void Dispose(){
        this.cancel.Cancel();
        this.cancel.Dispose();
        this.paint.Dispose();
    }
}
