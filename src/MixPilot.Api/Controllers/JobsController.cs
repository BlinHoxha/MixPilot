using Framework.Contracts.Common;
using Framework.Contracts.Pagination;
using Microsoft.AspNetCore.Mvc;
using MixPilot.Application.Abstractions.Services;
using MixPilot.Contracts;

namespace MixPilot.Api.Controllers;

[ApiController, Route("api/v1/set-generation-jobs")]
public sealed class SetGenerationJobsController(ISetGenerationJobService jobs) : ControllerBase
{
    [HttpPost, Idempotent]
    [ProducesResponseType<CommonResult<SetGenerationJobResponse>>(202)]
    public async Task<ActionResult<CommonResult<SetGenerationJobResponse>>> Create(CreateSetGenerationJobRequest request, CancellationToken ct)
    {
        var job = await jobs.Create(request, HttpContext.TraceIdentifier, ct);
        return Accepted($"/api/v1/jobs/{job.Id}", CommonResult<SetGenerationJobResponse>.Success(job));
    }
}

[ApiController, Route("api/v1/jobs")]
public sealed class JobsController(ISetGenerationJobService jobs) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<CommonResult<PageResponse<SetGenerationJobResponse>>>(200)]
    public async Task<ActionResult<CommonResult<PageResponse<SetGenerationJobResponse>>>> Search([FromQuery] PageQuery query, CancellationToken ct) =>
        CommonResult<PageResponse<SetGenerationJobResponse>>.Success(await jobs.Search(query, ct));

    [HttpGet("{id:guid}")]
    [ProducesResponseType<CommonResult<SetGenerationJobResponse>>(200)]
    public async Task<ActionResult<CommonResult<SetGenerationJobResponse>>> Get(Guid id, CancellationToken ct) =>
        CommonResult<SetGenerationJobResponse>.Success(await jobs.Get(id, ct));

    [HttpPost("{id:guid}/cancel")]
    [ProducesResponseType<CommonResult<SetGenerationJobResponse>>(200)]
    public async Task<ActionResult<CommonResult<SetGenerationJobResponse>>> Cancel(Guid id, CancellationToken ct) =>
        CommonResult<SetGenerationJobResponse>.Success(await jobs.Cancel(id, ct));
}
