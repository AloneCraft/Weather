using System.Buffers.Binary;
using Weather.Providers.Grib;
using Target=Weather.Providers.Grib.Grib2Decoder;

namespace Weather.Providers.Tests;

internal static class GfsFixture{
    public const string File="gfs/gfs.t00z.pgrb2.1p00.f003";

    /// <summary>記録データ(1°、2026-10-04 00Z 初期値の 3 時間予報。6 要素を連結し、.idx の位置を付け直したもの)。</summary>
    public static IReadOnlyDictionary<string,byte[]> Messages(){
        var data=System.IO.File.ReadAllBytes(FixtureHttpMessageHandler.FixturePath(File));
        var lines=System.IO.File.ReadAllLines(FixtureHttpMessageHandler.FixturePath(File+".idx")).Where(static l=>l.Length>0).ToArray();
        var result=new Dictionary<string,byte[]>(StringComparer.Ordinal);
        for(var i=0;i<lines.Length;i++){
            var parts=lines[i].Split(':');
            var start=int.Parse(parts[1],System.Globalization.CultureInfo.InvariantCulture);
            var end=data.Length;
            if(i+1<lines.Length){
                end=int.Parse(lines[i+1].Split(':')[1],System.Globalization.CultureInfo.InvariantCulture);
            }
            result[parts[3]]=data[start..end];
        }
        return result;
    }
}

public class Grib2Decoder{
    [Fact,Trait("Category","Unit")]public void Decode(){
        var messages=GfsFixture.Messages();
        {
            //GFS 1°: 格子 3.0(360×181、北端から)・複合圧縮 + 空間差分(5.3)
            var m=Target.Decode(messages["TMP"]);
            Assert.Equal(3,m.PackingTemplate);
            Assert.Equal(new Grib2LatLonGrid(360,181,90,0,-90,359,1,1,0),m.Grid);
            Assert.Equal(new DateTimeOffset(2026,10,4,0,0,0,TimeSpan.Zero),m.ReferenceTime);
            Assert.Equal((0,0,0),(m.Discipline,m.Category,m.Number));
            Assert.Equal(360*181,m.Values.Length);
        }
        {
            //走査順の検証: 北極の行(1 点を 360 回繰り返す)はすべて同じ値になる。並びがずれていれば崩れる
            foreach(var name in new[]{"TMP","PRMSL"}){
                var m=Target.Decode(messages[name]);
                var pole=m.Values.AsSpan(0,360).ToArray();
                Assert.All(pole,v=>Assert.Equal(pole[0],v,0.01f));
                var south=m.Values.AsSpan(180*360,360).ToArray();
                Assert.All(south,v=>Assert.Equal(south[0],v,0.01f));
            }
        }
        {
            //物理的に妥当な範囲(気温 K、風 m/s、気圧 Pa、降水強度 kg/m²/s、雲量 %)
            AssertRange(Target.Decode(messages["TMP"]).Values,180,330);
            AssertRange(Target.Decode(messages["UGRD"]).Values,-80,80);
            AssertRange(Target.Decode(messages["VGRD"]).Values,-80,80);
            AssertRange(Target.Decode(messages["PRMSL"]).Values,85_000,110_000);
            AssertRange(Target.Decode(messages["PRATE"]).Values,0,0.1f);
            AssertRange(Target.Decode(messages["TCDC"]).Values,0,100);
        }
        {
            //地理的な妥当性: 赤道付近は南極より暖かい。日本付近の海面気圧は 980〜1040 hPa
            var t=Target.Decode(messages["TMP"]);
            Assert.True(At(t,0,140)>At(t,-80,0)+30);
            var p=Target.Decode(messages["PRMSL"]);
            Assert.InRange(At(p,35,140)/100,980,1040);
        }
        {
            //単純圧縮(5.0)とビットマップ: 合成したメッセージを正確に復号する
            var values=new[]{1f,2.5f,float.NaN,4f,10f,float.NaN};
            var m=Target.Decode(SyntheticSimple(values,3,2));
            Assert.Equal(0,m.PackingTemplate);
            Assert.Equal(values,m.Values);
        }
        {
            //南 → 北に並ぶ格子は北端から並べ直す
            var m=Target.Decode(SyntheticSimple([1,2,3,4,5,6],3,2,southToNorth:true));
            Assert.Equal([4f,5,6,1,2,3],m.Values);
        }
        {
            //GRIB でないデータ・途中で切れたデータは FormatException
            Assert.Throws<FormatException>(()=>Target.Decode("NOTGRIB0000000000000"u8));
            Assert.Throws<FormatException>(()=>Target.Decode(messages["TMP"].AsSpan(0,1000)));
        }
    }

