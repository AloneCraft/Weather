namespace Weather.Core;

/// <summary>地図のレイヤー(Rendering.md「地図」)。Clouds は雲量と等圧線(海面気圧)をまとめて表示する。</summary>
public enum FieldLayer{Wind,Precipitation,Temperature,Clouds}

/// <summary>格子の値の種類と単位(SI 系。表示単位への換算は Presentation)。</summary>
public enum FieldQuantity{WindU,WindV,WindSpeed,TemperatureC,PrecipitationMmPerHour,CloudCoverPercent,PressureHpa}

/// <summary>
/// 正則な緯度経度格子。行 0 が北端、列 0 が西端で、北 → 南・西 → 東に並ぶ。
/// 経度方向に地球を一周する格子(GFS)は経度方向に循環させて補間する。
/// </summary>
public readonly record struct GridGeometry{
    public GridGeometry(int columns,int rows,double northLatitude,double westLongitude,double latitudeStep,double longitudeStep){
        if(columns<2||rows<2){
            throw new ArgumentOutOfRangeException(nameof(columns),"格子は 2×2 以上が必要です。");
        }
        if(!(latitudeStep>0)||!(longitudeStep>0)){
            throw new ArgumentOutOfRangeException(nameof(latitudeStep),"格子の間隔は正の値が必要です。");
        }
        if(northLatitude>90||northLatitude-(rows-1)*latitudeStep<-90-1e-9){
            throw new ArgumentOutOfRangeException(nameof(northLatitude),"緯度の範囲外です。");
        }
        this.Columns=columns;
        this.Rows=rows;
        this.NorthLatitude=northLatitude;
        this.WestLongitude=westLongitude;
        this.LatitudeStep=latitudeStep;
        this.LongitudeStep=longitudeStep;
    }

    public int Columns{get;}
    public int Rows{get;}
    public double NorthLatitude{get;}
    public double WestLongitude{get;}
    public double LatitudeStep{get;}
    public double LongitudeStep{get;}
    public int Count=>this.Columns*this.Rows;
    public double SouthLatitude=>this.NorthLatitude-(this.Rows-1)*this.LatitudeStep;

    /// <summary>経度方向に地球を一周しているか(最後の列の次が最初の列)。</summary>
    public bool WrapsLongitude=>Math.Abs(this.Columns*this.LongitudeStep-360)<1e-6;

    public double LatitudeOf(int row){
        return this.NorthLatitude-row*this.LatitudeStep;
    }

    public double LongitudeOf(int column){
        return this.WestLongitude+column*this.LongitudeStep;
    }

    /// <summary>緯度経度 → 格子座標(小数)。範囲外は false(経度が一周する格子では経度は常に範囲内)。</summary>
    public bool TryGetPosition(double latitude,double longitude,out double column,out double row){
        if(!double.IsFinite(latitude)||!double.IsFinite(longitude)){
            //NaN は比較がすべて偽になり範囲内として扱われてしまうので、ここで範囲外にする
            column=double.NaN;
            row=double.NaN;
            return false;
        }
        row=(this.NorthLatitude-latitude)/this.LatitudeStep;
        var x=(longitude-this.WestLongitude)/this.LongitudeStep;
        if(this.WrapsLongitude){
            x%=this.Columns;
            if(x<0){
                x+=this.Columns;
            }
            if(x>=this.Columns){
                //x が 0 のすぐ手前の負の値だと、足した結果が丸めで列数ちょうどになる
                x=0;
            }
        }
        column=x;
        if(row<0||row>this.Rows-1){
            return false;
        }
        if(!this.WrapsLongitude&&(x<0||x>this.Columns-1)){
            return false;
        }
        return true;
    }
}

