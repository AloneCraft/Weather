using System.Buffers.Binary;

namespace Weather.Providers.Grib;

/// <summary>正則な緯度経度格子(格子テンプレート 3.0)。角度は度。</summary>
public readonly record struct Grib2LatLonGrid(int Ni,int Nj,double La1,double Lo1,double La2,double Lo2,double Di,double Dj,int ScanningMode);

/// <summary>GRIB2 の 1 メッセージ(1 要素・1 時刻)。値は行 0 が北端、列 0 が Lo1 の順に並べ直してある。</summary>
public sealed class Grib2Message{
    public required int Discipline{get;init;}
    public required int Category{get;init;}
    public required int Number{get;init;}
    public required DateTimeOffset ReferenceTime{get;init;}
    public required Grib2LatLonGrid Grid{get;init;}
    public required float[] Values{get;init;}
    public required int PackingTemplate{get;init;}
}

/// <summary>
/// GRIB2(WMO FM 92 第 2 版)の復号。外部ライブラリを使わない(AOT・iOS で動かすため)。
/// 対応: 格子テンプレート 3.0、データ表現テンプレート 5.0(単純圧縮)・5.2(複合圧縮)・5.3(複合圧縮 + 空間差分)、ビットマップ。
/// GFS の pgrb2(0.5°・1°)は 5.3(2026-10-04 確認)。アルゴリズムは NCEP g2clib の comunpack に従う。
/// </summary>
public static class Grib2Decoder{
    /// <summary>格子の点数の上限。GFS 0.25°(1440×721 = 約 104 万点)に余裕を持たせた値。破損データで巨大な配列を確保しないための検証。</summary>
    private const int MaxPoints=1<<22;

    public static Grib2Message Decode(ReadOnlySpan<byte> data){
        if(data.Length<16||data[0]!='G'||data[1]!='R'||data[2]!='I'||data[3]!='B'){
            throw new FormatException("GRIB メッセージではありません。");
        }
        if(data[7]!=2){
            throw new FormatException($"GRIB の版 {data[7]} には対応していません。");
        }
        var discipline=(int)data[6];
        var total=BinaryPrimitives.ReadUInt64BigEndian(data[8..]);
        if(total>(ulong)data.Length){
            throw new FormatException("GRIB メッセージが途中で切れています。");
        }
        DateTimeOffset? reference=null;
        Grib2LatLonGrid? grid=null;
        var points=0;
        var category=-1;
        var number=-1;
        ReadOnlySpan<byte> drs=default;
        ReadOnlySpan<byte> bitmap=default;
        var hasBitmap=false;
        ReadOnlySpan<byte> payload=default;
        var pos=16;
        while(pos+4<=(int)total){
            if(data[pos]=='7'&&data[pos+1]=='7'&&data[pos+2]=='7'&&data[pos+3]=='7'){
                break;
            }
            var length=(int)BinaryPrimitives.ReadUInt32BigEndian(data[pos..]);
            if(length<5||pos+length>(int)total){
                throw new FormatException($"節の長さが不正です: {length}");
            }
            var section=data.Slice(pos,length);
            switch(section[4]){
                case 1:
                    reference=ReadReferenceTime(section);
                    break;
                case 3:
                    (grid,points)=ReadGrid(section);
                    break;
                case 4:
                    category=section[9];
                    number=section[10];
                    break;
                case 5:
                    drs=section;
                    break;
                case 6:
                    if(section[5]==0){
                        hasBitmap=true;
                        bitmap=section[6..];
                    }else if(section[5]!=255){
                        throw new NotSupportedException($"ビットマップの指示 {section[5]} には対応していません。");
                    }
                    break;
                case 7:
                    payload=section[5..];
                    break;
            }
            pos+=length;
        }
        if(reference is null||grid is not {} g||drs.IsEmpty){
            throw new FormatException("GRIB メッセージに必要な節がありません。");
        }
        var template=BinaryPrimitives.ReadUInt16BigEndian(drs[9..]);
        var encodedCount=BinaryPrimitives.ReadUInt32BigEndian(drs[5..]);
        if(encodedCount>(uint)points){
            //ビットマップがなければ点数と同じ、あればそれ以下。破損データで巨大な配列を確保しない
            throw new FormatException($"値の数 {encodedCount} が格子の点数 {points} を超えています。");
        }
        var encoded=(int)encodedCount;
        var packed=new float[encoded];
        switch(template){
            case 0:
                UnpackSimple(drs,payload,packed);
                break;
            case 2:
            case 3:
                UnpackComplex(drs,payload,packed,template==3);
                break;
            default:
                throw new NotSupportedException($"データ表現テンプレート 5.{template} には対応していません。");
        }
        var values=Expand(packed,points,hasBitmap,bitmap);
        return new Grib2Message{
            Discipline=discipline,
            Category=category,
            Number=number,
            ReferenceTime=reference.Value,
            Grid=g,
            Values=Reorder(values,g),
            PackingTemplate=template,
        };
    }

