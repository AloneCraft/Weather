using Android.App;
using Android.Appwidget;
using Android.Content;
using Android.Views;
using Android.Widget;
using Weather.Presentation.Background;
using Weather.Presentation.Resources;

namespace Weather.App.Background;

/// <summary>ホーム画面のウィジェット。内容はアプリ(前面)と背景更新が書いた JSON を表示するだけで、ここでは取得しない。</summary>
[BroadcastReceiver(Label="@string/widget_name",Exported=false)]
[IntentFilter([AppWidgetManager.ActionAppwidgetUpdate])]
[MetaData(AppWidgetManager.MetaDataAppwidgetProvider,Resource="@xml/weather_widget_info")]
public sealed class WeatherWidgetProvider:AppWidgetProvider{
    public override void OnUpdate(Context? context,AppWidgetManager? appWidgetManager,int[]? appWidgetIds){
        if(context is null||appWidgetManager is null||appWidgetIds is null){
            return;
        }
        var snapshot=WidgetFile.Read(context);
        appWidgetManager.UpdateAppWidget(appWidgetIds,WidgetViews.Create(context,snapshot));
        if(snapshot is null){
            //置かれた直後でまだ内容がない: 背景更新を 1 回実行する(気象庁・NWS の地点のみ取得される)
            AndroidBackgroundPlatform.RunOnce(context);
        }
    }
}

internal static class WidgetFile{
    public static string PathFor(Context context){
        return Path.Combine(context.FilesDir!.AbsolutePath,WidgetSnapshotJson.FileName);
    }

    public static WidgetSnapshot? Read(Context context){
        try{
            return WidgetSnapshotJson.Deserialize(File.ReadAllText(PathFor(context)));
        }catch(IOException){
            return null;
        }
    }

    /// <summary>一時ファイルに書いてから置き換える(ウィジェットが書きかけを読まないように)。</summary>
    public static void Write(Context context,WidgetSnapshot snapshot){
        var path=PathFor(context);
        var temp=path+".tmp";
        File.WriteAllText(temp,WidgetSnapshotJson.Serialize(snapshot));
        File.Move(temp,path,true);
    }
}

internal static class WidgetViews{
    public static RemoteViews Create(Context context,WidgetSnapshot? snapshot){
        var views=new RemoteViews(context.PackageName,Resource.Layout.weather_widget);
        views.SetOnClickPendingIntent(Resource.Id.widget_root,AndroidBackgroundPlatform.OpenAppIntent(context,0));
        if(snapshot is null){
            views.SetTextViewText(Resource.Id.widget_place,context.GetString(Resource.String.widget_name));
            views.SetTextViewText(Resource.Id.widget_icon,"");
            views.SetTextViewText(Resource.Id.widget_temp,"");
            views.SetTextViewText(Resource.Id.widget_condition,Strings.WidgetOpenApp);
            views.SetViewVisibility(Resource.Id.widget_highlow,ViewStates.Gone);
            views.SetViewVisibility(Resource.Id.widget_alert,ViewStates.Gone);
            views.SetTextViewText(Resource.Id.widget_footer,"");
            return views;
        }
        var temperature="";
        if(snapshot.State==WidgetState.Ready){
            temperature=snapshot.Temperature;
        }
        views.SetTextViewText(Resource.Id.widget_place,snapshot.PlaceName);
        views.SetTextViewText(Resource.Id.widget_icon,snapshot.Icon);
        views.SetTextViewText(Resource.Id.widget_temp,temperature);
        views.SetTextViewText(Resource.Id.widget_condition,snapshot.ConditionText);
        SetOptional(views,Resource.Id.widget_highlow,JoinNonEmpty("  ",snapshot.HighLow,snapshot.Precipitation));
        SetOptional(views,Resource.Id.widget_alert,snapshot.AlertText);
        views.SetTextViewText(Resource.Id.widget_footer,JoinNonEmpty("\n",snapshot.Credit,JoinNonEmpty(" · ",snapshot.Attribution,snapshot.UpdatedText),snapshot.Note));
        return views;
    }

    private static void SetOptional(RemoteViews views,int id,string? text){
        if(string.IsNullOrEmpty(text)){
            views.SetViewVisibility(id,ViewStates.Gone);
            return;
        }
        views.SetViewVisibility(id,ViewStates.Visible);
        views.SetTextViewText(id,text);
    }

    private static string? JoinNonEmpty(string separator,params string?[] parts){
        var list=parts.Where(static p=>!string.IsNullOrEmpty(p)).ToList();
        if(list.Count==0){
            return null;
        }
        return string.Join(separator,list);
    }
}
