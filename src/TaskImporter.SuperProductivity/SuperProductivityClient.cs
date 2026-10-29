using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TaskImporter.Core;
using TaskImporter.SuperProductivity.Models;

namespace TaskImporter.SuperProductivity;

/// <summary>
/// A typed client over Super Productivity's Local REST API.
///
/// Every response carries an { ok, data | error } envelope, so unwrapping
/// happens once here and callers deal in real types or a
/// <see cref="SuperProductivityException"/> that already knows how to explain
/// itself.
/// </summary>
public sealed class SuperProductivityClient : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly bool _ownsHttpClient;

    public SuperProductivityClient(SuperProductivityOptions options, HttpClient? http = null)
    {
        Options = options;
        _ownsHttpClient = http is null;
        _http = http ?? new HttpClient();
        _http.Timeout = options.RequestTimeout;
    }

    public SuperProductivityOptions Options { get; }

    // ---- Health ---------------------------------------------------------

    /// <summary>
    /// Probes /health, which needs no token. Returns null when nothing is
    /// listening, rather than throwing, because "is it running" is a question
    /// the palette asks casually and often.
    /// </summary>
    public async Task<HealthDto?> GetHealthAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _http
                .GetAsync(Options.BuildUri("health"), cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode) return null;

            var envelope = await response.Content
                .ReadFromJsonAsync<ApiEnvelope<HealthDto>>(JsonOptions, cancellationToken)
                .ConfigureAwait(false);

            return envelope?.Ok == true ? envelope.Data : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return null;
        }
    }

    // ---- Tasks ----------------------------------------------------------

    public Task<TaskDto> CreateTaskAsync(TaskDraft draft, CancellationToken cancellationToken = default)
        => SendAsync<TaskDto>(HttpMethod.Post, "tasks", ToRequest(draft), cancellationToken);

    public Task<TaskDto> CompleteTaskAsync(string taskId, CancellationToken cancellationToken = default)
        => SendAsync<TaskDto>(
            HttpMethod.Patch,
            $"tasks/{Uri.EscapeDataString(taskId)}",
            new UpdateTaskRequest { IsDone = true },
            cancellationToken);

    public Task<IReadOnlyList<TaskDto>> GetTasksAsync(
        string? query = null,
        string? projectId = null,
        string? tagId = null,
        bool includeDone = false,
        CancellationToken cancellationToken = default)
    {
        var parameters = new List<string>();
        if (!string.IsNullOrWhiteSpace(query)) parameters.Add("query=" + Uri.EscapeDataString(query!));
        if (!string.IsNullOrWhiteSpace(projectId)) parameters.Add("projectId=" + Uri.EscapeDataString(projectId!));
        if (!string.IsNullOrWhiteSpace(tagId)) parameters.Add("tagId=" + Uri.EscapeDataString(tagId!));
        if (includeDone) parameters.Add("includeDone=true");

        var path = parameters.Count == 0 ? "tasks" : "tasks?" + string.Join('&', parameters);
        return SendAsync<IReadOnlyList<TaskDto>>(HttpMethod.Get, path, null, cancellationToken);
    }

    /// <summary>
    /// Today's list, which is the TODAY tag rather than a date filter, in the
    /// order Super Productivity shows it.
    ///
    /// /tasks does not return tasks in list order, so the tag is fetched as
    /// well: its taskIds are the order the user dragged them into.
    /// </summary>
    public async Task<IReadOnlyList<TaskDto>> GetTodayAsync(CancellationToken cancellationToken = default)
    {
        var tasks = GetTasksAsync(tagId: "TODAY", cancellationToken: cancellationToken);
        var tags = GetTagsAsync(cancellationToken);
        await Task.WhenAll(tasks, tags).ConfigureAwait(false);

        var today = System.Linq.Enumerable.FirstOrDefault(
            tags.Result, tag => string.Equals(tag.Id, "TODAY", StringComparison.Ordinal));

        return InListOrder(tasks.Result, today?.TaskIds);
    }

    /// <summary>
    /// Sorts tasks by a tag's taskIds. Anything the tag does not mention keeps
    /// its relative order and goes at the end, so a task is never dropped just
    /// because the two calls disagree.
    /// </summary>
    public static IReadOnlyList<TaskDto> InListOrder(IReadOnlyList<TaskDto> tasks, IReadOnlyList<string>? order)
    {
        if (order is null || order.Count == 0) return tasks;

        var position = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < order.Count; i++) position.TryAdd(order[i], i);

        return System.Linq.Enumerable.ToArray(
            System.Linq.Enumerable.OrderBy(
                tasks, task => position.TryGetValue(task.Id, out var at) ? at : int.MaxValue));
    }

    // ---- The timer ------------------------------------------------------

    /// <summary>
    /// The task the timer is running on, or null when nothing is running.
    /// </summary>
    public async Task<TaskDto?> GetCurrentTaskAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await SendAsync<TaskDto?>(HttpMethod.Get, "task-control/current", null, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (SuperProductivityException ex) when (ex.Fault == SuperProductivityFault.Rejected)
        {
            // Some builds answer "no current task" with an error rather than null.
            return null;
        }
    }

    public Task StartTaskAsync(string taskId, CancellationToken cancellationToken = default)
        => SendAsync<object?>(
            HttpMethod.Post,
            $"tasks/{Uri.EscapeDataString(taskId)}/start",
            null,
            cancellationToken);

    public Task StopCurrentTaskAsync(CancellationToken cancellationToken = default)
        => SendAsync<object?>(HttpMethod.Post, "task-control/stop", null, cancellationToken);

    // ---- Reference data -------------------------------------------------

    public Task<IReadOnlyList<ProjectDto>> GetProjectsAsync(CancellationToken cancellationToken = default)
        => SendAsync<IReadOnlyList<ProjectDto>>(HttpMethod.Get, "projects", null, cancellationToken);

    public Task<IReadOnlyList<TagDto>> GetTagsAsync(CancellationToken cancellationToken = default)
        => SendAsync<IReadOnlyList<TagDto>>(HttpMethod.Get, "tags", null, cancellationToken);

    // ---- Plumbing -------------------------------------------------------

    /// <summary>
    /// Maps a draft onto the wire format. Public because it is worth testing on
    /// its own — it is where date formatting and field omission are decided.
    /// </summary>
    public static CreateTaskRequest ToRequest(TaskDraft draft)
    {
        var request = new CreateTaskRequest
        {
            Title = draft.Title,
            Notes = string.IsNullOrWhiteSpace(draft.Notes) ? null : draft.Notes,
            ProjectId = draft.ProjectId,
            TagIds = draft.TagIds.Count == 0 ? null : System.Linq.Enumerable.ToArray(draft.TagIds),
            TimeEstimate = draft.TimeEstimateMs,
        };

        if (draft.DueWithTime is not null)
        {
            request.DueWithTime = draft.DueWithTime.Value.ToUnixTimeMilliseconds();
            request.DueDay = draft.DueWithTime.Value.ToString("yyyy-MM-dd");
        }
        else if (draft.DueDay is not null)
        {
            request.DueDay = draft.DueDay.Value.ToString("yyyy-MM-dd");
        }

        return request;
    }

    private async Task<T> SendAsync<T>(
        HttpMethod method,
        string path,
        object? body,
        CancellationToken cancellationToken)
    {
        if (!Options.HasToken)
        {
            throw new SuperProductivityException(
                SuperProductivityFault.Unauthorized,
                "No access token is configured.");
        }

        using var request = new HttpRequestMessage(method, Options.BuildUri(path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Options.AccessToken);

        if (body is not null)
        {
            request.Content = JsonContent.Create(body, body.GetType(), options: JsonOptions);
        }

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new SuperProductivityException(
                SuperProductivityFault.NotRunning,
                "Could not reach Super Productivity.", inner: ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new SuperProductivityException(
                SuperProductivityFault.NotRunning,
                "Super Productivity did not respond in time.", inner: ex);
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                throw new SuperProductivityException(
                    SuperProductivityFault.Unauthorized,
                    "The access token was rejected.", "UNAUTHORIZED");
            }

            ApiEnvelope<T>? envelope;
            try
            {
                envelope = await response.Content
                    .ReadFromJsonAsync<ApiEnvelope<T>>(JsonOptions, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (JsonException ex)
            {
                throw new SuperProductivityException(
                    SuperProductivityFault.Unknown,
                    "Super Productivity returned something that was not a valid response.",
                    inner: ex);
            }

            if (envelope is null)
            {
                throw new SuperProductivityException(
                    SuperProductivityFault.Unknown, "Super Productivity returned an empty response.");
            }

            if (!envelope.Ok)
            {
                var code = envelope.Error?.Code;
                throw new SuperProductivityException(
                    MapFault(code),
                    envelope.Error?.ToString() ?? "The request was rejected.",
                    code);
            }

            return envelope.Data!;
        }
    }

    private static SuperProductivityFault MapFault(string? code) => code switch
    {
        "UNAUTHORIZED" => SuperProductivityFault.Unauthorized,
        "RENDERER_NOT_READY" or "NOT_READY" => SuperProductivityFault.NotReady,
        null => SuperProductivityFault.Unknown,
        _ => SuperProductivityFault.Rejected,
    };

    public void Dispose()
    {
        if (_ownsHttpClient) _http.Dispose();
    }
}
