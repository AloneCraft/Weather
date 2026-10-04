namespace Weather.Providers.Http;

/// <summary>すべてのリクエストに User-Agent を付与する。付け忘れによる規約違反を構造的に防ぐ。</summary>
public sealed class UserAgentHandler(WeatherProviderOptions options):DelegatingHandler{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken){
        request.Headers.Remove("User-Agent");
        request.Headers.TryAddWithoutValidation("User-Agent",options.UserAgent);
        return base.SendAsync(request,cancellationToken);
    }
}
