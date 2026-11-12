using System.ComponentModel.DataAnnotations;
using Framework.Contracts.Common;
using Framework.Contracts.Pagination;
using Microsoft.AspNetCore.Mvc;
using MixPilot.Application.Abstractions.Services;
using MixPilot.Contracts;

namespace MixPilot.Api.Controllers;

[ApiController, Route("api/v1/imports")]
public sealed class ImportsController(ITrackService library) : ControllerBase
{
    [HttpPost("csv")]
    [ProducesResponseType<ValidationProblemDetails>(400)]
    [ProducesResponseType<CommonResult<ImportResponse>>(200)]
    public async Task<ActionResult<CommonResult<ImportResponse>>> Import(CsvImportRequest request, CancellationToken ct) =>
        CommonResult<ImportResponse>.Success(await library.Import(request, ct));
}

