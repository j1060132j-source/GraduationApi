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

// ==========================================
// [新增] 學姊專用：B11 秘密後台看板
// ==========================================
app.MapGet("/b11-admin-secret-view", (AppDbContext db) =>
{
    // 1. 透過 EF Core 從資料庫撈取並分組統計
    var stats = db.StampRecords
        .GroupBy(r => r.StudentId)
        .Select(g => new
        {
            StudentId = g.Key,
            TotalStamps = g.Count(),
            LastUpdate = g.Max(r => r.ScanTime)
        })
        .OrderByDescending(x => x.TotalStamps)
        .ThenByDescending(x => x.LastUpdate)
        .ToList();

    // 2. 組裝 HTML 畫面
    var html = @"
    <!DOCTYPE html>
    <html>
    <head>
        <title>B11 實境解謎 - 後台看版</title>
        <meta name='viewport' content='width=device-width, initial-scale=1'>
        <style>
            body { font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif; padding: 20px; background-color: #0f172a; color: #f8fafc; }
            h2 { color: #60a5fa; text-align: center; letter-spacing: 2px; }
            table { width: 100%; max-width: 600px; margin: 0 auto; border-collapse: collapse; background: #1e293b; box-shadow: 0 4px 15px rgba(0,0,0,0.5); border-radius: 12px; overflow: hidden; border: 1px solid #334155; }
            th, td { padding: 15px; text-align: center; border-bottom: 1px solid #334155; }
            th { background-color: #1e3a8a; color: #bfdbfe; font-weight: bold; letter-spacing: 1px; }
            tr:hover { background-color: #334155; }
            .complete { color: #fbbf24; font-weight: 900; text-shadow: 0 0 10px rgba(251,191,36,0.4); }
        </style>
    </head>
    <body>
        <h2>🔍 B11 集章即時戰況</h2>
        <table>
            <thead>
                <tr>
                    <th>學號</th>
                    <th>已集章數</th>
                    <th>最後更新時間</th>
                </tr>
            </thead>
            <tbody>
    ";

    // 3. 把每一筆資料塞進表格裡
    foreach (var stat in stats)
    {
        // 你的資料庫是存 UTC 時間，這裡幫學姊轉回台灣時間 (+8) 顯示才不會錯亂
        var taiwanTime = stat.LastUpdate.AddHours(8).ToString("yyyy/MM/dd HH:mm:ss");

        // 滿 8 章顯示金色破關特效
        var stampText = stat.TotalStamps >= 8 ? "<span class='complete'>👑 8 (已破關)</span>" : stat.TotalStamps.ToString();

        html += $@"
            <tr>
                <td style='font-weight: bold;'>{stat.StudentId}</td>
                <td>{stampText}</td>
                <td style='font-size: 0.85em; color: #94a3b8;'>{taiwanTime}</td>
            </tr>
        ";
    }

    html += @"
            </tbody>
        </table>
    </body>
    </html>
    ";

    // 4. 回傳網頁格式
    return Results.Content(html, "text/html; charset=utf-8");
});
// ==========================================

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