namespace Weather.Core;

/// <summary>モデルの不変条件を検証する。違反は Adapter / 変換器のバグとして例外にする。</summary>
public static class Guard{
    public static void Percent(int? value,string name){
        if(value is <0 or >100){
            throw new ArgumentOutOfRangeException(name,value,"0〜100 の範囲外です。");
        }
    }

    public static void Direction(double? value,string name){
        if(value is {} v&&(double.IsNaN(v)||v<0||v>=360)){
            throw new ArgumentOutOfRangeException(name,value,"0 以上 360 未満である必要があります。");
        }
    }

    public static void Finite(double? value,string name){
        if(value is {} v&&!double.IsFinite(v)){
            throw new ArgumentOutOfRangeException(name,value,"有限の値である必要があります。");
        }
    }

    public static void NonNegative(double? value,string name){
        if(value is {} v&&(!double.IsFinite(v)||v<0)){
            throw new ArgumentOutOfRangeException(name,value,"0 以上の有限の値である必要があります。");
        }
    }

    public static void UnitInterval(double value,string name){
        if(double.IsNaN(value)||value<0||value>1){
            throw new ArgumentOutOfRangeException(name,value,"0〜1 の範囲外です。");
        }
    }
}
