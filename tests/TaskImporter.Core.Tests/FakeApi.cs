using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TaskImporter.Core.Tests;

/// <summary>
/// Stands in for Super Productivity: answers "METHOD path" with canned JSON and
/// keeps what was sent. Lets the client's real serialization run end to end
/// without a network or a running app.
/// </summary>
public sealed class FakeApi : HttpMessageHandler
{
    private readonly Dictionary<string, (HttpStatusCode Status, string Json)> _routes = new(StringComparer.Ordinal);

    public List<(string Route, string? Body)> Requests { get; } = new();

    public FakeApi On(string route, string json, HttpStatusCode status = HttpStatusCode.OK)
    {
        _routes[route] = (status, json);
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var route = request.Method.Method + " " + request.RequestUri!.PathAndQuery;
        var body = request.Content is null
            ? null
            : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        Requests.Add((route, body));

        if (!_routes.TryGetValue(route, out var answer))
        {
            return new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("""{"ok":false,"error":{"code":"NOT_FOUND","message":"no route"}}""", Encoding.UTF8, "application/json"),
            };
        }

        return new HttpResponseMessage(answer.Status)
        {
            Content = new StringContent(answer.Json, Encoding.UTF8, "application/json"),
        };
    }
}
