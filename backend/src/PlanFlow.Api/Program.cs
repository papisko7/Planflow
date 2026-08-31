using PlanFlow.Api;
using PlanFlow.Api.AuthPolicy;
using PlanFlow.Api.Middleware;
using PlanFlow.Application;
using PlanFlow.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddApplicationServices();
builder.Services.AddInfrastructure(builder.Configuration, builder.Environment);
builder.Services.AddControllers();
builder.Services.AddSwaggerDocumentation();
builder.Services.AddJwtAuthentication(builder.Configuration);
builder.Services.AddRbacPolicies();
builder.Services.AddApiRateLimiting();

var app = builder.Build();

// Catches every downstream exception first, so both Swagger and the controllers below get
// consistent ProblemDetails responses for Application-layer exceptions.
app.UseMiddleware<ExceptionHandlingMiddleware>();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwaggerDocumentation();
}

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

app.MapControllers();

app.Run();

// Exposes the implicit Program class to WebApplicationFactory<Program> in the integration test project.
public partial class Program { }
