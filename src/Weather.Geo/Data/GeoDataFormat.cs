using System.IO.Compression;
using System.Text;

namespace Weather.Geo.Data;

/// <summary>多角形(穴を含む複数の輪)。各輪は [lat0,lon0,lat1,lon1,…] の float 配列。</summary>
public sealed class GeoShape{
    public GeoShape(IReadOnlyList<float[]> rings){
        ArgumentNullException.ThrowIfNull(rings);
        this.Rings=rings;
        var minLat=float.MaxValue;
        var minLon=float.MaxValue;
        var maxLat=float.MinValue;
        var maxLon=float.MinValue;
        foreach(var ring in rings){
            for(var i=0;i+1<ring.Length;i+=2){
                minLat=Math.Min(minLat,ring[i]);
                maxLat=Math.Max(maxLat,ring[i]);
                minLon=Math.Min(minLon,ring[i+1]);
                maxLon=Math.Max(maxLon,ring[i+1]);
            }
        }
        this.MinLat=minLat;
        this.MinLon=minLon;
        this.MaxLat=maxLat;
        this.MaxLon=maxLon;
    }

    public IReadOnlyList<float[]> Rings{get;}
    public float MinLat{get;}
    public float MinLon{get;}
    public float MaxLat{get;}
    public float MaxLon{get;}

    public bool BoxContains(double lat,double lon){
        return lat>=this.MinLat&&lat<=this.MaxLat&&lon>=this.MinLon&&lon<=this.MaxLon;
    }
}

/// <summary>気象庁の市町村区域(class20)。</summary>
public sealed record JmaAreaRecord(string Code,string Name,string Kana,string EnglishName,string PrefectureName,float LabelLatitude,float LabelLongitude,GeoShape Shape);

/// <summary>国境(Natural Earth 10m、日本の視点版)。</summary>
public sealed record CountryRecord(string Iso2,string NameJa,string NameEn,GeoShape Shape);

/// <summary>都市(GeoNames cities5000)。</summary>
public sealed record PlaceRecord(int Id,string Name,string AsciiName,string? JaName,float Latitude,float Longitude,string Country,string? Admin1,int Population,string TimeZone);

/// <summary>日本周辺域マスク(格子のビット列)。</summary>
public sealed class JapanAreaMask(double minLat,double minLon,double step,int rows,int columns,byte[] bits){
    public double MinLat{get;}=minLat;
    public double MinLon{get;}=minLon;
    public double Step{get;}=step;
    public int Rows{get;}=rows;
    public int Columns{get;}=columns;
    public byte[] Bits{get;}=bits;

    public bool Contains(double lat,double lon){
        var row=(int)Math.Floor((lat-this.MinLat)/this.Step);
        var column=(int)Math.Floor((lon-this.MinLon)/this.Step);
        if(row<0||row>=this.Rows||column<0||column>=this.Columns){
            return false;
        }
        var index=row*this.Columns+column;
        return (this.Bits[index>>3]&(1<<(index&7)))!=0;
    }
}

/// <summary>地理データのバイナリ形式(GZip 圧縮)。GeoDataBuilder が書き、Geo が読む。</summary>
public static class GeoDataFormat{
    private const int Version=1;

    public static void WriteJmaAreas(Stream stream,IReadOnlyList<JmaAreaRecord> areas){
        using var writer=CreateWriter(stream,"JPA");
        writer.Write(areas.Count);
        foreach(var area in areas){
            writer.Write(area.Code);
            writer.Write(area.Name);
            writer.Write(area.Kana);
            writer.Write(area.EnglishName);
            writer.Write(area.PrefectureName);
            writer.Write(area.LabelLatitude);
            writer.Write(area.LabelLongitude);
            WriteShape(writer,area.Shape);
        }
    }

    public static List<JmaAreaRecord> ReadJmaAreas(Stream stream){
        using var reader=CreateReader(stream,"JPA");
        var count=reader.ReadInt32();
        var list=new List<JmaAreaRecord>(count);
        for(var i=0;i<count;i++){
            list.Add(new JmaAreaRecord(reader.ReadString(),reader.ReadString(),reader.ReadString(),reader.ReadString(),reader.ReadString(),reader.ReadSingle(),reader.ReadSingle(),ReadShape(reader)));
        }
        return list;
    }

    public static void WriteCountries(Stream stream,IReadOnlyList<CountryRecord> countries){
        using var writer=CreateWriter(stream,"CTY");
        writer.Write(countries.Count);
        foreach(var country in countries){
            writer.Write(country.Iso2);
            writer.Write(country.NameJa);
            writer.Write(country.NameEn);
            WriteShape(writer,country.Shape);
        }
    }

