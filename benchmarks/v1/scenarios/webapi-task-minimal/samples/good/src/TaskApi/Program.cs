using TaskApi;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddProblemDetails();

var databasePath = ResolveDatabasePath(builder.Environment.ContentRootPath);
var store = new TaskStore($"Data Source={databasePath}");
store.EnsureCreated();
builder.Services.AddSingleton(store);

var app = builder.Build();
app.UseStatusCodePages();
TaskEndpoints.Map(app);
app.Run();

static string ResolveDatabasePath(string contentRoot)
{
    var configured = Environment.GetEnvironmentVariable("TASKS_DB_PATH");
    if (string.IsNullOrWhiteSpace(configured))
    {
        return Path.Combine(contentRoot, "tasks.db");
    }

    return Path.IsPathRooted(configured) ? configured : Path.Combine(contentRoot, configured);
}

// Exposed so the WebApplicationFactory-based integration tests can locate the entry point.
public partial class Program;
