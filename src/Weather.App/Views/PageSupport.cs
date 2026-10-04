using Weather.Presentation;

namespace Weather.App.Views;

/// <summary>Shell の引数を VM(INavigationAware)へ転送する。IQueryAttributable は MAUI の型なので VM には実装しない。</summary>
public static class QueryForwarder{
    public static async void Forward(BindableObject page,IDictionary<string,object> query){
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(query);
        if(page.BindingContext is not INavigationAware aware){
            return;
        }
        var parameters=new Dictionary<string,string>(StringComparer.Ordinal);
        foreach(var (key,value) in query){
            if(value is string s){
                parameters[key]=s;
            }else if(value is not null){
                parameters[key]=value.ToString()??"";
            }
        }
        await aware.OnNavigatedToAsync(parameters);
    }
}
