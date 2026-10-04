using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using Weather.Geo.Data;
using Weather.Geo.Spatial;

namespace Weather.GeoDataBuilder;

/// <summary>
/// 地理データの生成(CacheAndRouting.md「地理データ」)。
/// 元データ: 気象庁(公共データ利用規約)、Natural Earth(パブリックドメイン)、GeoNames(CC BY 4.0)。
/// 実行: dotnet run --project tools/Weather.GeoDataBuilder
/// </summary>
internal static class Program{
    private const string UserAgent="WeatherAppGeoDataBuilder/0.1 (development build tool)";

    public static async Task<int> Main(){
        var root=FindRepositoryRoot();
        var cache=Path.Combine(root,"tools","Weather.GeoDataBuilder",".cache");
        var output=Path.Combine(root,"src","Weather.Geo","Data");
        Directory.CreateDirectory(cache);
        Directory.CreateDirectory(output);
        using var http=new HttpClient();
        http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent",UserAgent);
        http.Timeout=TimeSpan.FromMinutes(20);
        var downloader=new Downloader(http,cache);

        Console.WriteLine("[1/4] 気象庁の市町村区域");
        var areas=await JmaAreas.BuildAsync(downloader).ConfigureAwait(false);
        Write(Path.Combine(output,"jp-areas.bin"),s=>GeoDataFormat.WriteJmaAreas(s,areas));

        Console.WriteLine("[2/4] 国境(Natural Earth 10m 日本の視点版)");
        var countries=await Countries.BuildAsync(downloader).ConfigureAwait(false);
        Write(Path.Combine(output,"countries.bin"),s=>GeoDataFormat.WriteCountries(s,countries));

        Console.WriteLine("[3/4] 日本周辺域マスク");
        var mask=JapanMaskBuilder.Build(countries,areas);
        Write(Path.Combine(output,"jp-mask.bin"),s=>GeoDataFormat.WriteMask(s,mask));

        Console.WriteLine("[4/4] 都市(GeoNames cities5000)");
        var places=await Places.BuildAsync(downloader).ConfigureAwait(false);
        Write(Path.Combine(output,"places.bin"),s=>GeoDataFormat.WritePlaces(s,places));

        var stamp=string.Create(CultureInfo.InvariantCulture,$"generated={DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}\njmaAreas={areas.Count}\ncountries={countries.Count}\nplaces={places.Count}\n");
        await File.WriteAllTextAsync(Path.Combine(output,"data-version.txt"),stamp).ConfigureAwait(false);
        Console.WriteLine(stamp);
        return 0;
    }

    private static void Write(string path,Action<Stream> write){
        using(var stream=File.Create(path)){
            write(stream);
        }
        Console.WriteLine($"  -> {Path.GetFileName(path)} {new FileInfo(path).Length/1024} KB");
    }

    private static string FindRepositoryRoot(){
        var directory=new DirectoryInfo(AppContext.BaseDirectory);
        while(directory is not null){
            if(File.Exists(Path.Combine(directory.FullName,"Weather.slnx"))||Directory.Exists(Path.Combine(directory.FullName,".git"))){
                return directory.FullName;
            }
            directory=directory.Parent;
        }
        throw new InvalidOperationException("リポジトリのルートが見つかりません。");
    }
}

