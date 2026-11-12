using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Microsoft.AspNetCore.Http;

namespace SurveyonApi.Models
{
    public class ProListRequest
    {
        public string gb { get; set; } = string.Empty;
        public string user { get; set; } = string.Empty;
        public string projectnum { get; set; } = string.Empty;
        public string qnum { get; set; } = string.Empty;
        public string qid { get; set; } = string.Empty;
        public string merge_qnum { get; set; } = string.Empty;
        public string servername { get; set; } = string.Empty;
        public string column { get; set; } = string.Empty;
        public string value { get; set; } = string.Empty;
        public JToken? data { get; set; }
        public JToken? data_lb { get; set; }
        public string? datajson { get; set; }
        public List<string>? ids { get; set; }
        public List<string>? merge_qnums { get; set; }
        public int skipSpinner { get; set; } = 0;
    }

    public class ProListQnumDeleteRequest
    {
        public string projectnum { get; set; } = string.Empty;
        public List<string>? qnums { get; set; }
        public List<int>? ids { get; set; }
    }

    public class LvCodeItem
    {
        public string lv3 { get; set; } = string.Empty;
        public string lv123code { get; set; } = string.Empty;
        public string lv2code { get; set; } = string.Empty;
        public string lv2 { get; set; } = string.Empty;
        public string lv1code { get; set; } = string.Empty;
        public string lv1 { get; set; } = string.Empty;
        public string qnum { get; set; } = string.Empty;
        public string ex_gubun { get; set; } = string.Empty;
    }

    public class ProListImportLbAllRequest
    {
        public string user { get; set; } = string.Empty;
        public string projectnum { get; set; } = string.Empty;
        public string gb { get; set; } = "import_lb_all";
        public JToken? data_lb { get; set; }
    }

    public class ProListImportLbAllExcelRequest
    {
        public IFormFile? file { get; set; }
        public string? projectnum { get; set; }
        public string? user { get; set; }
    }

    public enum GroupPosition
    {
        Start,
        Middle,
        End,
        Single
    }

    public static class GTagHelper
    {
        public static string ToTag(string baseCode, GroupPosition pos)
        {
            switch (pos)
            {
                case GroupPosition.Start:
                    return $"<{baseCode}b>";
                case GroupPosition.End:
                    return $"<{baseCode}e>";
                case GroupPosition.Single:
                    return $"<{baseCode}e>";
                case GroupPosition.Middle:
                default:
                    return $"<{baseCode}p>";
            }
        }
    }

    public class BrandItem
    {
        public int Code { get; set; }
        public string Text { get; set; } = string.Empty;
        public string Lv1 { get; set; } = string.Empty;
        public string Lv2 { get; set; } = string.Empty;
        public string Lv3 { get; set; } = string.Empty;

        public string Lv1Code => "g3";
        public string Lv2Code => "g2";
        public string Lv3Code => "g1";

        public int Lv1lbCode { get; set; } = int.MaxValue;
        public int Lv2lbCode { get; set; } = int.MaxValue;
    }

    public class BrandItemWithPos : BrandItem
    {
        public GroupPosition Lv1Pos { get; set; } = GroupPosition.Middle;
        public GroupPosition Lv2Pos { get; set; } = GroupPosition.Middle;
        public GroupPosition Lv3Pos { get; set; } = GroupPosition.Middle;
        public GroupPosition Lv1GroupPos { get; set; } = GroupPosition.Middle;
        public GroupPosition Lv2GroupPos { get; set; } = GroupPosition.Middle;
        public string Lv123GroupItemsLv1 { get; set; } = string.Empty;
        public string Lv123GroupItems { get; set; } = string.Empty;
    }

    public static class BrandItemHelper
    {
        public static string BuildCodeRangeString(IEnumerable<BrandItem> arr)
        {
            if (arr == null)
                return string.Empty;

            var codes = arr
                .Select(i => i.Code)
                .Distinct()
                .OrderBy(i => i)
                .ToList();

            if (codes.Count == 0)
                return string.Empty;

            var ranges = new List<string>();
            int start = codes[0];
            int prev = codes[0];

            for (int i = 1; i < codes.Count; i++)
            {
                int current = codes[i];
                if (current == prev + 1)
                {
                    prev = current;
                }
                else
                {
                    ranges.Add(start == prev ? start.ToString() : $"{start}..{prev}");
                    start = prev = current;
                }
            }

            ranges.Add(start == prev ? start.ToString() : $"{start}..{prev}");
            return string.Join("+", ranges);
        }
    }
}
