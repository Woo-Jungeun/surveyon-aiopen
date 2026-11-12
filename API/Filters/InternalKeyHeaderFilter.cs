using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace SurveyonApi.Filters
{
    public class InternalKeyHeaderFilter : IOperationFilter
    {
        private readonly IConfiguration _configuration;

        public InternalKeyHeaderFilter(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public void Apply(OpenApiOperation operation, OperationFilterContext context)
        {
            if (operation.Parameters == null)
                operation.Parameters = new List<OpenApiParameter>();

            // appsettings.json에서 키 가져오기
            var internalKey = _configuration["InternalAuth:InternalKey"];

            // 이미 존재하는지 확인 (예: [FromHeader]로 정의된 경우)
            var existingParam = operation.Parameters.FirstOrDefault(p => p.Name.Equals("X-Internal-Key", StringComparison.OrdinalIgnoreCase) && p.In == ParameterLocation.Header);

            if (existingParam != null)
            {
                // 이미 존재하면 기본값만 설정
                if (!string.IsNullOrEmpty(internalKey))
                {
                    if (existingParam.Schema == null) existingParam.Schema = new OpenApiSchema();
                    existingParam.Schema.Default = new OpenApiString(internalKey);
                }
            }
            else
            {
                // Swagger UI에서 디버깅 시 기본값으로 자동 전달되도록 헤더 추가
                operation.Parameters.Add(new OpenApiParameter
                {
                    Name = "X-Internal-Key",
                    In = ParameterLocation.Header,
                    Required = false,
                    Schema = new OpenApiSchema
                    {
                        Type = "string",
                        Default = !string.IsNullOrEmpty(internalKey) ? new OpenApiString(internalKey) : null
                    },
                    Description = "내부 시스템 인증 키 (개발/Swagger 디버깅용 기본값)"
                });
            }
        }
    }
}
