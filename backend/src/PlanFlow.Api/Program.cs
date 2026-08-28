using PlanFlow.Api;
using PlanFlow.Api.AuthPolicy;
using PlanFlow.Api.Middleware;
using PlanFlow.Application;
using PlanFlow.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddApplicationServices();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddControllers();
builder.Services.AddSwaggerDocumentation();
builder.Services.AddJwtAuthentication(builder.Configuration);
builder.Services.AddRbacPolicies();

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
app.UseAuthorization();

app.MapControllers();

app.Run();
