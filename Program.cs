using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// [新增] 1. 設定允許跨網域連線 (CORS)
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()   // 允許任何網域
              .AllowAnyHeader()   // 允許任何標頭
              .AllowAnyMethod();  // 允許任何方法 (GET, POST 等)
    });
});

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql("Host=ep-patient-mountain-ao08zl8r.c-2.ap-southeast-1.aws.neon.tech;Database=neondb;Username=neondb_owner;Password=npg_hfVjSzT4n1dt;SslMode=Require;"));

var app = builder.Build();

// [新增] 2. 啟用 CORS (必須放在 builder.Build() 之後)
app.UseCors("AllowAll");
// [新增] 終極 CORS 攔截器：專治 Safari 的囉嗦檢查
// ==========================================
app.Use(async (context, next) =>
{
    if (context.Request.Method == "OPTIONS")
    {
        context.Response.Headers["Access-Control-Allow-Origin"] = "*";
        context.Response.Headers["Access-Control-Allow-Headers"] = "*";
        context.Response.Headers["Access-Control-Allow-Methods"] = "*";
        context.Response.StatusCode = 200;
        return; // 直接秒退回 200 OK，不再往下跑
    }
    await next();
});
// ==========================================

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
}

app.MapGet("/", () => "畢業集章系統 API 運作中！");

app.MapGet("/api/progress/{studentId}", (string studentId, AppDbContext db) =>
{
    var records = db.StampRecords
                    .Where(r => r.StudentId == studentId)
                    .Select(r => r.StationId)
                    .ToList();
    return Results.Ok(new { StudentId = studentId, CollectedStamps = records });
});

app.MapPost("/api/stamp", (StampRequest req, AppDbContext db) =>
{
    try
    {
        var exists = db.StampRecords.Any(r => r.StudentId == req.StudentId && r.StationId == req.StationId);
        if (exists) return Results.BadRequest(new { Message = "這關你已經集過囉！" });

        // 【終極修復】PostgreSQL 拒絕接受 DateTime.Now，必須強制改用 DateTime.UtcNow！
        var newRecord = new StampRecord
        {
            StudentId = req.StudentId,
            StationId = req.StationId,
            ScanTime = DateTime.UtcNow
        };

        db.StampRecords.Add(newRecord);
        db.SaveChanges(); // 剛剛就是死在這裡，現在換成 UtcNow 就會暢通無阻！

        return Results.Ok(new { Message = "集章成功！", Station = req.StationId });
    }
    catch (Exception ex)
    {
        // 加上防彈衣：如果未來還有錯，把真實錯誤包裝成 JSON 傳給前端，避免觸發 Load failed
        return Results.Json(new { Message = $"寫入失敗: {ex.Message}" }, statusCode: 500);
    }
});

app.Run();

class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }
    public DbSet<StampRecord> StampRecords { get; set; }
}

class StampRecord
{
    public int Id { get; set; }
    public string StudentId { get; set; } = string.Empty;
    public int StationId { get; set; }
    public DateTime ScanTime { get; set; }
}

class StampRequest
{
    public string StudentId { get; set; } = string.Empty;
    public int StationId { get; set; }
}