/// <summary>
/// 地図に描く格子の値(1 要素・1 時刻)。欠損と日本周辺(方針 5 により他ソースを表示しない)は NaN。
/// </summary>
public sealed class GridField{
    public GridField(GridGeometry geometry,float[] values,FieldQuantity quantity,DateTimeOffset validTime,DateTimeOffset referenceTime,SourceAttribution source){
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(source);
        if(values.Length!=geometry.Count){
            throw new ArgumentException($"値の数 {values.Length} が格子の点数 {geometry.Count} と一致しません。",nameof(values));
        }
        if(validTime<referenceTime){
            throw new ArgumentException("有効時刻が初期時刻より前です。",nameof(validTime));
        }
        this.Geometry=geometry;
        this.Values=values;
        this.Quantity=quantity;
        this.ValidTime=validTime;
        this.ReferenceTime=referenceTime;
        this.Source=source;
    }

    public GridGeometry Geometry{get;}

    /// <summary>行優先(北 → 南、西 → 東)。</summary>
    public float[] Values{get;}

    public FieldQuantity Quantity{get;}
    public DateTimeOffset ValidTime{get;}

    /// <summary>数値予報の初期時刻(実行回)。</summary>
    public DateTimeOffset ReferenceTime{get;}

    public SourceAttribution Source{get;}

    public float this[int column,int row]=>this.Values[row*this.Geometry.Columns+column];

    /// <summary>双線形補間。周囲の 4 点のどれかが NaN(欠損・日本周辺)なら NaN を返す。</summary>
    public float Sample(double latitude,double longitude){
        var g=this.Geometry;
        if(!g.TryGetPosition(latitude,longitude,out var x,out var y)){
            return float.NaN;
        }
        var c0=(int)Math.Floor(x);
        var r0=(int)Math.Floor(y);
        var fx=x-c0;
        var fy=y-r0;
        var c1=c0+1;
        var r1=Math.Min(r0+1,g.Rows-1);
        if(c1>=g.Columns){
            if(g.WrapsLongitude){
                c1-=g.Columns;
            }else{
                c1=g.Columns-1;
            }
        }
        var v00=this[c0,r0];
        var v10=this[c1,r0];
        var v01=this[c0,r1];
        var v11=this[c1,r1];
        var top=v00+(v10-v00)*fx;
        var bottom=v01+(v11-v01)*fx;
        return (float)(top+(bottom-top)*fy);
    }

    /// <summary>
    /// 最も近い格子点の値(補間しない)。各格子点はその点を中心とする 1 区画を代表するとみなし、区画の中は同じ値になる。範囲外・欠損は NaN。
    /// 気象庁の格子(海上分布予報)はこれで描く。気象庁の格子点値を空間内挿すると独自の予報とみなされ予報業務の許可が必要になるため
    /// (気象庁「予報業務許可に関する Q&A」)、気象庁のデータには Sample(双線形補間)を使わない。
    /// </summary>
    public float SampleNearest(double latitude,double longitude){
        var g=this.Geometry;
        if(!g.TryGetPosition(latitude,longitude,out var x,out var y)){
            return float.NaN;
        }
        var column=(int)Math.Round(x,MidpointRounding.AwayFromZero);
        var row=(int)Math.Round(y,MidpointRounding.AwayFromZero);
        if(column>=g.Columns){
            if(g.WrapsLongitude){
                column-=g.Columns;
            }else{
                column=g.Columns-1;
            }
        }
        row=Math.Min(row,g.Rows-1);
        return this[column,row];
    }
}

/// <summary>風(東西成分 U・南北成分 V。m/s)。</summary>
public sealed record WindField(GridField U,GridField V){
    public (float U,float V) Sample(double latitude,double longitude){
        return (this.U.Sample(latitude,longitude),this.V.Sample(latitude,longitude));
    }

    /// <summary>最も近い格子点の値(補間しない。GridField.SampleNearest)。気象庁の海上分布予報の風に使う。</summary>
    public (float U,float V) SampleNearest(double latitude,double longitude){
        return (this.U.SampleNearest(latitude,longitude),this.V.SampleNearest(latitude,longitude));
    }
}

/// <summary>
/// 日本周辺域の判定(CacheAndRouting.md)。地点の解決(ResolvedLocation.IsJapanArea)と同じ規則。
/// 地図では、この範囲に気象庁以外のデータ(GFS)を描かない(方針 5)。実装は Geo。
/// </summary>
public interface IJapanArea{
    bool Contains(double latitude,double longitude);
}