    private static DateTimeOffset ReadReferenceTime(ReadOnlySpan<byte> s){
        var year=BinaryPrimitives.ReadUInt16BigEndian(s[12..]);
        return new DateTimeOffset(year,s[14],s[15],s[16],s[17],s[18],TimeSpan.Zero);
    }

    private static (Grib2LatLonGrid Grid,int Points) ReadGrid(ReadOnlySpan<byte> s){
        var pointCount=BinaryPrimitives.ReadUInt32BigEndian(s[6..]);
        var template=BinaryPrimitives.ReadUInt16BigEndian(s[12..]);
        if(template!=0){
            throw new NotSupportedException($"格子テンプレート 3.{template} には対応していません。");
        }
        var niCount=BinaryPrimitives.ReadUInt32BigEndian(s[30..]);
        var njCount=BinaryPrimitives.ReadUInt32BigEndian(s[34..]);
        if(pointCount>MaxPoints||(long)niCount*njCount!=pointCount){
            throw new FormatException($"格子の点数が不正です: {niCount}×{njCount} と {pointCount}(上限 {MaxPoints})");
        }
        var points=(int)pointCount;
        var ni=(int)niCount;
        var nj=(int)njCount;
        var basic=BinaryPrimitives.ReadUInt32BigEndian(s[38..]);
        var subdivisions=BinaryPrimitives.ReadUInt32BigEndian(s[42..]);
        //既定の単位は 10^-6 度。掛け算ではなく割り算にして 90 や 359.5 を誤差なく表す
        double divisor=1_000_000;
        double multiplier=1;
        if(basic!=0&&basic!=uint.MaxValue&&subdivisions!=0&&subdivisions!=uint.MaxValue){
            divisor=subdivisions;
            multiplier=basic;
        }
        double Angle(int offset,ReadOnlySpan<byte> section){
            return SignedInt32(section[offset..])*multiplier/divisor;
        }
        var grid=new Grib2LatLonGrid(
            ni,
            nj,
            Angle(46,s),
            Angle(50,s),
            Angle(55,s),
            Angle(59,s),
            Angle(63,s),
            Angle(67,s),
            s[71]);
        return (grid,points);
    }

    /// <summary>(R + X × 2^E) / 10^D(5.0〜5.3 共通の換算)。</summary>
    private static (double Reference,double BinaryScale,double DecimalScale,int Bits) Scaling(ReadOnlySpan<byte> drs){
        var reference=BinaryPrimitives.ReadSingleBigEndian(drs[11..]);
        var e=SignedInt16(drs[15..]);
        var d=SignedInt16(drs[17..]);
        return (reference,Math.Pow(2,e),Math.Pow(10,-d),drs[19]);
    }

    private static void UnpackSimple(ReadOnlySpan<byte> drs,ReadOnlySpan<byte> payload,float[] output){
        var (reference,binary,dec,bits)=Scaling(drs);
        var reader=new BitReader(payload);
        for(var i=0;i<output.Length;i++){
            output[i]=(float)((reference+reader.Read(bits)*binary)*dec);
        }
    }

