using System.ComponentModel.DataAnnotations;
using Framework.Contracts.Common;
using Framework.Contracts.Pagination;
using Microsoft.AspNetCore.Mvc;
using MixPilot.Application.Abstractions.Services;
using MixPilot.Contracts;

namespace MixPilot.Api.Controllers;

[ApiController, Route("api/v1/me/profile")]
public sealed class ProfileController(IDjProfileService library) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<CommonResult<ProfileResponse>>> Get(CancellationToken ct) =>
        CommonResult<ProfileResponse>.Success(await library.Get(ct));
    [HttpPut]
    public async Task<ActionResult<CommonResult<ProfileResponse>>> Save(SaveProfileRequest request, CancellationToken ct) =>
        CommonResult<ProfileResponse>.Success(await library.Save(request, ct));
}

