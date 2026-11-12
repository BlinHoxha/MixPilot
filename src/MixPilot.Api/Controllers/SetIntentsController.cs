using Framework.Contracts.Common;
using Microsoft.AspNetCore.Mvc;
using MixPilot.Application.Abstractions.Services;
using MixPilot.Contracts;

namespace MixPilot.Api.Controllers;

[ApiController, Route("api/v1/set-intents")]
public sealed class SetIntentsController(ISetIntentService interpreter) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<CommonResult<SetIntentResponse>>(200)]
    [ProducesResponseType<ApiProblemResponse>(503)]
    public async Task<ActionResult<CommonResult<SetIntentResponse>>> Interpret(SetIntentRequest request, CancellationToken ct) =>
        CommonResult<SetIntentResponse>.Success(await interpreter.Interpret(request, ct));
}