    /// <summary>複合圧縮(5.2)と空間差分(5.3)。g2clib の comunpack と同じ手順。</summary>
    private static void UnpackComplex(ReadOnlySpan<byte> drs,ReadOnlySpan<byte> payload,float[] output,bool spatial){
        var (reference,binary,dec,referenceBits)=Scaling(drs);
        var missingManagement=drs[22];
        var groupCount=BinaryPrimitives.ReadUInt32BigEndian(drs[31..]);
        if(groupCount>(uint)output.Length){
            throw new FormatException($"群の数 {groupCount} が値の数 {output.Length} を超えています。");
        }
        var groups=(int)groupCount;
        var widthReference=(int)drs[35];
        var widthBits=(int)drs[36];
        var lengthReference=(int)BinaryPrimitives.ReadUInt32BigEndian(drs[37..]);
        var lengthIncrement=(int)drs[41];
        var lastLength=(int)BinaryPrimitives.ReadUInt32BigEndian(drs[42..]);
        var lengthBits=(int)drs[46];
        if(groups==0){
            Array.Fill(output,(float)(reference*dec));
            return;
        }
        var reader=new BitReader(payload);
        var order=0;
        long first=0;
        long second=0;
        long minimum=0;
        if(spatial){
            order=drs[47];
            var octets=(int)drs[48];
            if(order is not (1 or 2)||octets==0){
                throw new NotSupportedException($"空間差分の次数 {order}(記述子 {octets} バイト)には対応していません。");
            }
            first=reader.ReadSignMagnitude(octets*8);
            if(order==2){
                second=reader.ReadSignMagnitude(octets*8);
            }
            minimum=reader.ReadSignMagnitude(octets*8);
        }
        var groupReference=new long[groups];
        for(var j=0;j<groups;j++){
            groupReference[j]=reader.Read(referenceBits);
        }
        reader.AlignToByte();
        var groupWidth=new int[groups];
        for(var j=0;j<groups;j++){
            groupWidth[j]=(int)reader.Read(widthBits)+widthReference;
        }
        reader.AlignToByte();
        var groupLength=new int[groups];
        for(var j=0;j<groups;j++){
            groupLength[j]=lengthReference+(int)reader.Read(lengthBits)*lengthIncrement;
        }
        groupLength[groups-1]=lastLength;
        reader.AlignToByte();

        var count=output.Length;
        var integers=new long[count];
        var missing=new bool[count];
        var n=0;
        for(var j=0;j<groups;j++){
            var width=groupWidth[j];
            var length=groupLength[j];
            if(n+length>count){
                throw new FormatException("群の長さの合計が値の数を超えています。");
            }
            if(width==0){
                var all=missingManagement!=0&&IsMissing(groupReference[j],referenceBits,missingManagement);
                for(var k=0;k<length;k++){
                    integers[n]=groupReference[j];
                    missing[n]=all;
                    n++;
                }
                continue;
            }
            for(var k=0;k<length;k++){
                var value=(long)reader.Read(width);
                if(missingManagement!=0&&IsMissing(value,width,missingManagement)){
                    missing[n]=true;
                }else{
                    integers[n]=value+groupReference[j];
                }
                n++;
            }
        }
        if(n!=count){
            throw new FormatException($"復号した値の数 {n} が期待値 {count} と一致しません。");
        }
        if(spatial){
            //欠損でない値だけで空間差分を戻す
            var previous=0L;
            var beforePrevious=0L;
            var index=0;
            for(var i=0;i<count;i++){
                if(missing[i]){
                    continue;
                }
                long value;
                if(index==0){
                    value=first;
                }else if(index==1&&order==2){
                    value=second;
                }else if(order==1){
                    value=integers[i]+minimum+previous;
                }else{
                    value=integers[i]+minimum+2*previous-beforePrevious;
                }
                integers[i]=value;
                beforePrevious=previous;
                previous=value;
                index++;
            }
        }
        for(var i=0;i<count;i++){
            if(missing[i]){
                output[i]=float.NaN;
            }else{
                output[i]=(float)((reference+integers[i]*binary)*dec);
            }
        }
    }

