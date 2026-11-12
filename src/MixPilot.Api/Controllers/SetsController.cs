using System.ComponentModel.DataAnnotations;
using Framework.Contracts.Common;
using Framework.Contracts.Pagination;
using Microsoft.AspNetCore.Mvc;
using MixPilot.Application.Abstractions.Services;
using MixPilot.Contracts;

namespace MixPilot.Api.Controllers;

[ApiController, Route("api/v1/sets")]
[ProducesResponseType<ProblemDetails>(422), ProducesResponseType<ProblemDetails>(409)]
public sealed class SetsController(ISetService sets) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<CommonResult<PageResponse<SetSummaryResponse>>>(200)]
    public async Task<ActionResult<CommonResult<PageResponse<SetSummaryResponse>>>> Search([FromQuery] PageQuery query, CancellationToken ct) =>
        CommonResult<PageResponse<SetSummaryResponse>>.Success(await sets.Search(query, ct));
    [HttpGet("{id:guid}")]
    [ProducesResponseType<CommonResult<SetResponse>>(200)]
    public async Task<ActionResult<CommonResult<SetResponse>>> Get(Guid id, CancellationToken ct) =>
        CommonResult<SetResponse>.Success(await sets.Get(id, null, ct));
    [HttpGet("{id:guid}/versions")]
    [ProducesResponseType<CommonResult<IReadOnlyList<SetVersionResponse>>>(200)]
    public async Task<ActionResult<CommonResult<IReadOnlyList<SetVersionResponse>>>> Versions(Guid id, CancellationToken ct) =>
        CommonResult<IReadOnlyList<SetVersionResponse>>.Success(await sets.Versions(id, ct));
    [HttpGet("{id:guid}/versions/{version:int:min(1)}")]
    [ProducesResponseType<CommonResult<SetResponse>>(200)]
    public async Task<ActionResult<CommonResult<SetResponse>>> Version(Guid id, int version, CancellationToken ct) =>
        CommonResult<SetResponse>.Success(await sets.Get(id, version, ct));
    [HttpPost("{id:guid}/versions/{version:int:min(1)}/approve")]
    [ProducesResponseType<CommonResult<SetResponse>>(200)]
    public async Task<ActionResult<CommonResult<SetResponse>>> Approve(Guid id, int version, ApproveSetVersionRequest request, CancellationToken ct) =>
        CommonResult<SetResponse>.Success(await sets.Approve(id, version, request, ct));
    [HttpPost("generate")]
    [ProducesResponseType<CommonResult<SetResponse>>(201)]
    public async Task<ActionResult<CommonResult<SetResponse>>> Generate(GenerateSetRequest request, CancellationToken ct)
    {
        var set = await sets.Generate(request, ct);
        return CreatedAtAction(nameof(Get), new { id = set.Id }, CommonResult<SetResponse>.Success(set));
    }
    [HttpPost("{id:guid}/regenerate")]
    [ProducesResponseType<CommonResult<SetResponse>>(200)]
    public async Task<ActionResult<CommonResult<SetResponse>>> Regenerate(Guid id, RegenerateSetRequest request, CancellationToken ct) =>
        CommonResult<SetResponse>.Success(await sets.Regenerate(id, request, ct));
    [HttpPut("{id:guid}/tracks")]
    [ProducesResponseType<CommonResult<SetResponse>>(200)]
    public async Task<ActionResult<CommonResult<SetResponse>>> Edit(Guid id, EditSetRequest request, CancellationToken ct) =>
        CommonResult<SetResponse>.Success(await sets.Edit(id, request, ct));
}


