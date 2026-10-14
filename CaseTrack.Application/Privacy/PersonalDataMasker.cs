using System.Globalization;

namespace CaseTrack.Application.Privacy;

// 個資遮罩：保留前後各約 1/4，中間換成 *。稽核紀錄（Infrastructure）和 API 回應（Api）共用同一套規則。
//   王小明 → 王*明、0912345678 → 09******78、test@example.com → test********.com
public static class PersonalDataMasker
{
    private const char MaskChar = '*';

    public static string? Mask(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        // 用「文字元素」計算長度，不用 string.Length：
        // 罕見字（例如 𠀀）在 .NET 字串裡佔兩個 char，用 char 切會把一個字切成兩半，變成亂碼
        var text = new StringInfo(value);
        var length = text.LengthInTextElements;

        // 太短的值保留前後會等於沒遮：1 個字全遮，2 個字只留第一個
        if (length == 1)
        {
            return MaskChar.ToString();
        }
        if (length == 2)
        {
            return text.SubstringByTextElements(0, 1) + MaskChar;
        }

        var keep = Math.Max(1, length / 4);
        return text.SubstringByTextElements(0, keep)
            + new string(MaskChar, length - keep * 2)
            + text.SubstringByTextElements(length - keep, keep);
    }
}
