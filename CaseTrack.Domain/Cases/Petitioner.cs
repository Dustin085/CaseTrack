namespace CaseTrack.Domain.Cases;

// 陳情人資料：送件當時的快照，是值物件（內容相同就相等，沒有自己的 Id）。
// 屬性刻意只有 get、沒有 init：record 的 with 會複製物件但不經過建構子，有 init 就能繞過下面的驗證。
public sealed record Petitioner
{
    public string Name { get; }
    public string? Phone { get; }
    public string? Email { get; }
    public string? Address { get; }

    public Petitioner(string? name, string? phone, string? email, string? address)
    {
        var normalizedName = Normalize(name)
            ?? throw new InvalidPetitionerException("陳情人姓名為必填");
        var normalizedPhone = Normalize(phone);
        var normalizedEmail = Normalize(email);

        // 要求補件、回覆都需要聯絡得到人
        if (normalizedPhone is null && normalizedEmail is null)
        {
            throw new InvalidPetitionerException("電話與 Email 至少需填寫一項");
        }

        Name = normalizedName;
        Phone = normalizedPhone;
        Email = normalizedEmail;
        Address = Normalize(address);
    }

    // 給 EF Core 從資料庫讀出時使用，不經過上面的驗證。
    // 驗證只在「建立」時做，讀取時信任資料庫：規則會隨時間變嚴格，舊資料不符合新規則也必須讀得出來。
    private Petitioner()
    {
        Name = null!;
    }

    // 空白一律視為沒填並存成 null，「沒填」只有一種表示方式
    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
