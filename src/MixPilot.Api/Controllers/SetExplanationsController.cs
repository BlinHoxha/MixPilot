using Framework.Contracts.Common;
using Microsoft.AspNetCore.Mvc;
using MixPilot.Application.Abstractions.Services;
using MixPilot.Contracts;

namespace MixPilot.Api.Controllers;

[ApiController, Route("api/v1/sets/{id:guid}/explanation")]
public sealed class SetExplanationsController(ISetExplanationService explanations) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<CommonResult<SetExplanationResponse>>(200)]
    [ProducesResponseType<ApiProblemResponse>(503)]
    public async Task<ActionResult<CommonResult<SetExplanationResponse>>> Explain(Guid id, ExplainSetRequest request, CancellationToken ct) =>
        CommonResult<SetExplanationResponse>.Success(await explanations.Explain(id, request, ct));
}
