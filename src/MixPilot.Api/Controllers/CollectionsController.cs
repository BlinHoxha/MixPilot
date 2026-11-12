using System.ComponentModel.DataAnnotations;
using Framework.Contracts.Common;
using Framework.Contracts.Pagination;
using Microsoft.AspNetCore.Mvc;
using MixPilot.Application.Abstractions.Services;
using MixPilot.Contracts;

namespace MixPilot.Api.Controllers;

[ApiController, Route("api/v1/collections")]
public sealed class CollectionsController(ICollectionService library) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<CommonResult<PageResponse<CollectionResponse>>>> Search([FromQuery] PageQuery query, CancellationToken ct) =>
        CommonResult<PageResponse<CollectionResponse>>.Success(await library.Search(query, ct));
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<CommonResult<CollectionResponse>>> Get(Guid id, CancellationToken ct) =>
        CommonResult<CollectionResponse>.Success(await library.Get(id, ct));
    [HttpPost]
    [ProducesResponseType<CommonResult<CollectionResponse>>(201)]
    public async Task<ActionResult<CommonResult<CollectionResponse>>> Create(SaveCollectionRequest request, CancellationToken ct)
    {
        var collection = await library.Create(request, ct);
        return CreatedAtAction(nameof(Get), new { id = collection.Id }, CommonResult<CollectionResponse>.Success(collection));
    }
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<CommonResult<CollectionResponse>>> Update(Guid id, SaveCollectionRequest request, CancellationToken ct) =>
        CommonResult<CollectionResponse>.Success(await library.Update(id, request, ct));
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(204)]
    public async Task<IActionResult> Delete(Guid id, [FromQuery, Range(1, int.MaxValue)] int expectedRevision, CancellationToken ct)
    { await library.Delete(id, expectedRevision, ct); return NoContent(); }
}


