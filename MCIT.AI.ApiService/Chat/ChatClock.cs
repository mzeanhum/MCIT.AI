using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace MCIT.AI.ApiService.Chat;

internal sealed partial class ChatClock(TimeProvider timeProvider, TimeZoneInfo timeZone)
{
    public DateTimeOffset LocalNow => TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), timeZone);
    public string TimeZoneId => timeZone.Id;

    public static string? AnswerCurrentInformation(string prompt, DateTimeOffset localNow)
    {
        string question = QuestionSeparators().Replace(ArabicMarks().Replace(prompt.Normalize(NormalizationForm.FormKC), string.Empty), " ")
            .Trim()
            .ToLowerInvariant()
            .Replace('’', '\'')
            .Replace('أ', 'ا')
            .Replace('إ', 'ا')
            .Replace('آ', 'ا');
        string isoDate = localNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        string time = localNow.ToString("HH:mm", CultureInfo.InvariantCulture);
        return question switch
        {
            "كم تاريخ اليوم" or "ما هو تاريخ اليوم" or "ماهو تاريخ اليوم" or "ما تاريخ اليوم" or "تاريخ اليوم"
                or "وش تاريخ اليوم" or "ايش تاريخ اليوم" or "النهارده تاريخ كام" or "تاريخ اليوم كم"
                => $"تاريخ اليوم {isoDate}.",
            "what is today's date" or "what's today's date" or "what is the date today"
                or "what's the date today" or "today's date" or "today date" or "current date"
                => $"Today's date is {isoDate}.",
            "كم الساعة الان" or "الساعة كم الان" or "كم الساعة" or "الساعة كم" or "كم الساعه الان"
                or "ما الوقت الان" or "ما هو الوقت الان" or "ماهو الوقت الان" or "الساعة الان"
                or "الساعة كام دلوقتي" or "الوقت الان"
                => $"الساعة الآن {time}.",
            "what time is it" or "what time is it now" or "what is the current time" or "current time"
                => $"The current time is {time}.",
            "ما هو سعر الذهب اليوم" or "ماهو سعر الذهب اليوم" or "كم سعر الذهب اليوم" or "ما سعر الذهب اليوم"
                or "كم سعر الذهب" or "ما هو سعر الذهب" or "ماهو سعر الذهب" or "ما سعر الذهب" or "سعر الذهب"
                or "سعر الذهب اليوم" or "سعر الذهب الان" or "كم سعر الذهب الان" or "ما هو سعر الذهب الان"
                => "لا يتوفر لدي مصدر مباشر لأسعار الذهب، لذلك لا أستطيع إعطاء السعر الحالي بدقة.",
            "what is the gold price today" or "what is today's gold price" or "what's the gold price today"
                or "gold price today" or "current gold price"
                => "I don't have a live gold price feed, so I can't give you an accurate current quote.",
            _ => null
        };
    }

    [GeneratedRegex(@"[\u0640\u064B-\u065F\u0670]")]
    private static partial Regex ArabicMarks();

    [GeneratedRegex(@"[\s؟?!.,،]+")]
    private static partial Regex QuestionSeparators();
}
