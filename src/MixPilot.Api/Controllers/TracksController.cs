using System.ComponentModel.DataAnnotations;
using Framework.Contracts.Common;
using Framework.Contracts.Pagination;
using Microsoft.AspNetCore.Mvc;
using MixPilot.Application.Abstractions.Services;
using MixPilot.Contracts;

namespace MixPilot.Api.Controllers;

[ApiController, Route("api/v1/tracks")]
[ProducesResponseType<ProblemDetails>(401), ProducesResponseType<ValidationProblemDetails>(400)]
public sealed class TracksController(ITrackService library) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<CommonResult<PageResponse<TrackResponse>>>(200)]
    public async Task<ActionResult<CommonResult<PageResponse<TrackResponse>>>> Search([FromQuery] TrackQuery query, CancellationToken ct) =>
        CommonResult<PageResponse<TrackResponse>>.Success(await library.Search(query, ct));
    [HttpGet("{id:guid}")]
    [ProducesResponseType<ProblemDetails>(404)]
    [ProducesResponseType<CommonResult<TrackResponse>>(200)]
    public async Task<ActionResult<CommonResult<TrackResponse>>> Get(Guid id, CancellationToken ct) =>
        CommonResult<TrackResponse>.Success(await library.Get(id, ct));
    [HttpPost]
    [ProducesResponseType<CommonResult<TrackResponse>>(201), ProducesResponseType<ProblemDetails>(409)]
    public async Task<ActionResult<CommonResult<TrackResponse>>> Create(CreateTrackRequest request, CancellationToken ct)
    {
        var track = await library.Create(request, ct);
        return CreatedAtAction(nameof(Get), new { id = track.Id }, CommonResult<TrackResponse>.Success(track));
    }
    [HttpPut("{id:guid}")]
    [ProducesResponseType<ProblemDetails>(404), ProducesResponseType<ProblemDetails>(409)]
    [ProducesResponseType<CommonResult<TrackResponse>>(200)]
    public async Task<ActionResult<CommonResult<TrackResponse>>> Update(Guid id, UpdateTrackRequest request, CancellationToken ct) =>
        CommonResult<TrackResponse>.Success(await library.Update(id, request, ct));
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(204), ProducesResponseType<ProblemDetails>(404), ProducesResponseType<ProblemDetails>(409)]
    public async Task<IActionResult> Delete(Guid id, [FromQuery, Range(1, int.MaxValue)] int expectedRevision, CancellationToken ct)
    { await library.Delete(id, expectedRevision, ct); return NoContent(); }
}