    /// <summary>欠損値の管理(1: 全ビット 1 が主欠損、2: さらに全ビット 1 − 1 が副欠損)。</summary>
    private static bool IsMissing(long value,int bits,int management){
        if(bits==0){
            return false;
        }
        var allOnes=(1L<<bits)-1;
        if(value==allOnes){
            return true;
        }
        return management==2&&value==allOnes-1;
    }

    /// <summary>ビットマップを展開する(ビットが立っていない点は NaN)。</summary>
    private static float[] Expand(float[] packed,int points,bool hasBitmap,ReadOnlySpan<byte> bitmap){
        if(!hasBitmap){
            if(packed.Length!=points){
                throw new FormatException($"値の数 {packed.Length} が格子の点数 {points} と一致しません。");
            }
            return packed;
        }
        var values=new float[points];
        var k=0;
        for(var i=0;i<points;i++){
            if((bitmap[i>>3]&(0x80>>(i&7)))!=0){
                values[i]=packed[k++];
            }else{
                values[i]=float.NaN;
            }
        }
        return values;
    }

    /// <summary>走査の向きを「行 0 = 北端、列は西 → 東」にそろえる。</summary>
    private static float[] Reorder(float[] values,Grib2LatLonGrid grid){
        var mode=grid.ScanningMode;
        if((mode&0x20)!=0||(mode&0x10)!=0){
            throw new NotSupportedException($"走査モード {mode} には対応していません。");
        }
        var westToEast=(mode&0x80)==0;
        var northToSouth=grid.La1>=grid.La2;
        if(westToEast&&northToSouth){
            return values;
        }
        var result=new float[values.Length];
        for(var j=0;j<grid.Nj;j++){
            var row=j;
            if(!northToSouth){
                row=grid.Nj-1-j;
            }
            for(var i=0;i<grid.Ni;i++){
                var column=i;
                if(!westToEast){
                    column=grid.Ni-1-i;
                }
                result[row*grid.Ni+column]=values[j*grid.Ni+i];
            }
        }
        return result;
    }

    /// <summary>GRIB2 の符号付き整数は「最上位ビットが符号、残りが絶対値」。</summary>
    private static int SignedInt32(ReadOnlySpan<byte> s){
        var raw=BinaryPrimitives.ReadUInt32BigEndian(s);
        var magnitude=(int)(raw&0x7FFFFFFF);
        if((raw&0x80000000)!=0){
            return -magnitude;
        }
        return magnitude;
    }

    private static int SignedInt16(ReadOnlySpan<byte> s){
        var raw=BinaryPrimitives.ReadUInt16BigEndian(s);
        var magnitude=raw&0x7FFF;
        if((raw&0x8000)!=0){
            return -magnitude;
        }
        return magnitude;
    }

    private ref struct BitReader{
        private readonly ReadOnlySpan<byte> data;
        private long position;

        public BitReader(ReadOnlySpan<byte> data){
            this.data=data;
            this.position=0;
        }

        /// <summary>上位ビットから count ビット(0〜32)を読む。</summary>
        public uint Read(int count){
            if(count==0){
                return 0;
            }
            if(count>32){
                throw new NotSupportedException($"{count} ビットの値には対応していません。");
            }
            uint result=0;
            var remaining=count;
            while(remaining>0){
                var index=(int)(this.position>>3);
                if(index>=this.data.Length){
                    throw new FormatException("データ節が途中で切れています。");
                }
                var offset=(int)(this.position&7);
                var available=8-offset;
                var take=Math.Min(available,remaining);
                var bits=(this.data[index]>>(available-take))&((1<<take)-1);
                result=(result<<take)|(uint)bits;
                remaining-=take;
                this.position+=take;
            }
            return result;
        }

        /// <summary>符号 1 ビット + 絶対値(空間差分の記述子)。</summary>
        public long ReadSignMagnitude(int bits){
            var sign=this.Read(1);
            var magnitude=(long)this.Read(bits-1);
            if(sign==1){
                return -magnitude;
            }
            return magnitude;
        }

        public void AlignToByte(){
            this.position=(this.position+7)&~7L;
        }
    }
}