/// <summary>ダウンロードした元データを .cache に保存して再利用する。</summary>
internal sealed class Downloader(HttpClient http,string cacheDirectory){
    public async Task<string> GetFileAsync(string url,string fileName){
        var path=Path.Combine(cacheDirectory,fileName);
        if(File.Exists(path)&&new FileInfo(path).Length>0){
            return path;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        Console.WriteLine($"  download {url}");
        using var response=await http.GetAsync(url,HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var temp=path+".part";
        await using(var file=File.Create(temp)){
            await response.Content.CopyToAsync(file).ConfigureAwait(false);
        }
        File.Move(temp,path,true);
        return path;
    }
}

internal static class GeoJson{
    public static List<float[]> ReadRings(JsonElement geometry,double tolerance){
        var rings=new List<float[]>();
        var type=geometry.GetProperty("type").GetString();
        var coordinates=geometry.GetProperty("coordinates");
        if(type=="Polygon"){
            AddPolygon(coordinates,rings,tolerance);
        }else if(type=="MultiPolygon"){
            foreach(var polygon in coordinates.EnumerateArray()){
                AddPolygon(polygon,rings,tolerance);
            }
        }
        return rings;
    }

    private static void AddPolygon(JsonElement polygon,List<float[]> rings,double tolerance){
        foreach(var ring in polygon.EnumerateArray()){
            var values=new List<float>();
            foreach(var point in ring.EnumerateArray()){
                var lon=point[0].GetDouble();
                var lat=point[1].GetDouble();
                values.Add((float)lat);
                values.Add((float)lon);
            }
            if(values.Count<8){
                continue;
            }
            rings.Add(Polygons.Simplify([..values],tolerance));
        }
    }
}

internal static class JmaAreas{
    public static async Task<List<JmaAreaRecord>> BuildAsync(Downloader downloader){
        var areaPath=await downloader.GetFileAsync("https://www.jma.go.jp/bosai/common/const/area.json","jma/area.json").ConfigureAwait(false);
        var relmPath=await downloader.GetFileAsync("https://www.jma.go.jp/bosai/common/const/class20relm.json","jma/class20relm.json").ConfigureAwait(false);
        using var area=JsonDocument.Parse(await File.ReadAllBytesAsync(areaPath).ConfigureAwait(false));
        using var relm=JsonDocument.Parse(await File.ReadAllBytesAsync(relmPath).ConfigureAwait(false));
        var class20s=area.RootElement.GetProperty("class20s");
        var class15s=area.RootElement.GetProperty("class15s");
        var class10s=area.RootElement.GetProperty("class10s");
        var offices=area.RootElement.GetProperty("offices");
        var codes=relm.RootElement.EnumerateObject().Select(static p=>p.Name).ToList();

        //気象庁サーバーへの負荷を抑えるため同時接続数を制限する
        using var gate=new SemaphoreSlim(4);
        var tasks=codes.Select(async code=>{
            await gate.WaitAsync().ConfigureAwait(false);
            try{
                return (code,await downloader.GetFileAsync($"https://www.jma.go.jp/bosai/common/const/geojson/class20s/{code}.json",$"jma/class20s/{code}.json").ConfigureAwait(false));
            }finally{
                gate.Release();
            }
        });
        var files=await Task.WhenAll(tasks).ConfigureAwait(false);

        var result=new List<JmaAreaRecord>();
        foreach(var (code,path) in files){
            if(!class20s.TryGetProperty(code,out var node)){
                continue;
            }
            using var geo=JsonDocument.Parse(await File.ReadAllBytesAsync(path).ConfigureAwait(false));
            var rings=new List<float[]>();
            float? labelLat=null;
            float? labelLon=null;
            foreach(var feature in geo.RootElement.GetProperty("features").EnumerateArray()){
                rings.AddRange(GeoJson.ReadRings(feature.GetProperty("geometry"),0.0005));
                if(labelLat is null&&feature.GetProperty("properties").TryGetProperty("labelPoints",out var labels)&&labels.ValueKind==JsonValueKind.Array){
                    foreach(var label in labels.EnumerateArray()){
                        labelLon=(float)label[0].GetDouble();
                        labelLat=(float)label[1].GetDouble();
                        break;
                    }
                }
            }
            if(rings.Count==0){
                continue;
            }
            var shape=new GeoShape(rings);
            if(labelLat is null||labelLon is null){
                labelLat=(shape.MinLat+shape.MaxLat)/2;
                labelLon=(shape.MinLon+shape.MaxLon)/2;
            }
            var prefecture=FindOfficeName(node,class15s,class10s,offices);
            result.Add(new JmaAreaRecord(
                code,
                node.GetProperty("name").GetString()!,
                Str(node,"kana"),
                Str(node,"enName"),
                prefecture,
                labelLat.Value,
                labelLon.Value,
                shape));
        }
        result.Sort(static (a,b)=>string.CompareOrdinal(a.Code,b.Code));
        return result;
    }

    private static string FindOfficeName(JsonElement class20,JsonElement class15s,JsonElement class10s,JsonElement offices){
        var c15=class20.GetProperty("parent").GetString()!;
        var c10=class15s.GetProperty(c15).GetProperty("parent").GetString()!;
        var office=class10s.GetProperty(c10).GetProperty("parent").GetString()!;
        return offices.GetProperty(office).GetProperty("name").GetString()!;
    }

    private static string Str(JsonElement element,string name){
        if(element.TryGetProperty(name,out var value)&&value.ValueKind==JsonValueKind.String){
            return value.GetString()!;
        }
        return "";
    }
}

internal static class Countries{
    public static async Task<List<CountryRecord>> BuildAsync(Downloader downloader){
        var path=await downloader.GetFileAsync("https://raw.githubusercontent.com/nvkelso/natural-earth-vector/master/geojson/ne_10m_admin_0_countries_jpn.geojson","ne/ne_10m_admin_0_countries_jpn.geojson").ConfigureAwait(false);
        using var doc=JsonDocument.Parse(await File.ReadAllBytesAsync(path).ConfigureAwait(false));
        var result=new List<CountryRecord>();
        foreach(var feature in doc.RootElement.GetProperty("features").EnumerateArray()){
            var p=feature.GetProperty("properties");
            var iso=Str(p,"ISO_A2_EH");
            if(iso.Length!=2){
                iso=Str(p,"ISO_A2");
            }
            if(iso.Length!=2){
                continue;
            }
            var rings=GeoJson.ReadRings(feature.GetProperty("geometry"),0.01);
            if(rings.Count==0){
                continue;
            }
            var nameEn=Str(p,"NAME_EN");
            if(nameEn.Length==0){
                nameEn=Str(p,"NAME");
            }
            var nameJa=Str(p,"NAME_JA");
            if(nameJa.Length==0){
                nameJa=nameEn;
            }
            result.Add(new CountryRecord(iso,nameJa,nameEn,new GeoShape(rings)));
        }
        return result;
    }

    private static string Str(JsonElement element,string name){
        if(element.TryGetProperty(name,out var value)&&value.ValueKind==JsonValueKind.String){
            return value.GetString()!;
        }
        return "";
    }
}

/// <summary>
/// 日本周辺域マスク: 日本の陸地、および最寄りの陸地が日本である 200 km 以内の海域。
/// 格子にポリゴンを走査線で塗り、距離変換(chamfer)で最寄りの陸地の国を伝播する。
/// </summary>
internal static class JapanMaskBuilder{
    private const double MinLat=20;
    private const double MaxLat=50;
    private const double MinLon=120;
    private const double MaxLon=155;
    private const double Step=0.05;
    private const double MaxDistanceKm=200;

    public static JapanAreaMask Build(IReadOnlyList<CountryRecord> countries,IReadOnlyList<JmaAreaRecord> areas){
        var rows=(int)Math.Round((MaxLat-MinLat)/Step);
        var columns=(int)Math.Round((MaxLon-MinLon)/Step);
        var owner=new byte[rows*columns];   //0 = 海、1 = 日本、2 = 他国
        foreach(var country in countries){
            if(country.Iso2=="JP"||!Intersects(country.Shape)){
                continue;
            }
            Rasterize(country.Shape,owner,rows,columns,2);
        }
        foreach(var country in countries){
            if(country.Iso2=="JP"){
                Rasterize(country.Shape,owner,rows,columns,1);
            }
        }
        foreach(var area in areas){
            Rasterize(area.Shape,owner,rows,columns,1);
        }

        var distance=new double[rows*columns];
        for(var i=0;i<distance.Length;i++){
            if(owner[i]==0){
                distance[i]=double.MaxValue;
            }
        }
        //前進・後退の 2 パスを 2 回繰り返す chamfer 距離変換
        for(var pass=0;pass<2;pass++){
            for(var r=0;r<rows;r++){
                for(var c=0;c<columns;c++){
                    Relax(r,c,r-1,c,owner,distance,rows,columns);
                    Relax(r,c,r,c-1,owner,distance,rows,columns);
                    Relax(r,c,r-1,c-1,owner,distance,rows,columns);
                    Relax(r,c,r-1,c+1,owner,distance,rows,columns);
                }
            }
            for(var r=rows-1;r>=0;r--){
                for(var c=columns-1;c>=0;c--){
                    Relax(r,c,r+1,c,owner,distance,rows,columns);
                    Relax(r,c,r,c+1,owner,distance,rows,columns);
                    Relax(r,c,r+1,c+1,owner,distance,rows,columns);
                    Relax(r,c,r+1,c-1,owner,distance,rows,columns);
                }
            }
        }
        var bits=new byte[(rows*columns+7)/8];
        var count=0;
        for(var i=0;i<owner.Length;i++){
            if(owner[i]==1&&distance[i]<=MaxDistanceKm){
                bits[i>>3]|=(byte)(1<<(i&7));
                count++;
            }
        }
        Console.WriteLine($"  cells={count}/{owner.Length}");
        return new JapanAreaMask(MinLat,MinLon,Step,rows,columns,bits);
    }

    private static void Relax(int r,int c,int nr,int nc,byte[] owner,double[] distance,int rows,int columns){
        if(nr<0||nr>=rows||nc<0||nc>=columns){
            return;
        }
        var n=nr*columns+nc;
        if(distance[n]==double.MaxValue){
            return;
        }
        var lat=MinLat+(r+0.5)*Step;
        var dy=(nr-r)*Step*111.2;
        var dx=(nc-c)*Step*111.2*Math.Cos(lat*Math.PI/180);
        var candidate=distance[n]+Math.Sqrt(dx*dx+dy*dy);
        var i=r*columns+c;
        if(candidate<distance[i]){
            distance[i]=candidate;
            owner[i]=owner[n];
        }
    }

    private static bool Intersects(GeoShape shape){
        return shape.MaxLat>=MinLat-5&&shape.MinLat<=MaxLat+5&&shape.MaxLon>=MinLon-5&&shape.MinLon<=MaxLon+5;
    }

    /// <summary>偶奇規則の走査線塗り。セルより小さい島も頂点のセルを塗って残す。</summary>
    private static void Rasterize(GeoShape shape,byte[] owner,int rows,int columns,byte value){
        var xs=new List<double>();
        for(var r=0;r<rows;r++){
            var lat=MinLat+(r+0.5)*Step;
            if(lat<shape.MinLat||lat>shape.MaxLat){
                continue;
            }
            xs.Clear();
            foreach(var ring in shape.Rings){
                var n=ring.Length/2;
                for(int i=0,j=n-1;i<n;j=i++){
                    double yi=ring[i*2];
                    double yj=ring[j*2];
                    if((yi>lat)!=(yj>lat)){
                        double xi=ring[i*2+1];
                        double xj=ring[j*2+1];
                        xs.Add(xi+(lat-yi)*(xj-xi)/(yj-yi));
                    }
                }
            }
            xs.Sort();
            for(var k=0;k+1<xs.Count;k+=2){
                var c0=(int)Math.Ceiling((xs[k]-MinLon)/Step-0.5);
                var c1=(int)Math.Floor((xs[k+1]-MinLon)/Step-0.5);
                for(var c=Math.Max(0,c0);c<=Math.Min(columns-1,c1);c++){
                    owner[r*columns+c]=value;
                }
            }
        }
        foreach(var ring in shape.Rings){
            for(var i=0;i+1<ring.Length;i+=2){
                var r=(int)Math.Floor((ring[i]-MinLat)/Step);
                var c=(int)Math.Floor((ring[i+1]-MinLon)/Step);
                if(r>=0&&r<rows&&c>=0&&c<columns&&owner[r*columns+c]==0){
                    owner[r*columns+c]=value;
                }
            }
        }
    }
}

internal static class Places{
    public static async Task<List<PlaceRecord>> BuildAsync(Downloader downloader){
        var citiesZip=await downloader.GetFileAsync("https://download.geonames.org/export/dump/cities5000.zip","geonames/cities5000.zip").ConfigureAwait(false);
        var admin1Path=await downloader.GetFileAsync("https://download.geonames.org/export/dump/admin1CodesASCII.txt","geonames/admin1CodesASCII.txt").ConfigureAwait(false);
        var alternateZip=await downloader.GetFileAsync("https://download.geonames.org/export/dump/alternateNamesV2.zip","geonames/alternateNamesV2.zip").ConfigureAwait(false);

        var admin1=new Dictionary<string,(string Name,int Id)>(StringComparer.Ordinal);
        foreach(var line in await File.ReadAllLinesAsync(admin1Path).ConfigureAwait(false)){
            var f=line.Split('\t');
            if(f.Length>=4&&int.TryParse(f[3],CultureInfo.InvariantCulture,out var id)){
                admin1[f[0]]=(f[1],id);
            }
        }

        var rows=new List<string[]>();
        using(var zip=ZipFile.OpenRead(citiesZip)){
            var entry=zip.GetEntry("cities5000.txt")??throw new InvalidDataException("cities5000.txt がありません。");
            using var reader=new StreamReader(entry.Open());
            while(await reader.ReadLineAsync().ConfigureAwait(false) is {} line){
                var f=line.Split('\t');
                if(f.Length>=18){
                    rows.Add(f);
                }
            }
        }
        var wanted=new HashSet<int>();
        foreach(var f in rows){
            wanted.Add(int.Parse(f[0],CultureInfo.InvariantCulture));
        }
        foreach(var a in admin1.Values){
            wanted.Add(a.Id);
        }

        //日本語名(isolanguage=ja)。優先名を優先し、俗称・歴史的名称は除く
        var japanese=new Dictionary<int,(string Name,bool Preferred)>();
        using(var zip=ZipFile.OpenRead(alternateZip)){
            var entry=zip.GetEntry("alternateNamesV2.txt")??throw new InvalidDataException("alternateNamesV2.txt がありません。");
            using var reader=new StreamReader(entry.Open());
            while(await reader.ReadLineAsync().ConfigureAwait(false) is {} line){
                var f=line.Split('\t');
                if(f.Length<8||f[2]!="ja"){
                    continue;
                }
                if(!int.TryParse(f[1],CultureInfo.InvariantCulture,out var id)||!wanted.Contains(id)){
                    continue;
                }
                if(f[6]=="1"||f[7]=="1"){
                    continue;
                }
                var preferred=f[4]=="1";
                if(!japanese.TryGetValue(id,out var current)||(preferred&&!current.Preferred)){
                    japanese[id]=(f[3],preferred);
                }
            }
        }

        var result=new List<PlaceRecord>(rows.Count);
        foreach(var f in rows){
            var id=int.Parse(f[0],CultureInfo.InvariantCulture);
            string? admin1Name=null;
            if(admin1.TryGetValue(f[8]+"."+f[10],out var a)){
                admin1Name=a.Name;
                if(japanese.TryGetValue(a.Id,out var aj)){
                    admin1Name=aj.Name;
                }
            }
            string? ja=null;
            if(japanese.TryGetValue(id,out var j)){
                ja=j.Name;
            }
            int.TryParse(f[14],CultureInfo.InvariantCulture,out var population);
            result.Add(new PlaceRecord(
                id,
                f[1],
                f[2],
                ja,
                float.Parse(f[4],CultureInfo.InvariantCulture),
                float.Parse(f[5],CultureInfo.InvariantCulture),
                f[8],
                admin1Name,
                population,
                f[17]));
        }
        result.Sort(static (x,y)=>y.Population.CompareTo(x.Population));
        return result;
    }
}
