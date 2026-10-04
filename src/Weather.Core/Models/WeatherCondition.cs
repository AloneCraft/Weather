namespace Weather.Core;

public enum SkyCover{Unknown,Clear,MostlyClear,PartlyCloudy,MostlyCloudy,Overcast}
public enum PrecipitationType{None,Drizzle,Rain,FreezingRain,RainAndSnow,Snow,IcePellets,Hail}
public enum PrecipitationIntensity{None,Light,Moderate,Heavy}
public enum Obscuration{None,Mist,Fog,Haze,Smoke,Dust}
public enum ConditionTransition{Occasionally,Temporarily,Later}   //時々 / 一時 / 後

/// <summary>要素分解した天気。昼夜は含めない(太陽高度から計算する)。</summary>
public readonly record struct WeatherCondition{
    public WeatherCondition(
        SkyCover sky,
        PrecipitationType precipitation=PrecipitationType.None,
        PrecipitationIntensity intensity=PrecipitationIntensity.None,
        bool isShowery=false,
        bool hasThunder=false,
        bool hasStrongWind=false,
        Obscuration obscuration=Obscuration.None){
        if((precipitation==PrecipitationType.None)!=(intensity==PrecipitationIntensity.None)){
            throw new ArgumentException("降水種別と強度は、どちらも None かどちらも非 None である必要があります。",nameof(intensity));
        }
        this.Sky=sky;
        this.Precipitation=precipitation;
        this.Intensity=intensity;
        this.IsShowery=isShowery;
        this.HasThunder=hasThunder;
        this.HasStrongWind=hasStrongWind;
        this.Obscuration=obscuration;
    }

    public SkyCover Sky{get;}
    public PrecipitationType Precipitation{get;}
    public PrecipitationIntensity Intensity{get;}
    public bool IsShowery{get;}
    public bool HasThunder{get;}
    public bool HasStrongWind{get;}
    public Obscuration Obscuration{get;}

    public bool HasPrecipitation=>this.Precipitation!=PrecipitationType.None;
}

/// <summary>主天気 +(時々 / 一時 / 後)+ 副天気。</summary>
public readonly record struct CompositeCondition{
    public CompositeCondition(WeatherCondition primary,ConditionTransition? transition=null,WeatherCondition? secondary=null){
        if(transition.HasValue!=secondary.HasValue){
            throw new ArgumentException("Transition と Secondary は、どちらも指定するかどちらも省略する必要があります。",nameof(secondary));
        }
        this.Primary=primary;
        this.Transition=transition;
        this.Secondary=secondary;
    }

    public WeatherCondition Primary{get;}
    public ConditionTransition? Transition{get;}
    public WeatherCondition? Secondary{get;}

    public static implicit operator CompositeCondition(WeatherCondition primary){
        return new CompositeCondition(primary);
    }
}
