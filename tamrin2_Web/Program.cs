using System.Net;
using System.Text;
using System.Text.Json;
using System.Collections.Generic;

#pragma warning disable CS8602
#pragma warning disable CS1998

public record Job(int Id, string Title, string Description);

public class JobServer
{
    private readonly List<Job> _jobs = new();
    private int _nextId = 1;
    private readonly HttpListener _listener = new();
    private readonly string _filePath = "jobs.json";
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public JobServer()
    {
        LoadJobsFromFile();
    }

    public void Start(string url)
    {
        _listener.Prefixes.Add(url);
        _listener.Start();
        Console.WriteLine($"Server started on {url}");
        Task.Run(ListenForRequests);
    }

    private async Task ListenForRequests()
    {
        while (_listener.IsListening)
        {
            try
            {
                var context = await _listener.GetContextAsync();
                _ = Task.Run(() => HandleRequest(context));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }
        }
    }

    private async Task HandleRequest(HttpListenerContext context)
    {
        var request = context.Request;
        var response = context.Response;

        try
        {
            var (statusCode, body) = request.HttpMethod switch
            {
                "GET" when request.Url.AbsolutePath == "/jobs" =>
                    HandleGetRequest(request),

                "POST" when request.Url.AbsolutePath == "/jobs" =>
                    await HandlePostRequest(request),

                "PUT" when request.Url.AbsolutePath.StartsWith("/jobs/") =>
                    await HandlePutRequest(request),

                "DELETE" when request.Url.AbsolutePath.StartsWith("/jobs/") =>
                    HandleDeleteRequest(request),

                _ => (404, "Not Found")
            };

            await WriteResponse(response, statusCode, body);
        }
        catch (Exception ex)
        {
            await WriteResponse(response, 500, $"Internal Server Error: {ex.Message}");
            Console.WriteLine($"Error handling request: {ex}");
        }
    }

    private (int statusCode, string body) HandleGetRequest(HttpListenerRequest request)
    {
        var page = int.TryParse(request.QueryString["page"], out var p) ? p : 1;
        var pageSize = int.TryParse(request.QueryString["pageSize"], out var ps) ? ps : 10;
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 10;

        var paged = _jobs.OrderBy(j => j.Id).Skip((page - 1) * pageSize).Take(pageSize).ToList();
        return (200, JsonSerializer.Serialize(paged, _jsonOptions));
    }

    private async Task<(int statusCode, string body)> HandlePostRequest(HttpListenerRequest request)
    {
        var job = await DeserializeBody<Job>(request);
        if (job.Title.Length > 100 || job.Description.Length > 100)
            return (400, "Title or Description too long (max 100 characters)");

        var newJob = job with { Id = _nextId++ };
        _jobs.Add(newJob);
        SaveJobsToFile();
        return (201, JsonSerializer.Serialize(newJob, _jsonOptions));
    }

    private async Task<(int statusCode, string body)> HandlePutRequest(HttpListenerRequest request)
    {
        var id = ExtractIdFromPath(request.Url.AbsolutePath);
        var existing = _jobs.Find(j => j.Id == id);
        if (existing is null) return (404, "Job not found");

        var updatedJob = await DeserializeBody<Job>(request);
        if (updatedJob.Title.Length > 100 || updatedJob.Description.Length > 100)
            return (400, "Title or Description too long (max 100 characters)");

        _jobs.Remove(existing);
        _jobs.Add(updatedJob with { Id = id });
        SaveJobsToFile();
        return (200, JsonSerializer.Serialize(updatedJob, _jsonOptions));
    }

    private (int statusCode, string body) HandleDeleteRequest(HttpListenerRequest request)
    {
        var id = ExtractIdFromPath(request.Url.AbsolutePath);
        var existing = _jobs.Find(j => j.Id == id);
        if (existing is null) return (404, "Job not found");

        _jobs.Remove(existing);
        SaveJobsToFile();
        return (200, "Job deleted");
    }

    private static int ExtractIdFromPath(string path)
    {
        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 || !int.TryParse(parts[1], out var id))
            throw new ArgumentException("Invalid job ID");
        return id;
    }

    private async Task<T> DeserializeBody<T>(HttpListenerRequest request)
    {
        using var reader = new StreamReader(request.InputStream);
        var body = await reader.ReadToEndAsync();
        return JsonSerializer.Deserialize<T>(body, _jsonOptions)
               ?? throw new InvalidOperationException("Invalid request body");
    }

    private static async Task WriteResponse(HttpListenerResponse response, int statusCode, string body)
    {
        response.StatusCode = statusCode;
        var buffer = Encoding.UTF8.GetBytes(body);
        response.ContentLength64 = buffer.Length;
        await response.OutputStream.WriteAsync(buffer);
        response.OutputStream.Close();
    }

    private void SaveJobsToFile()
    {
        var json = JsonSerializer.Serialize(_jobs, _jsonOptions);
        File.WriteAllText(_filePath, json);
    }

    private void LoadJobsFromFile()
    {
        if (File.Exists(_filePath))
        {
            var json = File.ReadAllText(_filePath);
            var loaded = JsonSerializer.Deserialize<List<Job>>(json, _jsonOptions);
            if (loaded != null)
            {
                _jobs.AddRange(loaded);
                _nextId = _jobs.Max(j => j.Id) + 1;
            }
        }
    }

    public static async Task Main()
    {
        var server = new JobServer();
        server.Start("http://localhost:5000/");
        Console.WriteLine("Press any key to stop the server...");
        Console.ReadKey();
    }
}

/*
start 
dotnet run

show all jobs
curl.exe http://localhost:5000/jobs

show jobs with paging
curl.exe "http://localhost:5000/jobs?page=1&pageSize=5"
paging works like this : page 1 size 5 = job 1-5, page 2 size 5 = job 6-10, . . .

add a new job
curl.exe -X POST http://localhost:5000/jobs -H "Content-Type: application/json" -d "{\"Title\":\"hi\",\"Description\":\"hi\"}"

update job
curl.exe -X PUT http://localhost:5000/jobs/1 -H "Content-Type: application/json" -d "{\"Id\":1,\"Title\":\"hello\",\"Description\":\"hello\"}"

delete job
curl.exe -X DELETE http://localhost:5000/jobs/1

Note: dont use power shell
Note: title and description max 100 chars
Note: data is saved in jobs.json
*/
