using SurveyonApi.Infrastructure;
using SurveyonApi.Services;
using SurveyonApi.Filters;
using SurveyonApi.Repositories;
using SurveyonApi.Hubs;
using Microsoft.Extensions.DependencyInjection;

// Aspose.Cells 및 엑셀 1252 인코딩 지원 등록
System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

var builder = WebApplication.CreateBuilder(args);

// 글로벌 FormOptions 및 Kestrel 대용량 요청 본문(100MB) 설정
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 100 * 1024 * 1024; // 100MB
});

builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 100 * 1024 * 1024; // 100MB
    options.ValueLengthLimit = 100 * 1024 * 1024;
});

builder.Services.Configure<IISServerOptions>(options =>
{
    options.MaxRequestBodySize = 100 * 1024 * 1024; // 100MB
});

// 컨테이너에 서비스 추가.
builder.Services.AddControllers(options =>
{
    options.Filters.Add<AuthCheckFilter>(); // 모든 컨트롤러에 인증 필터 적용
})
.AddNewtonsoftJson();


// ── AuthCore 원격 토큰 검증 및 IMemoryCache 등록 ──
builder.Services.Configure<AuthOptions>(builder.Configuration.GetSection("Auth"));
builder.Services.AddMemoryCache();
builder.Services.AddHttpClient("AuthCore", (sp, client) =>
{
    var authOpts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<AuthOptions>>().Value;
    var baseUrl = !string.IsNullOrWhiteSpace(authOpts.AuthCoreBaseUrl)
        ? authOpts.AuthCoreBaseUrl
        : "https://stest.hrc.kr/APIs/o/";
    if (!baseUrl.EndsWith("/")) baseUrl += "/";
    client.BaseAddress = new Uri(baseUrl);
    client.Timeout = TimeSpan.FromSeconds(15);
});

builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo { Title = "SurveyonApi", Version = "v1" });

    // Swagger에서 X-Auth-Token 헤더 입력 지원
    c.AddSecurityDefinition("X-Auth-Token", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Description = "인증 토큰을 입력하세요",
        Name = "X-Auth-Token",
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.ApiKey
    });

    c.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new Microsoft.OpenApi.Models.OpenApiReference
                {
                    Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                    Id = "X-Auth-Token"
                }
            },
            new string[] { }
        }
    });

    // Swagger에서 Authorization 헤더 입력 지원 (Bearer 등)
    c.AddSecurityDefinition("Authorization", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Description = "Authorization 헤더를 입력하세요 (예: Bearer <token>)",
        Name = "Authorization",
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.ApiKey
    });

    c.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new Microsoft.OpenApi.Models.OpenApiReference
                {
                    Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                    Id = "Authorization"
                }
            },
            new string[] { }
        }
    });

    // X-Internal-Key 헤더 자동 추가 필터
    c.OperationFilter<InternalKeyHeaderFilter>();
});

// CORS — React 개발 서버 허용 (http://localhost:3000, http://localhost:5173) 및 SignalR 웹소켓 자격증명 허용
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.SetIsOriginAllowed(_ => true)
     .AllowAnyHeader()
     .AllowAnyMethod()
     .AllowCredentials()));

// SignalR 실시간 통신 서비스 등록
builder.Services.AddSignalR();

// DbService 등록 (기존 유지 또는 제거 가능)
builder.Services.AddScoped<IDbService, DbService>();
builder.Services.AddScoped<IAuthService, AuthService>();
// 새로운 Lib 등록
builder.Services.AddScoped<IPostgreLib, PostgreLib>();
builder.Services.AddScoped<IMsSqlLib, MsSqlLib>();
// Repository 등록
builder.Services.AddScoped<IMainRepository, MainRepository>();
builder.Services.AddScoped<INewOldJsonService, NewOldJsonService>();
builder.Services.AddScoped<JsonHong>();

// AI오픈분석 › 분석 페이지 (/analysis/*)
builder.Services.AddScoped<IAnalysisRepository, AnalysisRepository>();
builder.Services.AddScoped<IAnalysisService, AnalysisService>();
builder.Services.AddSingleton<IWorkerService, WorkerService>();
builder.Services.AddSingleton<IQueueSendingService, QueueSendingService>();

// 프로젝트 동기화 프로그래스 진행 상태 관리 서비스 등록 (Singleton)
builder.Services.AddSingleton<IProjectProgressService, ProjectProgressService>();

builder.Services.AddHttpClient(); // 외부 API 호출용

var app = builder.Build();

// HTTP 요청 파이프라인 구성.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
else
{
    app.UseExceptionHandler("/error");
}
app.UseHttpsRedirection();
app.UseCors();
app.UseAuthorization();
app.UseSession();

app.MapControllers();
app.MapHub<WorkerHub>("/hubs/worker");

app.Run();

