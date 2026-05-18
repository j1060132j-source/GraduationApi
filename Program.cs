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
// [修正版] 學姊專用：B11 秘密後台看板 (含名次、越早越靠前)
// ==========================================
app.MapGet("/b11-admin-secret-view", (AppDbContext db) =>
{
    // 💡 核心排序修正：先比總章數(多到少)，再比最後更新時間(OrderBy 升冪 = 越早越前面)
    var stats = db.StampRecords
        .GroupBy(r => r.StudentId)
        .Select(g => new
        {
            StudentId = g.Key,
            TotalStamps = g.Count(),
            LastUpdate = g.Max(r => r.ScanTime)
        })
        .OrderByDescending(x => x.TotalStamps)
        .ThenBy(x => x.LastUpdate) // 👈 關鍵修正：由 ThenByDescending 改為 ThenBy
        .ToList();

    var html = @"
    <!DOCTYPE html>
    <html>
    <head>
        <title>實境解謎 - 後台看版</title>
        <meta name='viewport' content='width=device-width, initial-scale=1'>
        <style>
            body { font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif; padding: 20px; background-color: #0f172a; color: #f8fafc; }
            h2 { color: #60a5fa; text-align: center; letter-spacing: 2px; }
            table { width: 100%; max-width: 650px; margin: 0 auto; border-collapse: collapse; background: #1e293b; box-shadow: 0 4px 15px rgba(0,0,0,0.5); border-radius: 12px; overflow: hidden; border: 1px solid #334155; }
            th, td { padding: 15px; text-align: center; border-bottom: 1px solid #334155; }
            th { background-color: #1e3a8a; color: #bfdbfe; font-weight: bold; letter-spacing: 1px; }
            tr:hover { background-color: #334155; }
            .complete { color: #fbbf24; font-weight: 900; text-shadow: 0 0 10px rgba(251,191,36,0.4); }
            .rank-badge { display: inline-block; width: 24px; height: 24px; line-height: 24px; border-radius: 50%; font-weight: bold; }
            .rank-1 { background: #f59e0b; color: #1e293b; } /* 金牌 */
            .rank-2 { background: #94a3b8; color: #1e293b; } /* 銀牌 */
            .rank-3 { background: #b45309; color: #f8fafc; } /* 銅牌 */
        </style>
    </head>
    <body>
        <h2>🔍 集章即時戰況</h2>
        <table>
            <thead>
                <tr>
                    <th style='width: 15%'>名次</th>
                    <th style='width: 30%'>學號</th>
                    <th style='width: 25%'>已集章數</th>
                    <th style='width: 30%'>最後更新時間</th>
                </tr>
            </thead>
            <tbody>
    ";

    int rank = 1; // 👈 宣告名次計數器
    foreach (var stat in stats)
    {
        var taiwanTime = stat.LastUpdate.AddHours(8).ToString("yyyy/MM/dd HH:mm:ss");
        var stampText = stat.TotalStamps >= 8 ? "<span class='complete'>👑 8 (已破關)</span>" : stat.TotalStamps.ToString();

        // 幫前三名做個漂亮的徽章樣式
        string rankDisplay = rank.ToString();
        if (rank == 1) rankDisplay = "<span class='rank-badge rank-1'>1</span>";
        else if (rank == 2) rankDisplay = "<span class='rank-badge rank-2'>2</span>";
        else if (rank == 3) rankDisplay = "<span class='rank-badge rank-3'>3</span>";

        html += $@"
            <tr>
                <td>{rankDisplay}</td>
                <td style='font-weight: bold;'>{stat.StudentId}</td>
                <td>{stampText}</td>
                <td style='font-size: 0.85em; color: #94a3b8;'>{taiwanTime}</td>
            </tr>
        ";
        rank++; // 進入下一圈迴圈，名次自動加 1
    }

    html += @"
            </tbody>
        </table>
    </body>
    </html>
    ";

    return Results.Content(html, "text/html; charset=utf-8");
});

app.MapGet("/b11-admin-secret-detail", (AppDbContext db) =>
{
    // 💡 詳細查帳版同步修正排序邏輯
    var stats = db.StampRecords
        .GroupBy(r => r.StudentId)
        .Select(g => new
        {
            StudentId = g.Key,
            TotalStamps = g.Count(),
            LastUpdate = g.Max(r => r.ScanTime),
            Records = g.OrderBy(r => r.StationId).Select(r => new { r.StationId, r.ScanTime }).ToList()
        })
        .OrderByDescending(x => x.TotalStamps)
        .ThenBy(x => x.LastUpdate) // 👈 同步修正為 ThenBy 升冪
        .ToList();

    var html = @"
    <!DOCTYPE html>
    <html>
    <head>
        <title>實境解謎 - 詳細戰況查詢</title>
        <meta name='viewport' content='width=device-width, initial-scale=1'>
        <style>
            body { font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif; padding: 20px; background-color: #0f172a; color: #f8fafc; }
            h2 { color: #818cf8; text-align: center; letter-spacing: 2px; }
            table { width: 100%; max-width: 950px; margin: 0 auto; border-collapse: collapse; background: #1e293b; box-shadow: 0 4px 15px rgba(0,0,0,0.5); border-radius: 12px; overflow: hidden; border: 1px solid #334155; }
            th, td { padding: 15px; text-align: left; border-bottom: 1px solid #334155; line-height: 1.6; }
            th { background-color: #312e81; color: #c7d2fe; font-weight: bold; text-align: center; }
            tr:hover { background-color: #334155; }
            .complete { color: #fbbf24; font-weight: 900; }
            .detail-badge { display: inline-block; background: #3b82f6; color: white; padding: 4px 8px; border-radius: 6px; font-size: 0.8em; margin: 3px; border: 1px solid #60a5fa; }
            .rank-text { font-weight: bold; text-align: center; color: #94a3b8; }
        </style>
    </head>
    <body>
        <h2>🔍 集章詳細查詢系統</h2>
        <table>
            <thead>
                <tr>
                    <th style='width: 10%'>名次</th>
                    <th style='width: 15%'>學號</th>
                    <th style='width: 15%'>總進度</th>
                    <th style='width: 60%'>各關卡收集詳細時間</th>
                </tr>
            </thead>
            <tbody>
    ";

    int rank = 1; // 👈 詳細版也加入名次計數器
    foreach (var stat in stats)
    {
        var stampText = stat.TotalStamps >= 8 ? "<span class='complete'>👑 8 (破關)</span>" : stat.TotalStamps.ToString();

        var detailsHtml = "";
        foreach (var r in stat.Records)
        {
            var tTime = r.ScanTime.AddHours(8).ToString("HH:mm:ss");
            detailsHtml += $"<span class='detail-badge'>第 {r.StationId} 關: {tTime}</span>";
        }

        html += $@"
            <tr>
                <td class='rank-text'>{rank}</td>
                <td style='font-weight: bold; text-align: center;'>{stat.StudentId}</td>
                <td style='text-align: center;'>{stampText}</td>
                <td>{detailsHtml}</td>
            </tr>
        ";
        rank++;
    }

    html += @"
            </tbody>
        </table>
    </body>
    </html>
    ";

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