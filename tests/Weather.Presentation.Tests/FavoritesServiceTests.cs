using Weather.Core;
using Weather.Presentation;

namespace Weather.Presentation.Tests;

public class FavoritesService{
    [Fact,Trait("Category","Unit")]public async Task AddAsync(){
        var store=new FakeFavorites();
        var service=new Presentation.ViewModels.FavoritesService(store,new FakeWeatherService());
        var ct=TestContext.Current.CancellationToken;
        var changed=0;
        service.Changed+=(_,_)=>changed++;
        var first=await service.AddAsync("東京",new GeoPoint(35.6894,139.6917),ct);
        {
            //同じ予報セル(座標を小数 2 桁に丸めて一致)の地点は追加せず、既存を返す(行・ピン・背景取得の枠が重複しない)
            var again=await service.AddAsync("東京",new GeoPoint(35.6901,139.6949),ct);
            Assert.Equal(first.Id,again.Id);
            Assert.Single(store.Items);
            Assert.Equal(1,changed);
        }
        {
            //別のセルの地点は追加できる(並び順は末尾)
            var other=await service.AddAsync("横浜",new GeoPoint(35.4437,139.6380),ct);
            Assert.NotEqual(first.Id,other.Id);
            Assert.Equal(2,store.Items.Count);
            Assert.Equal(first.SortOrder+1,other.SortOrder);
            Assert.Equal(2,changed);
        }
    }
}
