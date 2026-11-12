using System.Threading;
using System.Threading.Tasks;

namespace SurveyonApi.Services
{
    public interface INewOldJsonService
    {
        /// <summary>
        /// 레거시 .NET NewOldCeck 이관: project_list 데이터를 기반으로 pn_in 테이블 레코드를 자동 생성/병합 동기화
        /// </summary>
        Task<bool> NewOldCheckAsync(string projectnum, string qnum = "", string qid = "", CancellationToken ct = default);
    }
}