    public static List<CountryRecord> ReadCountries(Stream stream){
        using var reader=CreateReader(stream,"CTY");
        var count=reader.ReadInt32();
        var list=new List<CountryRecord>(count);
        for(var i=0;i<count;i++){
            list.Add(new CountryRecord(reader.ReadString(),reader.ReadString(),reader.ReadString(),ReadShape(reader)));
        }
        return list;
    }

    public static void WritePlaces(Stream stream,IReadOnlyList<PlaceRecord> places){
        using var writer=CreateWriter(stream,"PLC");
        var zones=places.Select(static p=>p.TimeZone).Distinct(StringComparer.Ordinal).ToList();
        var zoneIndex=zones.Select(static (z,i)=>(z,i)).ToDictionary(static x=>x.z,static x=>x.i,StringComparer.Ordinal);
        writer.Write(zones.Count);
        foreach(var zone in zones){
            writer.Write(zone);
        }
        writer.Write(places.Count);
        foreach(var place in places){
            writer.Write(place.Id);
            writer.Write(place.Name);
            writer.Write(place.AsciiName);
            writer.Write(place.JaName??"");
            writer.Write(place.Latitude);
            writer.Write(place.Longitude);
            writer.Write(place.Country);
            writer.Write(place.Admin1??"");
            writer.Write(place.Population);
            writer.Write((ushort)zoneIndex[place.TimeZone]);
        }
    }

    public static List<PlaceRecord> ReadPlaces(Stream stream){
        using var reader=CreateReader(stream,"PLC");
        var zoneCount=reader.ReadInt32();
        var zones=new string[zoneCount];
        for(var i=0;i<zoneCount;i++){
            zones[i]=reader.ReadString();
        }
        var count=reader.ReadInt32();
        var list=new List<PlaceRecord>(count);
        for(var i=0;i<count;i++){
            var id=reader.ReadInt32();
            var name=reader.ReadString();
            var ascii=reader.ReadString();
            var ja=reader.ReadString();
            var lat=reader.ReadSingle();
            var lon=reader.ReadSingle();
            var country=reader.ReadString();
            var admin1=reader.ReadString();
            var population=reader.ReadInt32();
            var zone=zones[reader.ReadUInt16()];
            list.Add(new PlaceRecord(id,name,ascii,EmptyToNull(ja),lat,lon,country,EmptyToNull(admin1),population,zone));
        }
        return list;
    }

    public static void WriteMask(Stream stream,JapanAreaMask mask){
        ArgumentNullException.ThrowIfNull(mask);
        using var writer=CreateWriter(stream,"MSK");
        writer.Write(mask.MinLat);
        writer.Write(mask.MinLon);
        writer.Write(mask.Step);
        writer.Write(mask.Rows);
        writer.Write(mask.Columns);
        writer.Write(mask.Bits.Length);
        writer.Write(mask.Bits);
    }

    public static JapanAreaMask ReadMask(Stream stream){
        using var reader=CreateReader(stream,"MSK");
        var minLat=reader.ReadDouble();
        var minLon=reader.ReadDouble();
        var step=reader.ReadDouble();
        var rows=reader.ReadInt32();
        var columns=reader.ReadInt32();
        var length=reader.ReadInt32();
        return new JapanAreaMask(minLat,minLon,step,rows,columns,reader.ReadBytes(length));
    }

    private static void WriteShape(BinaryWriter writer,GeoShape shape){
        writer.Write(shape.Rings.Count);
        foreach(var ring in shape.Rings){
            writer.Write(ring.Length);
            foreach(var value in ring){
                writer.Write(value);
            }
        }
    }

    private static GeoShape ReadShape(BinaryReader reader){
        var ringCount=reader.ReadInt32();
        var rings=new List<float[]>(ringCount);
        for(var r=0;r<ringCount;r++){
            var length=reader.ReadInt32();
            var ring=new float[length];
            for(var i=0;i<length;i++){
                ring[i]=reader.ReadSingle();
            }
            rings.Add(ring);
        }
        return new GeoShape(rings);
    }

    private static BinaryWriter CreateWriter(Stream stream,string magic){
        var gzip=new GZipStream(stream,CompressionLevel.SmallestSize,leaveOpen:true);
        var writer=new BinaryWriter(gzip,Encoding.UTF8,leaveOpen:false);
        writer.Write(magic);
        writer.Write(Version);
        return writer;
    }

    private static BinaryReader CreateReader(Stream stream,string magic){
        var gzip=new GZipStream(stream,CompressionMode.Decompress,leaveOpen:true);
        var reader=new BinaryReader(gzip,Encoding.UTF8,leaveOpen:false);
        var actualMagic=reader.ReadString();
        var version=reader.ReadInt32();
        if(actualMagic!=magic||version!=Version){
            throw new InvalidDataException($"地理データの形式が違います: {actualMagic} v{version}");
        }
        return reader;
    }

    private static string? EmptyToNull(string value){
        if(value.Length==0){
            return null;
        }
        return value;
    }
}