    private static void AssertRange(float[] values,float min,float max){
        Assert.All(values,v=>Assert.InRange(v,min,max));
    }

    private static float At(Grib2Message m,int lat,int lon){
        return m.Values[(90-lat)*360+lon];
    }

    /// <summary>単純圧縮(5.0)のメッセージを作る。NaN はビットマップで欠損にする。値は 0.1 刻み(D=1、E=0、8 ビット)。</summary>
    private static byte[] SyntheticSimple(float[] values,int ni,int nj,bool southToNorth=false){
        using var stream=new MemoryStream();
        void Section(byte number,byte[] body){
            Span<byte> head=stackalloc byte[5];
            BinaryPrimitives.WriteUInt32BigEndian(head,(uint)(body.Length+5));
            head[4]=number;
            stream.Write(head);
            stream.Write(body);
        }
        static byte[] Int32(int value){
            var b=new byte[4];
            var magnitude=(uint)Math.Abs(value);
            if(value<0){
                magnitude|=0x80000000;
            }
            BinaryPrimitives.WriteUInt32BigEndian(b,magnitude);
            return b;
        }
        stream.Write("GRIB"u8);
        stream.Write(new byte[]{0,0,0,2});
        stream.Write(new byte[8]);
        var s1=new byte[16];
        BinaryPrimitives.WriteUInt16BigEndian(s1.AsSpan(7),2026);
        s1[9]=10;
        s1[10]=4;
        Section(1,s1);
        var s3=new byte[67];
        BinaryPrimitives.WriteUInt32BigEndian(s3.AsSpan(1),(uint)(ni*nj));
        Int32(ni).CopyTo(s3,25);
        Int32(nj).CopyTo(s3,29);
        var la1=10_000_000;
        var la2=9_000_000;
        if(southToNorth){
            (la1,la2)=(la2,la1);
        }
        Int32(la1).CopyTo(s3,41);
        Int32(0).CopyTo(s3,45);
        Int32(la2).CopyTo(s3,50);
        Int32(2_000_000).CopyTo(s3,54);
        Int32(1_000_000).CopyTo(s3,58);
        Int32(1_000_000).CopyTo(s3,62);
        if(southToNorth){
            s3[66]=0x40;
        }
        Section(3,s3);
        Section(4,new byte[29]);
        var present=values.Where(static v=>!float.IsNaN(v)).ToArray();
        var s5=new byte[16];
        BinaryPrimitives.WriteUInt32BigEndian(s5,(uint)present.Length);
        BinaryPrimitives.WriteSingleBigEndian(s5.AsSpan(6),0);
        BinaryPrimitives.WriteUInt16BigEndian(s5.AsSpan(12),1);
        s5[14]=8;
        Section(5,s5);
        var hasMissing=present.Length!=values.Length;
        var bitmap=new byte[1+(values.Length+7)/8];
        if(hasMissing){
            for(var i=0;i<values.Length;i++){
                if(!float.IsNaN(values[i])){
                    bitmap[1+(i>>3)]|=(byte)(0x80>>(i&7));
                }
            }
        }else{
            bitmap=[255];
        }
        Section(6,bitmap);
        Section(7,[..present.Select(static v=>(byte)Math.Round(v*10))]);
        stream.Write("7777"u8);
        var bytes=stream.ToArray();
        BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(8),(ulong)bytes.Length);
        return bytes;
    }
}
