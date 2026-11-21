using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace SaabWebProject.Areas.Mdr.Services
{
    // =====================================================
    // MDR | Phase 8 | هوش مصنوعی سازمانی — ابزارهای متنیِ قاعده‌محور (بدون مدل بیرونی، بدون شبکه)
    // همه‌ی خروجی‌ها «استخراجی» هستند: فقط جمله‌ها و عبارت‌های خودِ متن مبدأ را برمی‌گردانند، چیزی از خود نمی‌سازند.
    // =====================================================
    public class ActionItem { public string Title { get; set; } public DateTime? Due { get; set; } }

    public static class MdrAiText
    {
        private static readonly HashSet<string> Stop = new HashSet<string>
        {
            "وضعیت", "چه", "چی", "چیست", "چیه", "کدام", "کدامند", "کجا", "کجاست", "کی", "چرا", "چگونه", "چطور", "چطوری", "آیا", "است", "هست", "هستند", "بود", "شد", "شده", "شود", "می", "را", "از", "به", "در", "با", "که", "این", "آن", "اين",
            "و", "یا", "برای", "تا", "هم", "من", "ما", "شما", "او", "بر", "ها", "های", "درباره", "مورد", "لطفا", "لطفاً", "بگو", "بگویید", "بده", "بدهید", "نشان", "کن", "کنید", "دارد", "دارم", "داریم", "باید", "هر", "همه",
            "the", "is", "of", "and", "to", "in"
        };

        private static readonly Regex Word = new Regex(@"[\p{L}\p{Nd}]+", RegexOptions.Compiled);
        private static readonly Regex SentenceSplit = new Regex(@"(?<=[\.\!\?؟۔])\s+|[\r\n]+", RegexOptions.Compiled);
        private static readonly Regex Spaces = new Regex(@"\s+", RegexOptions.Compiled);
        private static readonly Regex Email = new Regex(@"[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}", RegexOptions.Compiled);
        private static readonly Regex LongNumber = new Regex(@"\d(?:[\s\-]?\d){7,}", RegexOptions.Compiled);
        private static readonly Regex ActionWord = new Regex(@"لطفا|لطفاً|بایست|باید|نیاز است|ضروری|اقدام|پیگیری|ارسال کنید|ارسال شود|تهیه کنید|تهیه شود|انجام دهید|انجام شود|تا تاریخ|حداکثر تا|مهلت", RegexOptions.Compiled);
        private static readonly Regex JalaliDate = new Regex(@"(?<![\d/])(13|14)\d{2}/\d{1,2}/\d{1,2}(?![\d/])", RegexOptions.Compiled);

        public static string Fix(string s)
        {
            return MdrSearchService.Normalize(s ?? "").Replace('ي', 'ی').Replace('ك', 'ک');
        }

        // کلیدواژه‌های پرسش: بدون کلمه‌های پرسشی/ربطی، بلندترین‌ها اول (اختصاصی‌تر)، حداکثر max و مجموع طول ≤ ۹۵ (سقف جستجو ۱۰۰ حرف)
        public static string[] Keywords(string q, int max = 5)
        {
            var toks = Word.Matches(Fix(q)).Cast<Match>().Select(m => m.Value).Where(t => t.Length >= 2 && !Stop.Contains(t)).Distinct().ToList();
            var res = new List<string>(); var total = 0;
            foreach (var t in toks.OrderByDescending(x => x.Length).ThenBy(x => x, StringComparer.Ordinal))
            {
                if (res.Count >= max || total + t.Length + 1 > 95) continue;
                res.Add(t); total += t.Length + 1;
            }
            return res.ToArray();
        }

        // پوشش داده‌ی شخصی پیش از ذخیره: ایمیل و رشته‌های عددیِ بلند (کدملی، تلفن، شماره‌ی حساب/کارت)
        public static string Mask(string s)
        {
            s = Fix(s);
            s = Email.Replace(s, "[ایمیل]");
            s = LongNumber.Replace(s, "[عدد]");
            return Spaces.Replace(s, " ").Trim();
        }

        public static List<string> Sentences(string text)
        {
            return SentenceSplit.Split(Fix(text)).Select(x => Spaces.Replace(x, " ").Trim()).Where(x => x.Length >= 3).ToList();
        }

        private static List<string> Tokens(string s)
        {
            return Word.Matches(Fix(s)).Cast<Match>().Select(m => m.Value).Where(t => t.Length >= 2 && !Stop.Contains(t)).ToList();
        }

        // خلاصه‌ی استخراجی: ≤ ۳ جمله با بیشترین بسامد واژه‌ی محتوایی، به ترتیب اصلی متن. متن کوتاه (<۲۰۰ حرف) خلاصه ندارد.
        public static string Summarize(string text, int maxSentences = 3, int maxChars = 600)
        {
            if (string.IsNullOrWhiteSpace(text) || Spaces.Replace(text, " ").Trim().Length < 200) return null;
            var sents = Sentences(text);
            if (sents.Count <= maxSentences) return Cut(string.Join(" ", sents), maxChars);
            var freq = new Dictionary<string, int>();
            foreach (var t in Tokens(text)) { int c; freq.TryGetValue(t, out c); freq[t] = c + 1; }
            var scored = sents.Select((s, i) =>
            {
                var tk = Tokens(s);
                var score = tk.Count == 0 ? 0 : tk.Sum(t => freq[t]) / Math.Sqrt(tk.Count);
                return new { i, s, score };
            }).OrderByDescending(x => x.score).ThenBy(x => x.i).Take(maxSentences).OrderBy(x => x.i).Select(x => x.s);
            return Cut(string.Join(" ", scored), maxChars);
        }

        // جمله‌هایی که شبیه درخواست اقدام‌اند؛ مهلت فقط اگر تاریخ شمسی آینده در همان جمله آمده باشد
        public static List<ActionItem> ActionItems(string text, int max = 5)
        {
            var res = new List<ActionItem>();
            foreach (var s in Sentences(text))
            {
                if (!ActionWord.IsMatch(s)) continue;
                var title = Cut(s, 200);
                if (res.Any(r => r.Title == title)) continue;
                res.Add(new ActionItem { Title = title, Due = FindDue(s) });
                if (res.Count >= max) break;
            }
            return res;
        }

        private static DateTime? FindDue(string s)
        {
            var m = JalaliDate.Match(s);
            if (!m.Success) return null;
            var p = m.Value.Split('/');
            try
            {
                var d = new PersianCalendar().ToDateTime(int.Parse(p[0]), int.Parse(p[1]), int.Parse(p[2]), 0, 0, 0, 0).Date;
                return d >= DateTime.Today ? d : (DateTime?)null;
            }
            catch { return null; }
        }

        // شباهت واژگانی (Jaccard) به درصد
        public static int Similarity(string a, string b)
        {
            var x = new HashSet<string>(Tokens(a)); var y = new HashSet<string>(Tokens(b));
            if (x.Count == 0 || y.Count == 0) return 0;
            var inter = x.Count(t => y.Contains(t));
            var union = x.Count + y.Count - inter;
            return union == 0 ? 0 : (int)Math.Round(100.0 * inter / union);
        }

        // چند کلیدواژه از کلیدواژه‌های پرسش در متن آمده است
        public static int Matched(string[] keywords, string text)
        {
            var t = Fix(text ?? "");
            return keywords.Count(k => MdrSearchService.Variants(k).Any(v => t.IndexOf(v, StringComparison.OrdinalIgnoreCase) >= 0));
        }

        public static string Cut(string s, int max)
        {
            s = Spaces.Replace(s ?? "", " ").Trim();
            return s.Length <= max ? s : s.Substring(0, max - 1).TrimEnd() + "…";
        }
    }
}
