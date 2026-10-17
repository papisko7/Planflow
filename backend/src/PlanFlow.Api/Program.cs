using PlanFlow.Api;
using PlanFlow.Api.AuthPolicy;
using PlanFlow.Api.Hubs;
using PlanFlow.Api.Middleware;
using PlanFlow.Application;
using PlanFlow.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddApplicationServices();
builder.Services.AddInfrastructure(builder.Configuration, builder.Environment);
builder.Services.AddControllers().AddJsonOptions(o =>
{
    o.JsonSerializerOptions.Converters.Add(new PlanFlow.Api.Common.UtcDateTimeConverter());
    o.JsonSerializerOptions.Converters.Add(new PlanFlow.Api.Common.NullableUtcDateTimeConverter());
});
builder.Services.AddSwaggerDocumentation();
builder.Services.AddJwtAuthentication(builder.Configuration);
builder.Services.AddRbacPolicies();
builder.Services.AddApiRateLimiting();
var signalR = builder.Services.AddSignalR();

// With several server instances a chat message sent through instance A must also reach people connected to instance B;
// the Redis backplane forwards hub messages between instances. Off by default so a single instance (or a dev machine
// without Redis) keeps working — enable it with "SignalR:RedisBackplane": true.
if (builder.Configuration.GetValue<bool>("SignalR:RedisBackplane"))
{
    signalR.AddStackExchangeRedis(builder.Configuration.GetConnectionString("Redis")!,
        options => options.Configuration.ChannelPrefix = StackExchange.Redis.RedisChannel.Literal("planflow-signalr"));
}

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
app.MapHub<ChatHub>("/hubs/chat");

app.Run();

// Exposes the implicit Program class to WebApplicationFactory<Program> in the integration test project.
public partial class Program { }
