using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using MixPilot.Api;
using MixPilot.Application.Abstractions.Identity;
using MixPilot.Domain;
using MixPilot.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 1_048_576);
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<JobOwnerContext>();
builder.Services.AddScoped<HttpCurrentUser>();
builder.Services.AddScoped<ICurrentUser, ScopedCurrentUser>();
builder.Services.AddMixPilot(builder.Configuration);
if (builder.Configuration.GetValue("Jobs:Enabled", true)) builder.Services.AddHostedService<SetGenerationWorker>();

bool developmentIdentity = builder.Configuration.GetValue<bool>("Authentication:UseDevelopmentIdentity");
if (developmentIdentity && !builder.Environment.IsDevelopment())
    throw new InvalidOperationException("The development identity is permitted only in Development.");
if (developmentIdentity)
    builder.Services.AddAuthentication("Development").AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, DevelopmentAuthenticationHandler>("Development", _ => { });
else
{
    string authority = builder.Configuration["Authentication:Authority"] ?? throw new InvalidOperationException("Authentication:Authority is required.");
    string audience = builder.Configuration["Authentication:Audience"] ?? throw new InvalidOperationException("Authentication:Audience is required.");
    if (!Uri.TryCreate(authority, UriKind.Absolute, out var authorityUri) || authorityUri.Scheme != "https" || string.IsNullOrWhiteSpace(audience))
        throw new InvalidOperationException("Configure an HTTPS OIDC authority and a nonempty API audience.");
    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
    {
        options.Authority = authority; options.Audience = audience; options.MapInboundClaims = false;
        options.RequireHttpsMetadata = true;
    });
}
if (!builder.Environment.IsDevelopment() && builder.Configuration.GetValue<bool>("AI:Enabled") &&
    string.Equals(builder.Configuration["AI:Provider"] ?? "Local", "Local", StringComparison.OrdinalIgnoreCase))
    throw new InvalidOperationException("The local extractive AI provider is intended for Development only.");
builder.Services.AddAuthorization(options => options.FallbackPolicy = new AuthorizationPolicyBuilder()
    .RequireAuthenticatedUser().RequireClaim("sub").RequireClaim("iss").Build());

builder.Services.AddControllers(options => options.OutputFormatters.RemoveType<StringOutputFormatter>()).AddJsonOptions(options =>
{
    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
    options.JsonSerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
});
builder.Services.Configure<ApiBehaviorOptions>(options => options.InvalidModelStateResponseFactory = context =>
{
    var problem = new ValidationProblemDetails(context.ModelState)
    {
        Status = 400, Title = "Request validation failed", Type = "https://httpstatuses.com/400", Instance = context.HttpContext.Request.Path
    };
    problem.Extensions["code"] = "validation_failed"; problem.Extensions["traceId"] = context.HttpContext.TraceIdentifier;
    return new BadRequestObjectResult(problem) { ContentTypes = { "application/problem+json" } };
});
builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
{
    context.ProblemDetails.Extensions.TryAdd("traceId", context.HttpContext.TraceIdentifier);
    context.ProblemDetails.Extensions.TryAdd("code", context.ProblemDetails.Status switch
    {
        401 => "authentication_required", 403 => "forbidden", 404 => "not_found", 405 => "method_not_allowed", _ => "request_failed"
    });
});
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new() { Title = "MixPilot API", Version = "v1", Description = "Shared web and mobile backend. Metadata registration and deterministic set planning." });
    options.SupportNonNullableReferenceTypes();
    options.OperationFilter<ApiContractOperationFilter>();
    options.AddSecurityDefinition("Bearer", new() { Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "JWT" });
    options.AddSecurityRequirement(new() { [new() { Reference = new() { Type = ReferenceType.SecurityScheme, Id = "Bearer" } }] = [] });
});
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
{
    var origins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
    if (origins.Length > 0) policy.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod();
}));
builder.Services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database", tags: ["ready"]);
var app = builder.Build();
app.UseExceptionHandler();
app.UseStatusCodePages();
if (!app.Environment.IsDevelopment()) { app.UseHsts(); app.UseHttpsRedirection(); }
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
if (app.Environment.IsDevelopment()) { app.UseSwagger(); app.UseSwaggerUI(); }
app.MapControllers();
app.MapHealthChecks("/health/live", new() { Predicate = _ => false }).AllowAnonymous();
app.MapHealthChecks("/health/ready", new() { Predicate = c => c.Tags.Contains("ready") }).AllowAnonymous();
app.Run();

namespace MixPilot.Api { public partial class Program; }

