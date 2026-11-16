using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;
using Framework.Contracts.Pagination;
using MixPilot.Application.Abstractions.AI;
using MixPilot.Application.Abstractions.Persistence;
using MixPilot.Application.Abstractions.Services;
using MixPilot.Contracts;
using MixPilot.Domain;

namespace MixPilot.Application.Services;

public sealed class SetGenerationJobService(ISetGenerationJobRepository jobs, ISetService sets,
    ISetPromptInterpreter interpreter, IUnitOfWork unitOfWork, TimeProvider clock) : ISetGenerationJobService, ISetGenerationJobProcessor
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    { Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) } };

    public async Task<SetGenerationJobResponse> Create(CreateSetGenerationJobRequest request, string correlationId, CancellationToken ct)
    {
        if (!Validator.TryValidateObject(request, new ValidationContext(request), new List<ValidationResult>(), true))
            throw new RuleViolation("validation_failed", "The job request is invalid.", 400);
        var now = clock.GetUtcNow();
        var job = new SetGenerationJob
        {
            Name = request.Name.Trim(),
            RequirementsJson = request.Requirements is null ? null : JsonSerializer.Serialize(request.Requirements, Json),
            Prompt = request.Prompt,
            Territory = request.Territory,
            CreatedAt = now,
            UpdatedAt = now,
            CorrelationId = correlationId
        };
        await jobs.Add(job, ct);
        return Response(job);
    }

    public async Task<SetGenerationJobResponse> Get(Guid id, CancellationToken ct) =>
        Response(await jobs.Get(id, ct) ?? throw Missing());

    public async Task<PageResponse<SetGenerationJobResponse>> Search(PageQuery query, CancellationToken ct)
    {
        var page = await jobs.Search(query, ct);
        return PageResponse<SetGenerationJobResponse>.Create(page.Items.Select(Response), page.PageNumber, page.PageSize, page.TotalCount);
    }

    public async Task<SetGenerationJobResponse> Cancel(Guid id, CancellationToken ct) =>
        Response(await jobs.Cancel(id, clock.GetUtcNow(), ct));

    public async Task Process(Guid id, int attempt, CancellationToken ct)
    {
        var job = await jobs.Get(id, ct) ?? throw Missing();
        if (job.Status != "running" || job.Attempts != attempt)
            throw new InvalidOperationException("The job lease is no longer owned by this worker.");
        try
        {
            if (await jobs.CancelIfRequested(id, attempt, clock.GetUtcNow(), ct)) return;
            SetRequirementsRequest requirements;
            if (job.Prompt is not null)
            {
                var preview = await interpreter.Interpret(new() { Prompt = job.Prompt, Territory = job.Territory! }, ct);
                requirements = preview.Requirements;
                await jobs.SetProgress(id, attempt, 50, clock.GetUtcNow(), ct);
                if (await jobs.CancelIfRequested(id, attempt, clock.GetUtcNow(), ct)) return;
            }
            else requirements = JsonSerializer.Deserialize<SetRequirementsRequest>(job.RequirementsJson!, Json)!;
            if (await jobs.CancelIfRequested(id, attempt, clock.GetUtcNow(), ct)) return;
            await unitOfWork.ExecuteInTransaction(async token =>
            {
                var set = await sets.Generate(new() { Name = job.Name, Requirements = requirements }, token);
                await jobs.Finish(id, attempt, "succeeded", null, set.Id, clock.GetUtcNow(), token);
            }, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (InvalidOperationException error) when (error.Message == "The job lease is no longer owned by this worker.")
        { throw; }
        catch (RuleViolation error)
        {
            await jobs.Finish(id, attempt, "failed", error.Code, null, clock.GetUtcNow(), CancellationToken.None);
        }
        catch
        {
            await jobs.Finish(id, attempt, "failed", "internal_error", null, clock.GetUtcNow(), CancellationToken.None);
        }
    }

    private static RuleViolation Missing() => new("not_found", "The job was not found.", 404);
    private static SetGenerationJobResponse Response(SetGenerationJob job) => new(job.Id, "setGeneration", job.Status,
        job.Progress, job.ErrorCode, job.ResultSetId, job.CreatedAt, job.UpdatedAt, job.CorrelationId);
}
