using System.ComponentModel.DataAnnotations;
using Framework.Contracts.Common;
using Framework.Contracts.Pagination;
using Microsoft.AspNetCore.Mvc;
using MixPilot.Application.Abstractions.Services;
using MixPilot.Contracts;
using MixPilot.Application.AI;
using AIFramework.Core.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace MixPilot.Api.Controllers;

[ApiController, Route("api/v1/capabilities")]
public sealed class CapabilitiesController(AiFeatureOptions ai, IServiceProviderIsService services) : ControllerBase
{
    [HttpGet]
    public ActionResult<CommonResult<CapabilitiesResponse>> Get()
    {
        bool interpretation = ai.Enabled && services.IsService(typeof(IStructuredGenerationModel));
        return CommonResult<CapabilitiesResponse>.Success(new("v1",
            ["personalLibrary", "csvImport", "collections", "djProfile", "deterministicSetPlanning", "setEditing", "setVersions", ..(ai.Enabled ? new[] { "setExplanations" } : Array.Empty<string>()), ..(interpretation ? new[] { "promptInterpretation" } : Array.Empty<string>())],
            [..(interpretation ? Array.Empty<string>() : new[] { "promptInterpretation" }), "audioAnalysisJobs", "signalR", "externalDiscovery", "djSoftwareExport", "personalization"],
            100, 500, false, interpretation));
    }
}

