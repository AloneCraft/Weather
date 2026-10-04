using Microsoft.Data.Sqlite;
using Weather.Core;

namespace Weather.Infrastructure;

/// <summary>お気に入り(SQLite の favorites テーブル)。</summary>
public sealed class SqliteFavoritesStore(WeatherDatabase database):IFavoritesStore{
    public async ValueTask<IReadOnlyList<FavoritePlace>> GetAllAsync(CancellationToken cancellationToken){
        await using var connection=await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command=connection.CreateCommand();
        command.CommandText="SELECT place_id,display_name,latitude,longitude,sort_order,station_key FROM favorites ORDER BY sort_order,place_id;";
        var list=new List<FavoritePlace>();
        await using var reader=await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while(await reader.ReadAsync(cancellationToken).ConfigureAwait(false)){
            string? station=null;
            if(!reader.IsDBNull(5)){
                station=reader.GetString(5);
            }
            list.Add(new FavoritePlace(reader.GetString(0),reader.GetString(1),new GeoPoint(reader.GetDouble(2),reader.GetDouble(3)),reader.GetInt32(4)){StationKey=station});
        }
        return list;
    }

    public async ValueTask SaveAsync(FavoritePlace place,CancellationToken cancellationToken){
        ArgumentNullException.ThrowIfNull(place);
        await using var connection=await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command=connection.CreateCommand();
        command.CommandText="""
            INSERT INTO favorites(place_id,display_name,latitude,longitude,sort_order,station_key)
            VALUES($id,$name,$lat,$lon,$order,$station)
            ON CONFLICT(place_id) DO UPDATE SET display_name=$name,latitude=$lat,longitude=$lon,sort_order=$order,station_key=$station;
            """;
        command.Parameters.AddWithValue("$id",place.Id);
        command.Parameters.AddWithValue("$name",place.DisplayName);
        command.Parameters.AddWithValue("$lat",place.Point.Latitude);
        command.Parameters.AddWithValue("$lon",place.Point.Longitude);
        command.Parameters.AddWithValue("$order",place.SortOrder);
        command.Parameters.AddWithValue("$station",(object?)place.StationKey??DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DeleteAsync(string id,CancellationToken cancellationToken){
        await using var connection=await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command=connection.CreateCommand();
        command.CommandText="DELETE FROM favorites WHERE place_id=$id;";
        command.Parameters.AddWithValue("$id",id);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask ReorderAsync(IReadOnlyList<string> idsInOrder,CancellationToken cancellationToken){
        ArgumentNullException.ThrowIfNull(idsInOrder);
        await using var connection=await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction=(SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        for(var i=0;i<idsInOrder.Count;i++){
            await using var command=connection.CreateCommand();
            command.Transaction=transaction;
            command.CommandText="UPDATE favorites SET sort_order=$order WHERE place_id=$id;";
            command.Parameters.AddWithValue("$order",i);
            command.Parameters.AddWithValue("$id",idsInOrder[i]);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }
}
