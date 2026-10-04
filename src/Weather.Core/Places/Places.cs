namespace Weather.Core;

/// <summary>お気に入りの地点。StationKey は観測履歴に使う観測所(History.md)。</summary>
public sealed record FavoritePlace(string Id,string DisplayName,GeoPoint Point,int SortOrder){
    public string? StationKey{get;init;}
}

public interface IFavoritesStore{
    ValueTask<IReadOnlyList<FavoritePlace>> GetAllAsync(CancellationToken cancellationToken);
    ValueTask SaveAsync(FavoritePlace place,CancellationToken cancellationToken);
    ValueTask DeleteAsync(string id,CancellationToken cancellationToken);
    ValueTask ReorderAsync(IReadOnlyList<string> idsInOrder,CancellationToken cancellationToken);
}

public enum PlaceKind{JmaArea,City}

/// <summary>地点検索の候補。</summary>
public sealed record PlaceSuggestion(string Id,string Name,string? Detail,GeoPoint Point,string CountryCode,PlaceKind Kind,long Population);

public interface IPlaceSearch{
    ValueTask<IReadOnlyList<PlaceSuggestion>> SearchAsync(string query,int maxCount,CancellationToken cancellationToken);
}
