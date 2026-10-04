namespace Weather.Providers.Tests;

/// <summary>SolutionStructure.md の不変条件 1・2 を参照アセンブリで検査する。</summary>
public class ArchitectureRules{
    [Theory,Trait("Category","Unit")]
    [InlineData(typeof(Weather.Core.GeoPoint))]
    [InlineData(typeof(Weather.Providers.Routing.WeatherProviderRouter))]
    [InlineData(typeof(Weather.Geo.LocationResolver))]
    [InlineData(typeof(Weather.Remote.RemoteWeatherService))]
    public void NoMauiReference(Type marker){
        var names=marker.Assembly.GetReferencedAssemblies().Select(static a=>a.Name??"");
        Assert.DoesNotContain(names,static n=>n.StartsWith("Microsoft.Maui",StringComparison.Ordinal));
    }

    [Fact,Trait("Category","Unit")]public void ProvidersDoNotReferenceGeo(){
        var names=typeof(Weather.Providers.WeatherService).Assembly.GetReferencedAssemblies().Select(static a=>a.Name??"");
        Assert.DoesNotContain("Weather.Geo",names);
    }

    [Fact,Trait("Category","Unit")]public void RemoteReferencesCoreOnly(){
        //中継の電文・クライアントは Core だけに依存する(アプリが Providers / Geo なしでも使える。Phase 4)
        var names=typeof(Weather.Remote.RemoteWeatherService).Assembly.GetReferencedAssemblies().Select(static a=>a.Name??"").Where(static n=>n.StartsWith("Weather.",StringComparison.Ordinal));
        Assert.Equal(["Weather.Core"],names);
    }
}
